using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RansomGuard.Agent.Core.Configuration;
using RansomGuard.Agent.Core.Diagnostics;
using RansomGuard.Agent.Core.Detection;
using RansomGuard.Agent.Core.Detection.Genealogy;
using RansomGuard.Agent.Core.Detection.Sentinel;
using RansomGuard.Agent.Core.Detection.Sentinel.Etw;
using RansomGuard.Agent.Core.Communication;
using RansomGuard.Agent.Core.Persistence.Entities;
using RansomGuard.Agent.Core.Persistence.Repositories;

namespace RansomGuard.Agent.Service;

/// <summary>
/// BackgroundService that monitors SENTINEL canary files for tampering.
/// Uses bounded channels (no async void) for real-time FileSystemWatcher events
/// plus periodic polling. Supports canary regeneration with anti-loop protection.
/// </summary>
public sealed class SentinelMonitor : BackgroundService
{
    private const int EventChannelCapacity = 10_000;

    /// <summary>
    /// Consecutive failed integrity checks after which SENTINEL reads degraded /
    /// integrity_check_failing. One failure (a locked database, a transient I/O error)
    /// must not change the state; a persistent one must show — but never by switching
    /// off the real-time watchers, which still catch a canary being touched.
    /// </summary>
    public const int MaxConsecutiveIntegrityCheckFailures = 5;

    private readonly ILogger<SentinelMonitor> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IFileEventDeduplicator _deduplicator;
    private readonly RestartManagerHelper _restartManager;
    private readonly IAlertForwardingQueue _alertQueue;
    private readonly IModuleStateRegistry _moduleState;
    private readonly IModuleFailureReporter _failureReporter;
    private readonly IModuleFaultInjector _faultInjector;
    private readonly AgentConfiguration _config;
    private readonly TimeSpan _startupDelay;
    private readonly TaskCompletionSource _watcherFault = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly object _healthGate = new();
    private bool _stopped;
    private bool _integrityCheckFailing;
    private readonly List<FileSystemWatcher> _watchers = [];
    private readonly Channel<CanaryFileEvent> _eventChannel;
    private readonly ConcurrentDictionary<string, RegenerationStats> _regenTracking = new();
    private readonly EtwFileIoSession? _etwSession;
    private readonly EtwCanaryFilter _etwFilter = new();
    private readonly CanaryCorrelator _correlator;

