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
    private readonly IModuleFaultInjector _faultInjector;
    private readonly Func<bool> _isElevated;
    private IDataVolumeTracker? _tracker;
    private bool _wasLearning;
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
    /// <param name="faultInjector">Development fault injection; null or the production one never stops anything.</param>
    public ExfilWatchMonitor(
        ILogger<ExfilWatchMonitor> logger,
        INetworkActivityMonitor networkMonitor,
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<AgentConfiguration> config,
        IModuleStateRegistry moduleState,
        IModuleFailureReporter failureReporter,
        IAlertForwardingQueue? alertQueue = null,
        Func<bool>? isElevated = null,
        IModuleFaultInjector? faultInjector = null)
    {
        _logger = logger;
        _networkMonitor = networkMonitor;
        _scopeFactory = scopeFactory;
        _alertQueue = alertQueue;
        _config = config.CurrentValue;
        _moduleState = moduleState;
        _failureReporter = failureReporter;
        _faultInjector = faultInjector ?? new NoModuleFaultInjector();
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

            // The chain is proven before "active" (EXFIL-REG-001): every service a network event
            // needs is resolved once here. A missing one is inactive / init_failed with a Critical
            // log -- never a module that reads active while it skips every rule.
            try
            {
                await using AsyncServiceScope probe = _scopeFactory.CreateAsyncScope();
                _tracker = probe.ServiceProvider.GetRequiredService<IDataVolumeTracker>();
                _ = probe.ServiceProvider.GetRequiredService<ExfilRuleEngine>();
                _ = probe.ServiceProvider.GetRequiredService<IExfilActionEngine>();
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogCritical(ex, "EXFIL WATCH not started: its detection chain is not wired (init_failed) -- protection from this module is down");
                _moduleState.Publish(ModuleCode.ExfilWatch, ModuleState.Inactive, ModuleReasonCode.InitFailed);
                return;
            }

            _logger.LogInformation("EXFIL WATCH monitor starting (learning phase: {Days} days)",
                _config.ExfilWatch.LearningPhaseDays);

            // Start ETW capture
            _networkMonitor.Start(_eventChannel.Writer, moduleToken);
            captureStarted = true;

            consumerTask = ConsumeEventsAsync(moduleToken);

            PublishPosture(starting: true);
            reachedActive = true;

            await ModuleTaskObserver.ObserveUntilStoppedAsync(
                stoppingToken,
                ("consumer", consumerTask),
                ("status-log", LogStatusPeriodicallyAsync(moduleToken)),
                ("fault-injection", _faultInjector.WhenStopRequestedAsync(ModuleCode.ExfilWatch, moduleToken)));
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
            PublishPosture(); // the learning phase ends without an event: re-read every minute
        }
    }

    /// <summary>The response to one finding (alert, throttle, block by severity), in its own scope.</summary>
    private async Task RespondAsync(ExfilFinding finding, CancellationToken ct)
    {
        try
        {
            await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
            var actionEngine = scope.ServiceProvider.GetRequiredService<IExfilActionEngine>();
            await actionEngine.ExecuteAsync(finding, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Module stopping.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "EXFIL WATCH: response to {Rule} finding on {Process}:{PID} failed",
                finding.RuleName, finding.ProcessName, finding.ProcessId);
        }
    }

    /// <summary>
    /// Active, or degraded / learning_phase while the tracker learns (EXFIL-REG-001): VolumeAnomaly
    /// is mute until the baselines exist; DnsTunneling, AfterHoursExfil and LolbasExfil fire. The
    /// phase is counted from the tracker's creation, in memory: a restart begins it again (AGT-EXF-001).
    /// </summary>
    private void PublishPosture(bool starting = false)
    {
        if (_tracker?.IsLearningPhase == true)
        {
            if (starting)
            {
                _logger.LogWarning(
                    "EXFIL WATCH in learning phase for {Days} days from this start (a restart begins it again, AGT-EXF-001): VolumeAnomaly is mute until the baselines exist; DnsTunneling, AfterHoursExfil and LolbasExfil fire",
                    _config.ExfilWatch!.LearningPhaseDays);
            }

            _wasLearning = true;
            _moduleState.Publish(ModuleCode.ExfilWatch, ModuleState.Degraded, ModuleReasonCode.LearningPhase);
            return;
        }

        if (starting || _wasLearning)
        {
            _logger.LogInformation("EXFIL WATCH monitor active: every registered rule fires");
        }

        _wasLearning = false;
        _moduleState.Publish(ModuleCode.ExfilWatch, ModuleState.Active);
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
                // Required, never optional (EXFIL-REG-001): a missing service stops the module at
                // start; here it would be a bug, and a bug must throw, not skip the rules.
                IDataVolumeTracker tracker = scope.ServiceProvider.GetRequiredService<IDataVolumeTracker>();
                tracker.RecordEvent(evt);

                var ruleEngine = scope.ServiceProvider.GetRequiredService<ExfilRuleEngine>();
                var findings = ruleEngine.Evaluate(evt, tracker, _config.ExfilWatch!);
                foreach (var finding in findings.Where(f => f.Severity >= ExfilSeverity.Medium))
                {
                    // Forward to GRID first: the queue persists it itself.
                    _alertQueue?.TryEnqueue(AlertMapper.FromExfilFinding(finding));

                    // The response runs in the background, in a scope of its own that lives as long
                    // as it does. It used to be resolved from this event's scope and fired with
                    // Task.Run: the scope was disposed at the end of this iteration, the audit
                    // repository with it, and the actions (throttle, block) died on an
                    // ObjectDisposedException before reaching the firewall -- found by
                    // ExfilWatchProductionWiringTests (EXFIL-REG-001, run 39616563004).
                    _ = RespondAsync(finding, ct);
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
