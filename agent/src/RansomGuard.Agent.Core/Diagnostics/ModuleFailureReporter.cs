using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RansomGuard.Agent.Core.Persistence.Repositories;

namespace RansomGuard.Agent.Core.Diagnostics;

/// <summary>
/// The single place a detection module reports that it went down. A module
/// swallows its own fatal exception so it cannot take the host — and every
/// other module — with it; a swallowed exception must therefore leave three
/// traces, or it would simply vanish.
/// </summary>
public interface IModuleFailureReporter
{
    /// <summary>
    /// Records that a module stopped: registry state, a Critical log with the
    /// exception, and an entry in the signed audit chain. Never throws.
    /// </summary>
    /// <param name="moduleCode">Stable module code (see <see cref="ModuleCode"/>).</param>
    /// <param name="reachedActive">
    /// True if the module had reached its running state (stopped_unexpectedly),
    /// false if it failed during startup (init_failed).
    /// </param>
    /// <param name="exception">The exception that stopped the module.</param>
    Task ReportStoppedAsync(string moduleCode, bool reachedActive, Exception exception);

    /// <summary>
    /// Records that a module lost events but is still running (e.g. a file
    /// watcher buffer overflow): a Critical log and an audit entry, at most once
    /// per module per <see cref="ModuleFailureReporter.EventsLostWindow"/>, with the
    /// number of losses. The module state is left unchanged because the module is
    /// still watching. Never throws.
    /// </summary>
    /// <param name="moduleCode">Stable module code.</param>
    /// <param name="exception">The exception describing the loss.</param>
    Task ReportEventsLostAsync(string moduleCode, Exception exception);
}

/// <inheritdoc />
public sealed class ModuleFailureReporter : IModuleFailureReporter
{
    /// <summary>Audit action written when a module stops.</summary>
    public const string ModuleStoppedAction = "ModuleStopped";

    /// <summary>Audit action written when a module loses events but keeps running.</summary>
    public const string ModuleEventsLostAction = "ModuleEventsLost";

    /// <summary>
    /// At most one ModuleEventsLost entry (and one Critical log) per module in this
    /// window. During an encryption burst a watcher buffer overflows in a loop; one
    /// signed audit entry per overflow would flood the chain the GRID has to verify.
    /// </summary>
    public static readonly TimeSpan EventsLostWindow = TimeSpan.FromMinutes(1);

    private readonly IModuleStateRegistry _registry;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ModuleFailureReporter> _logger;
    private readonly TimeProvider _time;
    private readonly ConcurrentDictionary<string, EventsLostWindowState> _eventsLost = new(StringComparer.Ordinal);