    /// <summary>
    /// Initializes the SENTINEL monitor.
    /// </summary>
    /// <param name="logger">Logger.</param>
    /// <param name="config">Agent configuration.</param>
    /// <param name="scopeFactory">Scope factory for per-event services.</param>
    /// <param name="deduplicator">File event deduplicator.</param>
    /// <param name="restartManager">Process attribution through the Restart Manager.</param>
    /// <param name="alertQueue">Queue forwarding alerts to the GRID.</param>
    /// <param name="moduleState">Runtime module-state registry.</param>
    /// <param name="failureReporter">Reports the module going down.</param>
    /// <param name="startupDelay">
    /// Test seam: wait before starting, for the deployment service; null (the DI default) is 2 s.
    /// </param>
    /// <param name="faultInjector">Development fault injection; null or the production one never stops anything.</param>
    /// <param name="etwSession">
    /// The kernel FileIO session (ADR-025); opened only when Sentinel:EtwEnabled is true. Null: watcher only.
    /// </param>
    /// <param name="time">The clock of the correlation window.</param>
    public SentinelMonitor(
        ILogger<SentinelMonitor> logger,
        IOptionsMonitor<AgentConfiguration> config,
        IServiceScopeFactory scopeFactory,
        IFileEventDeduplicator deduplicator,
        RestartManagerHelper restartManager,
        IAlertForwardingQueue alertQueue,
        IModuleStateRegistry moduleState,
        IModuleFailureReporter failureReporter,
        TimeSpan? startupDelay = null,
        IModuleFaultInjector? faultInjector = null,
        EtwFileIoSession? etwSession = null,
        TimeProvider? time = null)
    {
        _etwSession = etwSession;
        _correlator = new CanaryCorrelator(time);
        _logger = logger;
        _config = config.CurrentValue;
        _scopeFactory = scopeFactory;
        _deduplicator = deduplicator;
        _restartManager = restartManager;
        _alertQueue = alertQueue;
        _moduleState = moduleState;
        _failureReporter = failureReporter;
        _faultInjector = faultInjector ?? new NoModuleFaultInjector();
        _startupDelay = startupDelay ?? TimeSpan.FromSeconds(2);
        _eventChannel = Channel.CreateBounded<CanaryFileEvent>(
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
        Task? consumerTask = null;
        Task? pollingTask = null;
        Task? etwConsumerTask = null;

        // Every task of the module runs on this token, so that after a failure
        // nothing keeps running unseen (no orphaned polling loop, no consumer).
        using var moduleCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        CancellationToken moduleToken = moduleCts.Token;

        try
        {
            SentinelOptions? sentinel = _config.Sentinel;
            if (sentinel is null || !sentinel.Enabled)
            {
                _moduleState.Publish(ModuleCode.Sentinel, ModuleState.DisabledByConfig, ModuleReasonCode.DisabledByConfig);
                return;
            }

            // Wait for deployment service to finish
            await Task.Delay(_startupDelay, stoppingToken);

            consumerTask = ConsumeEventsAsync(moduleToken);

            // Set up real-time FileSystemWatcher per canary directory
            string[] resolvedDirs = EnvironmentVariableResolver.ResolvePaths(sentinel.WatchDirectories);
            foreach (string dir in resolvedDirs)
            {
                if (!Directory.Exists(dir))
                {
                    _logger.LogWarning("SENTINEL: folder {Directory} does not exist and is not watched", dir);
                    continue;
                }

                var watcher = new FileSystemWatcher(dir)
                {
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                    IncludeSubdirectories = false,
                    EnableRaisingEvents = true
                };

                watcher.Changed += (_, e) => EnqueueEvent(e.FullPath, CanaryAlertType.CanaryModified, e.ChangeType);
                watcher.Deleted += (_, e) => EnqueueEvent(e.FullPath, CanaryAlertType.CanaryDeleted, e.ChangeType);
                watcher.Renamed += (_, e) => EnqueueEvent(e.OldFullPath, CanaryAlertType.CanaryRenamed, e.ChangeType);
                watcher.Error += OnWatcherError;

                _watchers.Add(watcher);
            }

            // Zero watcher is not "active": it is a blind detector (under LocalSystem,
            // %USERPROFILE% resolves to the system profile and the folders do not exist).
            if (_watchers.Count == 0)
            {
                _logger.LogError("SENTINEL watches nothing: none of the {Count} configured folders exists",
                    resolvedDirs.Length);
                _moduleState.Publish(ModuleCode.Sentinel, ModuleState.Inactive, ModuleReasonCode.NoWatchDirectory);
                return;
            }

            _logger.LogInformation("SENTINEL monitor active — real-time watchers on {DirCount} directories, polling every {IntervalMs}ms",
                _watchers.Count, sentinel.CheckIntervalMs);
            _moduleState.Publish(ModuleCode.Sentinel, ModuleState.Active);
            reachedActive = true;

            // The kernel FileIO session (ADR-025): writes the watcher does not see, and one detection
            // per operation with the watcher. When it cannot open, the session has said why in the
            // log; SENTINEL keeps detecting by its watcher (the state says so from commit 7).
            if (sentinel.EtwEnabled && _etwSession is not null)
            {
                await RefreshCanariesAsync(moduleToken);
                _correlator.EtwRunning = _etwSession.Start(_etwFilter.OnFileIo, _ => { });
                etwConsumerTask = ConsumeEtwAsync(moduleToken);
            }

            // The periodic check is not observed as fatal: its failures degrade the
            // module (integrity_check_failing) and it never ends on its own before shutdown.
            pollingTask = PollIntegrityAsync(sentinel.CheckIntervalMs, moduleToken);

            // Only the real-time path going down stops SENTINEL (stopped_unexpectedly).
            await ModuleTaskObserver.ObserveUntilStoppedAsync(
                stoppingToken,
                ("consumer", consumerTask),
                ("watchers", _watcherFault.Task),
                ("fault-injection", _faultInjector.WhenStopRequestedAsync(ModuleCode.Sentinel, moduleToken)));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
        catch (Exception ex)
        {
            // From here on the polling loop may no longer publish a state: the module is down.
            lock (_healthGate)
            {
                _stopped = true;
            }

            // Swallowed on purpose: one module going down must not take the host —
            // and every other module — with it. The reporter leaves the three
            // traces: registry state, Critical log, audit entry.
            await _failureReporter.ReportStoppedAsync(ModuleCode.Sentinel, reachedActive, ex);
        }
        finally
        {
            await StopModuleAsync(moduleCts, consumerTask, pollingTask, etwConsumerTask);
        }
    }

    /// <summary>
    /// Runs the periodic integrity check. After <see cref="MaxConsecutiveIntegrityCheckFailures"/>
    /// failures in a row SENTINEL reads degraded / integrity_check_failing; the first
    /// successful check brings it back to active. The watchers and the consumer keep
    /// running throughout. Ends only on cancellation.
    /// </summary>
    private async Task PollIntegrityAsync(int intervalMs, CancellationToken ct)
    {
        int consecutiveFailures = 0;
        while (true)
        {
            await Task.Delay(intervalMs, ct);
            try
            {
                await PeriodicIntegrityCheckAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                consecutiveFailures++;
                if (consecutiveFailures == MaxConsecutiveIntegrityCheckFailures)
                {
                    _logger.LogError(ex,
                        "SENTINEL integrity check failed {Count} times in a row — degraded, real-time watchers still running",
                        consecutiveFailures);
                    SetIntegrityCheckFailing(true);
                }
                else
                {
                    _logger.LogWarning(ex, "SENTINEL integrity check failed ({Count} in a row); retrying next cycle",
                        consecutiveFailures);
                }

                continue;
            }

            if (consecutiveFailures >= MaxConsecutiveIntegrityCheckFailures)
            {
                _logger.LogInformation("SENTINEL integrity check succeeded again after {Count} failures — active",
                    consecutiveFailures);
            }

            consecutiveFailures = 0;
            SetIntegrityCheckFailing(false);
        }
    }

    /// <summary>
    /// Publishes degraded / active for the integrity check, never after the module has
    /// stopped: a check succeeding after the watchers died must not bring back "active".
    /// </summary>
    private void SetIntegrityCheckFailing(bool failing)
    {
        lock (_healthGate)
        {
            if (_stopped || _integrityCheckFailing == failing)
            {
                return;
            }

            _integrityCheckFailing = failing;
            if (failing)
            {
                _moduleState.Publish(ModuleCode.Sentinel, ModuleState.Degraded, ModuleReasonCode.IntegrityCheckFailing);
            }
            else
            {
                _moduleState.Publish(ModuleCode.Sentinel, ModuleState.Active);
            }
        }
    }

    /// <summary>
    /// A watcher reports an error. A buffer overflow loses events but the watcher
    /// keeps running; any other error means it has stopped and SENTINEL is blind.
    /// </summary>
    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        Exception error = e.GetException();
        if (error is InternalBufferOverflowException)
        {
            // ReportEventsLostAsync never throws, and writes at most once a minute.
            _ = _failureReporter.ReportEventsLostAsync(ModuleCode.Sentinel, error);
            return;
        }

        _watcherFault.TrySetException(error);
    }

    /// <summary>Stops every source and task of the module; best effort, never throws.</summary>
    private async Task StopModuleAsync(CancellationTokenSource moduleCts, Task? consumerTask, Task? pollingTask, Task? etwConsumerTask = null)
    {
        _etwSession?.Stop();
        foreach (FileSystemWatcher watcher in _watchers)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
        }

        _watchers.Clear();
        _eventChannel.Writer.TryComplete();
        await moduleCts.CancelAsync();

        foreach ((string name, Task? task) in new[] { ("consumer", consumerTask), ("integrity-polling", pollingTask), ("etw-consumer", etwConsumerTask) })
        {
            if (task is null)
            {
                continue;
            }

            try
            {
                await task;
            }
            catch (Exception ex)
            {
                // Cancelled by the stop above, or already reported as the failure.
                _logger.LogDebug(ex, "SENTINEL {Task} ended during stop", name);
            }
        }
    }

