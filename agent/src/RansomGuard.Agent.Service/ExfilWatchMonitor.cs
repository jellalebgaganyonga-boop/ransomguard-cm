using System.Runtime.Versioning;
using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RansomGuard.Agent.Core.Configuration;
using RansomGuard.Agent.Core.Diagnostics;
using RansomGuard.Agent.Core.Detection.ExfilWatch;
using RansomGuard.Agent.Core.Detection.ExfilWatch.Actions;
using RansomGuard.Agent.Core.Detection.ExfilWatch.Models;
using RansomGuard.Agent.Core.Detection.ExfilWatch.Rules;
using RansomGuard.Agent.Core.Communication;
using RansomGuard.Agent.Core.Security.RateLimiting;

namespace RansomGuard.Agent.Service;

/// <summary>
/// BackgroundService that runs the EXFIL WATCH network monitoring pipeline.
/// Architecture: ETW Capture -> Channel -> DataVolumeTracker -> DetectionRules -> Alerts.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ExfilWatchMonitor : BackgroundService
{
    private const int EventChannelCapacity = 10_000;

    private readonly ILogger<ExfilWatchMonitor> _logger;
    private readonly INetworkActivityMonitor _networkMonitor;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IAlertForwardingQueue? _alertQueue;
    private readonly AgentConfiguration _config;
    private readonly IModuleStateRegistry _moduleState;
    private readonly IModuleFailureReporter _failureReporter;
    private readonly Func<bool> _isElevated;
    private readonly Channel<NetworkEvent> _eventChannel;
    private long _totalEventsProcessed;

    /// <summary>Total events processed since start.</summary>
    public long TotalEventsProcessed => Interlocked.Read(ref _totalEventsProcessed);

    /// <summary>Initializes the EXFIL WATCH monitor.</summary>
    /// <param name="logger">Logger.</param>
    /// <param name="networkMonitor">Kernel ETW network capture.</param>
    /// <param name="scopeFactory">Scope factory for per-event services.</param>
    /// <param name="config">Agent configuration.</param>
    /// <param name="moduleState">Runtime module-state registry.</param>
    /// <param name="failureReporter">Reports the module going down.</param>
    /// <param name="alertQueue">Queue forwarding alerts to the GRID.</param>
    /// <param name="isElevated">
    /// Test seam for the elevation check; null (the DI default) uses the real
    /// Windows administrator check.
    /// </param>
    public ExfilWatchMonitor(
        ILogger<ExfilWatchMonitor> logger,
        INetworkActivityMonitor networkMonitor,
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<AgentConfiguration> config,
        IModuleStateRegistry moduleState,
        IModuleFailureReporter failureReporter,
        IAlertForwardingQueue? alertQueue = null,
        Func<bool>? isElevated = null)
    {
        _logger = logger;
        _networkMonitor = networkMonitor;
        _scopeFactory = scopeFactory;
        _alertQueue = alertQueue;
        _config = config.CurrentValue;
        _moduleState = moduleState;
        _failureReporter = failureReporter;
        _isElevated = isElevated ?? IsElevated;
        _eventChannel = Channel.CreateBounded<NetworkEvent>(
            new BoundedChannelOptions(EventChannelCapacity)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true
            });
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        bool reachedActive = false;
        bool captureStarted = false;
        Task? consumerTask = null;

        // Every task of the module runs on this token, so that after a failure
        // nothing keeps running unseen.
        using var moduleCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        CancellationToken moduleToken = moduleCts.Token;

        try
        {
            if (_config.ExfilWatch is null || !_config.ExfilWatch.Enabled)
            {
                _logger.LogInformation("EXFIL WATCH module is disabled");
                _moduleState.Publish(ModuleCode.ExfilWatch, ModuleState.DisabledByConfig, ModuleReasonCode.DisabledByConfig);
                return;
            }

            // Kernel ETW (the core TCP/IP capture) requires elevation. Without it the
            // module cannot do its job, so report it honestly as inactive rather than
            // letting it look active on a degraded capture.
            if (!_isElevated())
            {
                _logger.LogWarning("EXFIL WATCH inactive: administrator rights missing (kernel ETW unavailable)");
                _moduleState.Publish(ModuleCode.ExfilWatch, ModuleState.Inactive, ModuleReasonCode.AdminRightsMissing);
                return;
            }

            _logger.LogInformation("EXFIL WATCH monitor starting (learning phase: {Days} days)",
                _config.ExfilWatch.LearningPhaseDays);

            // Start ETW capture
            _networkMonitor.Start(_eventChannel.Writer, moduleToken);
            captureStarted = true;

            consumerTask = ConsumeEventsAsync(moduleToken);

            _logger.LogInformation("EXFIL WATCH monitor active");
            _moduleState.Publish(ModuleCode.ExfilWatch, ModuleState.Active);
            reachedActive = true;

            await ModuleTaskObserver.ObserveUntilStoppedAsync(
                stoppingToken,
                ("consumer", consumerTask),
                ("status-log", LogStatusPeriodicallyAsync(moduleToken)));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
        catch (Exception ex)
        {
            // Swallowed on purpose: one module going down must not take the host —
            // and every other module — with it. The reporter leaves the three
            // traces: registry state, Critical log, audit entry.
            await _failureReporter.ReportStoppedAsync(ModuleCode.ExfilWatch, reachedActive, ex);
        }
        finally
        {
            await StopModuleAsync(moduleCts, consumerTask, captureStarted);
        }
    }

    private async Task LogStatusPeriodicallyAsync(CancellationToken ct)
    {
        using var heartbeat = new PeriodicTimer(TimeSpan.FromSeconds(60));
        while (await heartbeat.WaitForNextTickAsync(ct))
        {
            _logger.LogInformation(
                "EXFIL WATCH heartbeat | Events processed: {Processed} | ETW captured: {Captured}",
                TotalEventsProcessed, _networkMonitor.TotalEventsCaptured);
        }
    }

    /// <summary>Stops every source and task of the module; best effort, never throws.</summary>
    private async Task StopModuleAsync(CancellationTokenSource moduleCts, Task? consumerTask, bool captureStarted)
    {
        _eventChannel.Writer.TryComplete();
        await moduleCts.CancelAsync();

        if (consumerTask is not null)
        {
            try
            {
                await consumerTask;
            }
            catch (Exception ex)
            {
                // Cancelled by the stop above, or already reported as the failure.
                _logger.LogDebug(ex, "EXFIL WATCH consumer ended during stop");
            }
        }

        if (captureStarted)
        {
            try
            {
                await _networkMonitor.StopAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "EXFIL WATCH: ETW capture did not stop cleanly");
            }
        }
    }

    /// <summary>True when the agent runs with administrator rights (required for kernel ETW).</summary>
    private static bool IsElevated()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        return new System.Security.Principal.WindowsPrincipal(identity)
            .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
    }

    private async Task ConsumeEventsAsync(CancellationToken ct)
    {
        await foreach (var evt in _eventChannel.Reader.ReadAllAsync(ct))
        {
            try
            {
                Interlocked.Increment(ref _totalEventsProcessed);

                await using var scope = _scopeFactory.CreateAsyncScope();
                var tracker = scope.ServiceProvider.GetService<IDataVolumeTracker>();

                // Feed event to data volume tracker
                tracker?.RecordEvent(evt);

                // Evaluate detection rules and dispatch actions for findings >= Medium
                if (tracker is not null)
                {
                    var ruleEngine = scope.ServiceProvider.GetService<ExfilRuleEngine>();
                    var findings = ruleEngine?.Evaluate(evt, tracker, _config.ExfilWatch!);

                    if (findings is { Count: > 0 })
                    {
                        var actionEngine = scope.ServiceProvider.GetService<IExfilActionEngine>();
                        if (actionEngine is not null)
                        {
                            foreach (var finding in findings.Where(f => f.Severity >= ExfilSeverity.Medium))
                            {
                                // Fire-and-forget for non-blocking pipeline
                                _ = Task.Run(() => actionEngine.ExecuteAsync(finding, ct), ct);

                                // Forward to GRID in parallel
                                _alertQueue?.TryEnqueue(AlertMapper.FromExfilFinding(finding));
                            }
                        }
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "EXFIL WATCH: Error processing event from {Process}:{PID}",
                    evt.ProcessName, evt.ProcessId);
            }
        }
    }
}