    /// <summary>Creates the reporter.</summary>
    /// <param name="registry">Runtime module-state registry.</param>
    /// <param name="scopeFactory">Scope factory for the audit repository.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="time">Clock and timers for the events-lost window; null uses the system clock.</param>
    public ModuleFailureReporter(
        IModuleStateRegistry registry,
        IServiceScopeFactory scopeFactory,
        ILogger<ModuleFailureReporter> logger,
        TimeProvider? time = null)
    {
        _registry = registry;
        _scopeFactory = scopeFactory;
        _logger = logger;
        _time = time ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public async Task ReportStoppedAsync(string moduleCode, bool reachedActive, Exception exception)
    {
        string reason = reachedActive ? ModuleReasonCode.StoppedUnexpectedly : ModuleReasonCode.InitFailed;

        // 1. The truth first: the registry is what the next heartbeat reports, and
        //    it cannot fail. Logging and audit come after, so a broken database
        //    can never keep the console believing the module is up.
        _registry.Publish(moduleCode, ModuleState.Inactive, reason);

        // 2. Critical, with the exception, in the local log.
        bool injected = exception is ModuleFaultInjectedException;
        _logger.LogCritical(exception,
            injected
                ? "{Module} stopped ({Reason}) by Development fault injection — protection from this module is down"
                : "{Module} stopped ({Reason}) — protection from this module is down",
            moduleCode, reason);

        // 3. The signed audit chain. fault_injection always says which it was: a provoked stop
        //    never passes for a real failure.
        await AppendAuditAsync(ModuleStoppedAction, moduleCode, reason, exception, extra: new()
        {
            ["fault_injection"] = injected,
        }).ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>
    /// The first loss of a quiet period is reported at once (count 1), so the operator
    /// learns about it without delay. Losses that follow inside the window are only
    /// counted; when the window ends, one entry carries their number and opens the
    /// next window. Each window therefore holds at most one entry.
    /// </remarks>
    public Task ReportEventsLostAsync(string moduleCode, Exception exception)
    {
        EventsLostWindowState window = _eventsLost.GetOrAdd(moduleCode, _ => new EventsLostWindowState());
        lock (window.Gate)
        {
            if (window.Open)
            {
                window.Pending++;
                window.LastException = exception;
                return Task.CompletedTask;
            }

            OpenWindow(moduleCode, window);
        }

        return WriteEventsLostAsync(moduleCode, 1, exception);
    }

    private void OpenWindow(string moduleCode, EventsLostWindowState window)
    {
        window.Open = true;
        window.Pending = 0;
        window.LastException = null;
        window.Timer = _time.CreateTimer(
            _ => _ = CloseWindowAsync(moduleCode, window), null, EventsLostWindow, Timeout.InfiniteTimeSpan);
    }

    private async Task CloseWindowAsync(string moduleCode, EventsLostWindowState window)
    {
        int count;
        Exception last;
        lock (window.Gate)
        {
            window.Timer?.Dispose();
            window.Timer = null;
            if (window.Pending == 0)
            {
                window.Open = false; // quiet: the next loss is reported at once
                return;
            }

            count = window.Pending;
            last = window.LastException!;
            OpenWindow(moduleCode, window); // this entry is the next window's only one
        }

        await WriteEventsLostAsync(moduleCode, count, last).ConfigureAwait(false);
    }

    private async Task WriteEventsLostAsync(string moduleCode, int count, Exception exception)
    {
        _logger.LogCritical(exception,
            "{Module} lost file-system events {Count} time(s) within {WindowSeconds} s (watcher still running)",
            moduleCode, count, (int)EventsLostWindow.TotalSeconds);
        await AppendAuditAsync(ModuleEventsLostAction, moduleCode, reason: null, exception, extra: new()
        {
            ["overflow_count"] = count,
            ["window_seconds"] = (int)EventsLostWindow.TotalSeconds,
        }).ConfigureAwait(false);
    }

    private async Task AppendAuditAsync(
        string action, string moduleCode, string? reason, Exception exception, Dictionary<string, object?>? extra)
    {
        try
        {
            // The audit chain is uploaded to the GRID: record the exception type
            // and HResult only, never its message (it can carry paths or secrets).
            var fields = new Dictionary<string, object?>
            {
                ["module"] = moduleCode,
                ["reason"] = reason,
                ["exception_type"] = exception.GetType().FullName,
                ["hresult"] = exception.HResult,
            };
            foreach (KeyValuePair<string, object?> field in extra ?? [])
            {
                fields[field.Key] = field.Value;
            }

            string details = JsonSerializer.Serialize(fields);

            await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
            var audit = scope.ServiceProvider.GetRequiredService<IAuditLogRepository>();
            await audit.AppendAsync(action, details, entityType: "Module", cancellationToken: CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception auditException)
        {
            // The failure itself is already in the registry and the log.
            _logger.LogError(auditException, "Could not write the {Action} audit entry for {Module}", action, moduleCode);
        }
    }

    private sealed class EventsLostWindowState
    {
        public object Gate { get; } = new();

        public bool Open { get; set; }

        public int Pending { get; set; }

        public Exception? LastException { get; set; }

        public ITimer? Timer { get; set; }
    }
}