    private void EnqueueEvent(string filePath, CanaryAlertType alertType, WatcherChangeTypes changeType)
    {
        if (!_deduplicator.ShouldProcess(filePath, changeType))
        {
            return;
        }

        if (!_eventChannel.Writer.TryWrite(new CanaryFileEvent(filePath, alertType, DateTime.UtcNow)))
        {
            _logger.LogWarning("SENTINEL event channel saturated, dropped event for {Path}", filePath);
        }
    }

    private async Task ConsumeEventsAsync(CancellationToken cancellationToken)
    {
        await foreach (CanaryFileEvent evt in _eventChannel.Reader.ReadAllAsync(cancellationToken))
        {
            try
            {
                using IServiceScope scope = _scopeFactory.CreateScope();
                var canaryRepo = scope.ServiceProvider.GetRequiredService<ISentinelCanaryRepository>();

                SentinelCanary? canary = await canaryRepo.GetByFilePathAsync(evt.FilePath, cancellationToken);
                if (canary is null || canary.Status != CanaryStatus.Active)
                {
                    continue;
                }

                // One detection per operation (ADR-025, section 3): ETW may have raised it already.
                if (_correlator.OnWatcher(canary.FilePath, OperationOf(evt.AlertType)) is null)
                {
                    continue;
                }

                await RaiseCanaryAlertAsync(canary, evt.AlertType, scope.ServiceProvider, cancellationToken);

                // Real-time regeneration for deleted canaries
                if (evt.AlertType == CanaryAlertType.CanaryDeleted && _config.Sentinel?.EnableRealTimeRegeneration == true)
                {
                    await TryRegenerateCanaryAsync(canary, scope.ServiceProvider, cancellationToken);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing SENTINEL event for {FilePath}", evt.FilePath);
            }
        }
    }

    private async Task TryRegenerateCanaryAsync(SentinelCanary deletedCanary, IServiceProvider services, CancellationToken cancellationToken)
    {
        SentinelOptions sentinel = _config.Sentinel!;

        RegenerationStats stats = _regenTracking.GetOrAdd(deletedCanary.Directory, _ => new RegenerationStats());

        // Anti-loop: check hourly limit
        stats.CleanupOlderThan(TimeSpan.FromHours(1));
        if (stats.Count >= sentinel.MaxRegenerationsPerHourPerDirectory)
        {
            _logger.LogCritical(
                "SENTINEL SUSTAINED ATTACK: {RegenCount} regenerations in 1 hour for {Directory}. Manual intervention required.",
                stats.Count, deletedCanary.Directory);

            var canaryRepo = services.GetRequiredService<ISentinelCanaryRepository>();
            var auditRepo = services.GetRequiredService<IAuditLogRepository>();

            await auditRepo.AppendAsync("SustainedAttack",
                $"Canary regeneration limit exceeded ({stats.Count}/{sentinel.MaxRegenerationsPerHourPerDirectory}) in {deletedCanary.Directory}",
                "SentinelCanary", deletedCanary.Id, cancellationToken);
            return;
        }

        // Regenerate with same template
        var canaryService = services.GetRequiredService<ICanaryFileService>();
        await canaryService.CreateCanaryAsync(
            deletedCanary.Directory, deletedCanary.TemplateUsed, sentinel.CanaryPrefix, cancellationToken);

        stats.RecordRegeneration();

        var audit = services.GetRequiredService<IAuditLogRepository>();
        await audit.AppendAsync("CanaryRegenerated",
            $"Canary regenerated in {deletedCanary.Directory} (template: {deletedCanary.TemplateUsed}) after tampering",
            "SentinelCanary", deletedCanary.Id, cancellationToken);

        _logger.LogInformation("SENTINEL canary regenerated in {Directory} after deletion (template: {Template})",
            deletedCanary.Directory, deletedCanary.TemplateUsed);
    }

    private async Task PeriodicIntegrityCheckAsync(CancellationToken cancellationToken)
    {
        using IServiceScope scope = _scopeFactory.CreateScope();
        var canaryService = scope.ServiceProvider.GetRequiredService<ICanaryFileService>();
        var canaryRepo = scope.ServiceProvider.GetRequiredService<ISentinelCanaryRepository>();

        IReadOnlyList<SentinelCanary> activeCanaries = await canaryService.GetAllCanariesAsync(cancellationToken);
        _etwFilter.SetCanaries(activeCanaries.Select(c => c.FilePath));
        _correlator.EtwRunning = _etwSession?.State == EtwSessionState.Running;
        _correlator.Sweep();

        foreach (SentinelCanary canary in activeCanaries)
        {
            if (cancellationToken.IsCancellationRequested) break;

            if (!File.Exists(canary.FilePath))
            {
                await RaiseCanaryAlertAsync(canary, CanaryAlertType.CanaryDeleted, scope.ServiceProvider, cancellationToken);

                if (_config.Sentinel?.EnableRealTimeRegeneration == true)
                {
                    await TryRegenerateCanaryAsync(canary, scope.ServiceProvider, cancellationToken);
                }
                continue;
            }

            bool isIntact = await canaryService.VerifyCanaryIntegrityAsync(canary, cancellationToken);
            if (!isIntact)
            {
                await RaiseCanaryAlertAsync(canary, CanaryAlertType.CanaryModified, scope.ServiceProvider, cancellationToken);
                continue;
            }

            canary.LastCheckedAt = DateTime.UtcNow;
            await canaryRepo.UpdateAsync(canary, cancellationToken);
        }
    }

    /// <summary>The canaries the ETW filter knows, before the session opens; then every integrity cycle.</summary>
    private async Task RefreshCanariesAsync(CancellationToken cancellationToken)
    {
        using IServiceScope scope = _scopeFactory.CreateScope();
        var canaryService = scope.ServiceProvider.GetRequiredService<ICanaryFileService>();
        IReadOnlyList<SentinelCanary> canaries = await canaryService.GetAllCanariesAsync(cancellationToken);
        _etwFilter.SetCanaries(canaries.Select(c => c.FilePath));
    }

    /// <summary>
    /// ETW canary writes (filtered on the session's thread) into detections. A write only ETW sees
    /// -- a rewrite through a mapped view, a deferred write carried by PID 4 -- is a detection too
    /// (ADR-025, 2a). The alert leaves now; attribution is commit 4.
    /// </summary>
    private async Task ConsumeEtwAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (FileIoRecord record in _etwFilter.Reader.ReadAllAsync(cancellationToken))
            {
                try
                {
                    CanaryDetection? detection = _correlator.OnEtw(record);
                    if (detection is null)
                    {
                        continue; // joins a detection already raised (watcher or ETW)
                    }

                    using IServiceScope scope = _scopeFactory.CreateScope();
                    var canaryRepo = scope.ServiceProvider.GetRequiredService<ISentinelCanaryRepository>();
                    SentinelCanary? canary = await canaryRepo.GetByFilePathAsync(record.FileName, cancellationToken);
                    if (canary is null || canary.Status != CanaryStatus.Active)
                    {
                        continue;
                    }

                    CanaryAlertType alertType = AlertTypeOf(detection.Operation);
                    await RaiseCanaryAlertAsync(canary, alertType, scope.ServiceProvider, cancellationToken);
                    if (alertType == CanaryAlertType.CanaryDeleted && _config.Sentinel?.EnableRealTimeRegeneration == true)
                    {
                        await TryRegenerateCanaryAsync(canary, scope.ServiceProvider, cancellationToken);
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing SENTINEL ETW event for {FilePath}", record.FileName);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // stopping
        }
    }

    private static CanaryOperation OperationOf(CanaryAlertType alertType) => alertType switch
    {
        CanaryAlertType.CanaryDeleted => CanaryOperation.Deletion,
        CanaryAlertType.CanaryRenamed => CanaryOperation.Rename,
        _ => CanaryOperation.Modification,
    };

    private static CanaryAlertType AlertTypeOf(CanaryOperation operation) => operation switch
    {
        CanaryOperation.Deletion => CanaryAlertType.CanaryDeleted,
        CanaryOperation.Rename => CanaryAlertType.CanaryRenamed,
        _ => CanaryAlertType.CanaryModified,
    };

    private async Task RaiseCanaryAlertAsync(
        SentinelCanary canary,
        CanaryAlertType alertType,
        IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        var canaryRepo = services.GetRequiredService<ISentinelCanaryRepository>();
        var auditRepo = services.GetRequiredService<IAuditLogRepository>();

        ProcessAttribution? attribution = TryAttributeProcess(canary.FilePath);

        var alert = new CanaryAlert
        {
            CanaryId = canary.Id,
            CanaryPath = canary.FilePath,
            AlertType = alertType,
            Severity = AlertSeverity.Critical,
            OffendingProcessId = attribution?.ProcessId,
            OffendingProcessName = attribution?.ProcessName,
            OffendingProcessPath = attribution?.ExecutablePath
        };

        // The GRID first (AGT-AUD-002): nothing local — database, audit — may delay or cancel
        // the alert. The forwarding queue persists it itself if the GRID is unreachable.
        _alertQueue.TryEnqueue(AlertMapper.FromCanaryAlert(alert));
        _logger.LogCritical(
            "SENTINEL ALERT: {AlertType} on canary {CanaryPath} | Process: {ProcessName} (PID: {ProcessId}) | Severity: {Severity}",
            alertType, canary.FilePath, attribution?.ProcessName ?? "unknown",
            attribution?.ProcessId.ToString() ?? "N/A", alert.Severity);

        canary.Status = alertType switch
        {
            CanaryAlertType.CanaryModified => CanaryStatus.Modified,
            CanaryAlertType.CanaryDeleted => CanaryStatus.Deleted,
            CanaryAlertType.CanaryRenamed => CanaryStatus.Renamed,
            _ => CanaryStatus.Compromised
        };
        // From here the alert has left: its local record and its audit entry are completed even
        // during a shutdown, so a sent detection never lacks its trace.
        try
        {
            await canaryRepo.UpdateAsync(canary, CancellationToken.None);
            await canaryRepo.AddAlertAsync(alert, CancellationToken.None);
        }
        catch (Exception ex)
        {
            // The alert is already on its way to the GRID; the local copy is what failed.
            _logger.LogError(ex, "SENTINEL alert {AlertId} sent, but its local record failed", alert.Id);
        }

        string details = $"Canary alert: {alertType} on {canary.FilePath}";
        if (attribution is not null)
        {
            details += $" by process {attribution.ProcessName} (PID: {attribution.ProcessId})";
        }

        // One INSERT, never a wait on the chain; never throws when it cannot record.
        await auditRepo.AppendAsync("CanaryAlert", details, "CanaryAlert", alert.Id, CancellationToken.None);

        // Fire-and-forget genealogy enrichment for process attribution
        _ = Task.Run(async () =>
        {
            try
            {
                using var enrichScope = _scopeFactory.CreateScope();
                var enricher = enrichScope.ServiceProvider.GetRequiredService<IGenealogyEnricher>();
                await enricher.EnrichAlertAsync(alert.Id, canary.FilePath, default);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Genealogy enrichment failed for canary alert {AlertId}", alert.Id);
            }
        }, CancellationToken.None);
    }

    private ProcessAttribution? TryAttributeProcess(string filePath)
    {
        try
        {
            IReadOnlyList<ProcessAttribution> processes = _restartManager.GetProcessesLockingFile(filePath);
            if (processes.Count == 0)
            {
                return null;
            }

            ProcessAttribution offender = processes
                .OrderByDescending(p => p.StartTime ?? DateTime.MinValue)
                .First();

            _logger.LogInformation(
                "Process attribution: {ProcessName} (PID: {ProcessId}) identified for {FilePath}",
                offender.ProcessName, offender.ProcessId, filePath);

            return offender;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Process attribution failed for {FilePath}", filePath);
            return null;
        }
    }
}

/// <summary>
/// Represents a canary file event from FileSystemWatcher, queued via bounded channel.
/// </summary>
internal sealed record CanaryFileEvent(string FilePath, CanaryAlertType AlertType, DateTime Timestamp);

/// <summary>
/// Tracks canary regeneration attempts per directory for anti-loop protection.
/// </summary>
internal sealed class RegenerationStats
{
    private readonly List<DateTime> _timestamps = [];
    private readonly object _lock = new();

    /// <summary>
    /// Current count of regenerations within the tracking window.
    /// </summary>
    public int Count
    {
        get { lock (_lock) return _timestamps.Count; }
    }

    /// <summary>
    /// Records a new regeneration event.
    /// </summary>
    public void RecordRegeneration()
    {
        lock (_lock) _timestamps.Add(DateTime.UtcNow);
    }

    /// <summary>
    /// Removes entries older than the specified window.
    /// </summary>
    public void CleanupOlderThan(TimeSpan window)
    {
        DateTime cutoff = DateTime.UtcNow - window;
        lock (_lock) _timestamps.RemoveAll(t => t < cutoff);
    }
}
