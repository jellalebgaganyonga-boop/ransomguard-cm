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
using RansomGuard.Agent.Core.Detection.Sentinel.Response;
using RansomGuard.Agent.Core.Communication;
using RansomGuard.Agent.Core.Communication.Models;
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

    /// <summary>
    /// Paths the agent is writing itself -- its restorations here, and the decoys the deployment
    /// service plants at install. Shared with that service: when it lived in this class, the
    /// canaries created before the watchers existed were invisible to it and a fresh install
    /// raised a burst of Critical "canary modified" alerts for the agent's own work.
    /// </summary>
    private readonly AgentOwnWrites _ownWrites;

    /// <summary>
    /// Modifications the real-time path has reported and is restoring, so the periodic integrity
    /// sweep does not raise a second alert for the same write while the decoy is being put back.
    /// </summary>
    private readonly ReportedModifications _reported;
    private readonly EtwFileIoSession? _etwSession;
    private readonly EtwCanaryFilter _etwFilter = new();
    private readonly CanaryCorrelator _correlator;
    private readonly ProcessCreationTimes _processTimes;
    private readonly UserProfileEnumerator _profiles;
    private readonly CanaryAttributor _attributor;
    private readonly CanaryResponseEngine? _response;
    private readonly TimeProvider _time;

    /// <summary>
    /// Lets ENTROPY read the same session (AGT-ATTR-001). Null in the unit tests that build the
    /// monitor by hand: the canary filter is then handed straight to the session, as before.
    /// </summary>
    private readonly FileIoFanout? _fanout;

    /// <summary>
    /// Initializes the SENTINEL monitor.
    /// </summary>
    /// <param name="logger">Logger.</param>
    /// <param name="config">Agent configuration.</param>
    /// <param name="scopeFactory">Scope factory for per-event services.</param>
    /// <param name="deduplicator">File event deduplicator.</param>
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
    /// <param name="time">The clock of the correlation window and of the attribution delay.</param>
    /// <param name="processTimes">Process creation times for the attribution; the OS by default (test seam).</param>
    /// <param name="profiles">The real user profiles that %EACHUSERPROFILE% expands to (AGT-PATH-001); null: the Windows registry.</param>
    /// <param name="responseEngine">The response (ADR-026): suspends the attributed writer. Required when Sentinel:Response:SuspendProcess is true.</param>
    /// <param name="fanout">Dispatches the session's records to several readers (AGT-ATTR-001); null hands the canary filter straight to the session, as before.</param>
    /// <param name="ownWrites">The paths the agent writes itself, shared with the deployment service; null builds a private one (tests).</param>
    public SentinelMonitor(
        ILogger<SentinelMonitor> logger,
        IOptionsMonitor<AgentConfiguration> config,
        IServiceScopeFactory scopeFactory,
        IFileEventDeduplicator deduplicator,
        IAlertForwardingQueue alertQueue,
        IModuleStateRegistry moduleState,
        IModuleFailureReporter failureReporter,
        TimeSpan? startupDelay = null,
        IModuleFaultInjector? faultInjector = null,
        EtwFileIoSession? etwSession = null,
        TimeProvider? time = null,
        ProcessCreationTimes? processTimes = null,
        UserProfileEnumerator? profiles = null,
        CanaryResponseEngine? responseEngine = null,
        FileIoFanout? fanout = null,
        AgentOwnWrites? ownWrites = null)
    {
        _ownWrites = ownWrites ?? new AgentOwnWrites();
        _fanout = fanout;
        _response = responseEngine;
        _processTimes = processTimes ?? new ProcessCreationTimes();
        _profiles = profiles ?? UserProfileEnumerator.Windows;
        _etwSession = etwSession;
        _time = time ?? TimeProvider.System;
        _reported = new ReportedModifications(_time);
        _correlator = new CanaryCorrelator(_time);
        _attributor = new CanaryAttributor(_processTimes);
        _logger = logger;
        _config = config.CurrentValue;
        _scopeFactory = scopeFactory;
        _deduplicator = deduplicator;
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
            string[] resolvedDirs = EnvironmentVariableResolver.ResolvePaths(sentinel.WatchDirectories, _profiles, _logger);
            foreach (string dir in resolvedDirs)
            {
                // AGT-PATH-001: a system or service profile is never watched -- it counts as missing.
                if (WatchedPathPolicy.IsRefused(dir))
                {
                    _logger.LogError("SENTINEL: folder {Directory} is in a system or service profile and is not watched (AGT-PATH-001)", dir);
                    continue;
                }

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
                _logger.LogError("SENTINEL watches nothing: none of the {Count} configured folders is watchable (missing, or refused as a system profile)",
                    resolvedDirs.Length);
                _moduleState.Publish(ModuleCode.Sentinel, ModuleState.Inactive, ModuleReasonCode.NoWatchDirectory);
                return;
            }

            // The chain is proven before "active" (plan de finition): a response the configuration
            // promises and that nothing can carry out is a lie the console would repeat.
            if (sentinel.Response?.SuspendProcess == true && _response is null)
            {
                _logger.LogCritical("SENTINEL: Sentinel:Response:SuspendProcess is true but no response engine is wired; the module does not start (init_failed)");
                _moduleState.Publish(ModuleCode.Sentinel, ModuleState.Inactive, ModuleReasonCode.InitFailed);
                return;
            }

            if (sentinel.Response is null)
            {
                _logger.LogWarning("SENTINEL: no Sentinel:Response section: a canary write is alerted, never answered (no suspension)");
            }
            else if (sentinel.Response.SuspendProcess && !sentinel.EtwEnabled)
            {
                _logger.LogWarning("SENTINEL: Sentinel:Response:SuspendProcess is true but Sentinel:EtwEnabled is false: without ETW no writer is ever attributed, so nobody is ever suspended");
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

                // SENTINEL owns the session; it is not its only reader any more (AGT-ATTR-001).
                // The canary filter is subscribed first and keeps receiving every record exactly
                // as before; ENTROPY subscribes its attribution cache to the same fan-out. With no
                // fan-out injected (unit tests construct the monitor directly) the filter is still
                // handed straight to the session, so the behaviour is unchanged.
                Action<FileIoRecord> onFileIo = _etwFilter.OnFileIo;
                if (_fanout is not null)
                {
                    _fanout.Subscribe(_etwFilter.OnFileIo);
                    onFileIo = _fanout.OnFileIo;
                }

                _correlator.EtwRunning = _etwSession.Start(onFileIo, _processTimes.OnProcessStart);
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
        // The agent's own restoration write (V4) is not an attack. ETW already leaves the agent's
        // PID out; the watcher does not know who wrote, so the path is marked before the rewrite.
        if (_ownWrites.IsOwn(filePath))
        {
            return;
        }

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
                CanaryDetection? watcherDetection = _correlator.OnWatcher(canary.FilePath, OperationOf(evt.AlertType));
                if (watcherDetection is null)
                {
                    continue;
                }

                await RaiseCanaryAlertAsync(canary, evt.AlertType, scope.ServiceProvider, cancellationToken, watcherDetection);

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

    /// <summary>
    /// Puts a modified or renamed canary back (V4): fresh content from its template at its own path,
    /// hash taken again, status Active, so the next write on it is detected too. Bounded by the same
    /// hourly limit as the regeneration after deletion: past it, SustainedAttack is recorded and the
    /// canary leaves the surveillance with its Modified / Renamed status, said in the log.
    /// </summary>
    private async Task TryRestoreCanaryAsync(SentinelCanary canary, CanaryAlertType alertType, IServiceProvider services)
    {
        SentinelOptions sentinel = _config.Sentinel!;
        var canaryRepo = services.GetRequiredService<ISentinelCanaryRepository>();
        var audit = services.GetRequiredService<IAuditLogRepository>();
        CanaryStatus failed = alertType == CanaryAlertType.CanaryRenamed ? CanaryStatus.Renamed : CanaryStatus.Modified;

        RegenerationStats stats = _regenTracking.GetOrAdd(canary.Directory, _ => new RegenerationStats());
        stats.CleanupOlderThan(TimeSpan.FromHours(1));
        if (stats.Count >= sentinel.MaxRegenerationsPerHourPerDirectory)
        {
            canary.Status = failed;
            _logger.LogCritical(
                "SENTINEL SUSTAINED ATTACK: {Count} restorations in 1 hour for {Directory}; canary {Path} leaves the surveillance. Manual intervention required.",
                stats.Count, canary.Directory, canary.FilePath);
            await RecordStatusAsync(canaryRepo, canary);
            await audit.AppendAsync("SustainedAttack",
                $"Canary restoration limit exceeded ({stats.Count}/{sentinel.MaxRegenerationsPerHourPerDirectory}) in {canary.Directory}",
                "SentinelCanary", canary.Id, CancellationToken.None);
            return;
        }

        try
        {
            // The agent's own write must not come back as a detection: the path is marked first.
            _ownWrites.Mark(canary.FilePath, AgentOwnWrites.RestoreGrace);
            var canaryService = services.GetRequiredService<ICanaryFileService>();
            await canaryService.RestoreCanaryAsync(canary, CancellationToken.None);
            stats.RecordRegeneration();
            await audit.AppendAsync("CanaryRestored",
                $"Canary restored after {alertType} at {canary.FilePath} (template: {canary.TemplateUsed}); still watched",
                "SentinelCanary", canary.Id, CancellationToken.None);
        }
        catch (Exception ex)
        {
            canary.Status = failed;
            _logger.LogError(ex, "SENTINEL: canary {Path} could not be restored after {AlertType}; it leaves the surveillance", canary.FilePath, alertType);
            await RecordStatusAsync(canaryRepo, canary);
        }
    }

    private async Task RecordStatusAsync(ISentinelCanaryRepository canaryRepo, SentinelCanary canary)
    {
        try
        {
            await canaryRepo.UpdateAsync(canary, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SENTINEL: status {Status} of {Path} not recorded", canary.Status, canary.FilePath);
        }
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
                // This sweep is the safety net for a write the watcher and ETW both missed. When
                // they did see it, they have already raised the alert that names the writer, and
                // the decoy is mid-restore -- so of course it does not match its hash. Raising
                // here would be a second Critical alert for one write, and this one could name
                // nobody: there is no write event left to attribute it to.
                if (_reported.IsBeingHandled(canary.FilePath))
                {
                    continue;
                }

                // The list this loop walks was read when the sweep began. A restore that finished
                // since then rewrote the decoy and stored a new hash, which leaves the copy in
                // hand stale: the file is correct and only looks modified, because it is being
                // compared against the hash it had before the attack.
                //
                // The re-read needs a scope of its own. This one's DbContext is where the stale
                // copy came from, and Entity Framework answers from what it is already tracking,
                // so asking it again would hand back the same stale object and prove nothing.
                using IServiceScope recheck = _scopeFactory.CreateScope();
                var recheckRepo = recheck.ServiceProvider.GetRequiredService<ISentinelCanaryRepository>();
                var recheckService = recheck.ServiceProvider.GetRequiredService<ICanaryFileService>();

                SentinelCanary? current = await recheckRepo.GetByIdAsync(canary.Id, cancellationToken);
                if (current is null || current.Status != CanaryStatus.Active
                    || await recheckService.VerifyCanaryIntegrityAsync(current, cancellationToken))
                {
                    continue;
                }

                // Asked once more: the real-time path may have raised the alert for this very
                // write while the re-read was in flight.
                if (_reported.IsBeingHandled(canary.FilePath))
                {
                    continue;
                }

                await RaiseCanaryAlertAsync(current, CanaryAlertType.CanaryModified, recheck.ServiceProvider, cancellationToken);
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
                    // The agent's own write, seen by the kernel. Filtering the agent's PID out of
                    // the session is not enough: Windows' cache manager flushes a file long after
                    // the process wrote it and the flush is attributed to a system PID, not to the
                    // writer. On the bench the canaries were planted at 17:51:28 and the kernel
                    // surfaced those same writes at 17:51:46 as "system_pid" -- eighteen seconds
                    // later, by which time nothing tied them to the agent any more. Every decoy a
                    // fresh install planted came back as a Critical attack on itself, each
                    // restoration produced another one, and SENTINEL ended up reporting a
                    // SUSTAINED ATTACK against a file only it had ever touched.
                    // Only a write that names nobody, on a path the agent has just written
                    // itself. PID 0 and 4 are the system's deferred writes -- the cache manager
                    // flushing what some process wrote earlier, which is how the agent's own
                    // canary writes come back (measured on the bench: written 17:51:28, reported
                    // 17:51:46 as system_pid). A write that names a real process is never
                    // dropped here, whatever path it is on: that is the writer SENTINEL exists to
                    // suspend, and dropping it left a canary's attacker running.
                    if (record.ProcessId is 0 or 4 && _ownWrites.WasWrittenByAgent(record.FileName))
                    {
                        continue;
                    }

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
                    await RaiseCanaryAlertAsync(canary, alertType, scope.ServiceProvider, cancellationToken, detection);
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
        CancellationToken cancellationToken = default,
        CanaryDetection? detection = null)
    {
        var canaryRepo = services.GetRequiredService<ISentinelCanaryRepository>();
        var auditRepo = services.GetRequiredService<IAuditLogRepository>();

        // The alert never waits for its attribution (ADR-025, section 3). With the ETW session
        // running, a detection leaves as "pending" and its complement follows, decided
        // CanaryAttributor.Delay after the first signal. Without it -- or for the integrity
        // check, which has no detection -- the attribution is final now: nobody is named.
        bool etwRunning = _correlator.EtwRunning;
        bool pending = detection is not null && etwRunning;
        string? finalReason = pending ? null : etwRunning ? AttributionReason.NoWriteEvent : AttributionReason.EtwUnavailable;

        var alert = new CanaryAlert
        {
            CanaryId = canary.Id,
            CanaryPath = canary.FilePath,
            AlertType = alertType,
            Severity = AlertSeverity.Critical,
        };

        // Claimed before the alert leaves, not after: everything below -- the enqueue, the
        // database, the audit entry, the restore -- takes time, and a sweep landing anywhere in
        // there would otherwise report this same modification again.
        if (alertType == CanaryAlertType.CanaryModified)
        {
            _reported.Reported(canary.FilePath);
        }

        // The GRID first (AGT-AUD-002): nothing local — database, audit — may delay or cancel
        // the alert. The forwarding queue persists it itself if the GRID is unreachable.
        AlertIngestRequest request = AlertMapper.FromCanaryAlert(alert, pending ? "pending" : "unavailable", finalReason);
        _alertQueue.TryEnqueue(request);
        _logger.LogCritical(
            "SENTINEL ALERT: {AlertType} on canary {CanaryPath} | attribution {Attribution} | Severity: {Severity}",
            alertType, canary.FilePath, pending ? "pending" : $"unavailable ({finalReason})", alert.Severity);
        if (pending)
        {
            _ = AttributeAsync(detection!, alert.Id, request.ClientMessageId);
        }

        // A modified or renamed canary stays watched (V4): its content is put back and its hash
        // taken again below, within the hourly limit. Before, the first alert set the status to
        // Modified, the repository only returns Active, and the canary left the surveillance: a
        // second write on it, 5 s later, raised nothing.
        bool restorable = alertType is CanaryAlertType.CanaryModified or CanaryAlertType.CanaryRenamed
            && _config.Sentinel?.EnableRealTimeRegeneration == true;
        canary.Status = restorable ? CanaryStatus.Active : alertType switch
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

        string details = $"Canary alert: {alertType} on {canary.FilePath}; attribution {(pending ? "pending" : $"unavailable ({finalReason})")}";

        // One INSERT, never a wait on the chain; never throws when it cannot record.
        await auditRepo.AppendAsync("CanaryAlert", details, "CanaryAlert", alert.Id, CancellationToken.None);

        if (restorable)
        {
            await TryRestoreCanaryAsync(canary, alertType, services);
        }

        // Released once the decoy is back and its new hash stored -- or once the restore has
        // failed and the decoy has left surveillance. Either way this modification is dealt with
        // and the sweep speaks for this path again, so a write landing from here on is reported
        // as the separate attack it is. Nothing is forgiven for a fixed stretch of time.
        if (alertType == CanaryAlertType.CanaryModified)
        {
            _reported.Handled(canary.FilePath);
        }

        // Genealogy enrichment is NOT started here any more. It used to run on the alert, before
        // the attribution existed, so it had nothing but the Restart Manager -- which names nobody
        // once the writer has closed the file. AttributeAsync now starts it with the writer it
        // resolved, 1.5 s later, which is the whole point of ADR-025's delay. A detection the
        // attribution cannot name gets the old file-lookup path from there.
    }

    /// <summary>
    /// The attribution of a detection (ADR-025, commit 4): decided CanaryAttributor.Delay after its
    /// first signal from the ETW writes it gathered, then sent as the alert's complement through the
    /// forwarding queue, after the alert. The Restart Manager is no longer asked: queried when the
    /// watcher fires, it never named the writer (AttributionBench, 0/60 on patterns 3, 5 and 6).
    /// </summary>
    private async Task AttributeAsync(CanaryDetection detection, Guid alertId, string clientMessageId)
    {
        try
        {
            TimeSpan wait = detection.FirstSeen + CanaryAttributor.Delay - _time.GetUtcNow();
            if (wait > TimeSpan.Zero)
            {
                await Task.Delay(wait, _time).ConfigureAwait(false);
            }

            AttributionOutcome outcome = _attributor.Resolve(detection, _correlator.EtwRunning);
            _alertQueue.TryEnqueueAttribution(clientMessageId, new AttributionComplementRequest
            {
                State = outcome.StateCode,
                Pid = outcome.ProcessId,
                ProcessCreatedAt = outcome.ProcessCreatedAtUtc,
                ProcessName = Truncate(outcome.ProcessName, 260),
                ImagePath = Truncate(outcome.ImagePath, 1024),
                Reason = outcome.Reason,
            });
            _logger.LogWarning(
                "SENTINEL attribution of {Path}: {State} {ProcessName} (PID {ProcessId}, created {CreatedAt:o}) {Reason}",
                detection.Path, outcome.StateCode, outcome.ProcessName, outcome.ProcessId, outcome.ProcessCreatedAtUtc, outcome.Reason);

            using IServiceScope scope = _scopeFactory.CreateScope();
            var audit = scope.ServiceProvider.GetRequiredService<IAuditLogRepository>();
            await audit.AppendAsync(
                "CanaryAttribution",
                $"{detection.Path}: {outcome.StateCode}"
                    + (outcome.ProcessId is int pid ? $" PID {pid} created {outcome.ProcessCreatedAtUtc:o} {outcome.ProcessName}" : $" ({outcome.Reason})"),
                "CanaryAlert", alertId, CancellationToken.None).ConfigureAwait(false);

            // GENEALOGY, on the writer this attribution named: its ancestry was captured while it
            // was running, so the chain is available even though the canary write is long finished.
            // Unattributed, the enricher falls back to the file lookup, as before.
            var enricher = scope.ServiceProvider.GetRequiredService<IGenealogyEnricher>();
            try
            {
                if (outcome.State == AttributionState.Resolved && outcome.ProcessId is int writerPid)
                {
                    await enricher.EnrichFromWriterAsync(
                        alertId, writerPid, outcome.ProcessCreatedAtUtc, detection.Path, CancellationToken.None)
                        .ConfigureAwait(false);
                }
                else
                {
                    await enricher.EnrichAlertAsync(alertId, detection.Path, CancellationToken.None).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                // Enrichment is forensic detail; the alert, its attribution and the response stand
                // without it.
                _logger.LogDebug(ex, "Genealogy enrichment failed for canary alert {AlertId}", alertId);
            }

            // The response (ADR-026): decided on the final attribution, after the complement left.
            if (_response is not null)
            {
                await _response.RespondAsync(outcome, detection.Path, alertId, clientMessageId).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            // The alert has left; a failed attribution leaves it pending in the console, and says so here.
            _logger.LogError(ex, "SENTINEL attribution failed for {Path}", detection.Path);
        }
    }

    private static string? Truncate(string? value, int max) =>
        value is null || value.Length <= max ? value : value[..max];
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
