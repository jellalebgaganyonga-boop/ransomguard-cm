using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RansomGuard.Agent.Core.Configuration;
using RansomGuard.Agent.Core.Diagnostics;
using RansomGuard.Agent.Core.Detection;
using RansomGuard.Agent.Core.Detection.Entropy;
using RansomGuard.Agent.Core.Persistence;
using RansomGuard.Agent.Core.Persistence.Entities;
using RansomGuard.Agent.Core.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using RansomGuard.Agent.Core.Communication;
using RansomGuard.Agent.Core.Detection.CrossModule;
using RansomGuard.Agent.Core.Detection.Genealogy;
using RansomGuard.Agent.Core.Detection.Sentinel.Etw;
using RansomGuard.Agent.Core.Security.RateLimiting;

namespace RansomGuard.Agent.Service;

/// <summary>
/// BackgroundService that monitors file entropy changes in real-time.
/// Computes Shannon entropy on file modifications and fires alerts when
/// encryption activity is detected via the EntropyDetector rule engine.
/// </summary>
public sealed class EntropyMonitor : BackgroundService
{
    private const int EventChannelCapacity = 10_000;

    private readonly ILogger<EntropyMonitor> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IFileEventDeduplicator _deduplicator;
    private readonly IEntropyCalculator _calculator;
    private readonly AgentConfiguration _config;
    private readonly Channel<string> _fileChannel;
    private readonly List<FileSystemWatcher> _watchers = [];
    private readonly IOperationRateLimiter? _rateLimiter;
    private readonly IDetectionEventBus? _eventBus;
    private readonly IAlertForwardingQueue? _alertQueue;
    private readonly UserProfileEnumerator _profiles;
    private readonly IModuleStateRegistry _moduleState;
    private readonly IModuleFailureReporter _failureReporter;
    private readonly IModuleFaultInjector _faultInjector;
    private readonly TimeSpan _startupDelay;
    private readonly TaskCompletionSource _watcherFault = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// Who wrote the file ENTROPY just read (AGT-ATTR-001). Null in unit tests that build the
    /// monitor by hand: an alert then carries no writer, as before.
    /// </summary>
    private readonly FileWriteAttributionCache? _attribution;

    /// <summary>The fan-out the cache subscribes to; null means no attribution source.</summary>
    private readonly FileIoFanout? _fanout;

    /// <summary>The session, read for whether it is running when an attribution is decided.</summary>
    private readonly EtwFileIoSession? _etwSession;

    /// <summary>
    /// The attribution capability the module state was last published for; null before the first
    /// publish, so the startup state is always written once.
    /// </summary>
    private bool? _lastPublishedCanAttribute;

    /// <summary>
    /// Initializes the entropy monitor.
    /// </summary>
    /// <param name="logger">Logger.</param>
    /// <param name="config">Agent configuration.</param>
    /// <param name="scopeFactory">Scope factory for per-event services.</param>
    /// <param name="deduplicator">File event deduplicator.</param>
    /// <param name="calculator">Shannon entropy calculator.</param>
    /// <param name="moduleState">Runtime module-state registry.</param>
    /// <param name="failureReporter">Reports the module going down.</param>
    /// <param name="rateLimiterFactory">Rate limiter for entropy computations.</param>
    /// <param name="eventBus">Cross-module detection bus.</param>
    /// <param name="alertQueue">Queue forwarding alerts to the GRID.</param>
    /// <param name="startupDelay">
    /// Test seam: wait before starting, for SENTINEL's deployment; null (the DI default) is 5 s.
    /// </param>
    /// <param name="faultInjector">Development fault injection; null or the production one never stops anything.</param>
    /// <param name="profiles">The real user profiles that %EACHUSERPROFILE% expands to (AGT-PATH-001); null: the Windows registry.</param>
    /// <param name="attribution">Names the process that wrote a file (AGT-ATTR-001); null leaves alerts without a writer.</param>
    /// <param name="fanout">The session fan-out the cache subscribes to; null leaves the cache unfed.</param>
    /// <param name="etwSession">The session, read for whether it is running; null means attribution answers etw_unavailable.</param>
    public EntropyMonitor(
        ILogger<EntropyMonitor> logger,
        IOptionsMonitor<AgentConfiguration> config,
        IServiceScopeFactory scopeFactory,
        IFileEventDeduplicator deduplicator,
        IEntropyCalculator calculator,
        IModuleStateRegistry moduleState,
        IModuleFailureReporter failureReporter,
        RateLimiterFactory? rateLimiterFactory = null,
        IDetectionEventBus? eventBus = null,
        IAlertForwardingQueue? alertQueue = null,
        TimeSpan? startupDelay = null,
        IModuleFaultInjector? faultInjector = null,
        UserProfileEnumerator? profiles = null,
        FileWriteAttributionCache? attribution = null,
        FileIoFanout? fanout = null,
        EtwFileIoSession? etwSession = null)
    {
        _attribution = attribution;
        _fanout = fanout;
        _etwSession = etwSession;
        _logger = logger;
        _config = config.CurrentValue;
        _scopeFactory = scopeFactory;
        _deduplicator = deduplicator;
        _calculator = calculator;
        _moduleState = moduleState;
        _failureReporter = failureReporter;
        _faultInjector = faultInjector ?? new NoModuleFaultInjector();
        _startupDelay = startupDelay ?? TimeSpan.FromSeconds(5);
        _rateLimiter = rateLimiterFactory?.GetLimiter(RateLimiterFactory.EntropyComputation);
        _eventBus = eventBus;
        _alertQueue = alertQueue;
        _profiles = profiles ?? UserProfileEnumerator.Windows;
        _fileChannel = Channel.CreateBounded<string>(
            new BoundedChannelOptions(EventChannelCapacity)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true
            });
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        bool reachedRunningState = false;
        Task? consumerTask = null;

        // Every task of the module runs on this token, so that after a failure
        // nothing keeps running unseen.
        using var moduleCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        CancellationToken moduleToken = moduleCts.Token;

        try
        {
            EntropyOptions? entropy = _config.Entropy;
            if (entropy is null || !entropy.Enabled)
            {
                _logger.LogInformation("ENTROPY module is disabled");
                _moduleState.Publish(ModuleCode.Entropy, ModuleState.DisabledByConfig, ModuleReasonCode.DisabledByConfig);
                return;
            }

            // Wait for SENTINEL deployment and baseline scan
            await Task.Delay(_startupDelay, stoppingToken);

            // Build initial baselines
            await BuildBaselinesAsync(stoppingToken);

            consumerTask = ConsumeEventsAsync(entropy, moduleToken);

            // Set up FSW on watch directories
            string[] resolvedPaths = EnvironmentVariableResolver.ResolvePaths(_config.Detection.WatchPaths, _profiles, _logger);
            foreach (string dir in resolvedPaths)
            {
                // AGT-PATH-001: a system or service profile is never watched -- it counts as missing.
                if (WatchedPathPolicy.IsRefused(dir))
                {
                    _logger.LogError("ENTROPY: folder {Directory} is in a system or service profile and is not watched (AGT-PATH-001)", dir);
                    continue;
                }

                if (!Directory.Exists(dir))
                {
                    _logger.LogWarning("ENTROPY: folder {Directory} does not exist and is not watched", dir);
                    continue;
                }

                var watcher = new FileSystemWatcher(dir)
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
                    IncludeSubdirectories = true,
                    EnableRaisingEvents = true
                };

                watcher.Changed += (_, e) =>
                {
                    if (_deduplicator.ShouldProcess(e.FullPath, e.ChangeType))
                    {
                        _fileChannel.Writer.TryWrite(e.FullPath);
                    }
                };
                watcher.Error += OnWatcherError;

                _watchers.Add(watcher);
            }

            // AGT-ATTR-001: subscribe the attribution cache to the same kernel session SENTINEL
            // opens, bounded to the folders actually being watched. Only the folders whose watcher
            // was created are given: a folder that does not exist or was refused generates no event
            // worth keeping. Without a fan-out or a cache, nothing is subscribed and an alert
            // carries no writer, exactly as before.
            if (_attribution is not null && _fanout is not null)
            {
                string[] watchedDirs = [.. _watchers.Select(w => w.Path)];
                _attribution.SetWatchedDirectories(watchedDirs);
                _fanout.Subscribe(_attribution.OnFileIo);
                _logger.LogInformation(
                    "ENTROPY attribution: reading the kernel FileIO session for {Count} folders; an alert can name the process that wrote the file",
                    watchedDirs.Length);
            }

            // Zero watcher is not "active": the module would see no modification at all.
            if (_watchers.Count == 0)
            {
                _logger.LogError("ENTROPY watches nothing: none of the {Count} configured folders is watchable (missing, or refused as a system profile)",
                    resolvedPaths.Length);
                _moduleState.Publish(ModuleCode.Entropy, ModuleState.Inactive, ModuleReasonCode.NoWatchDirectory);
                return;
            }

            _logger.LogInformation("ENTROPY monitor watching {DirCount} directories", _watchers.Count);

            // What ENTROPY may claim about itself. Both gaps that held it at degraded since B4
            // (2026-10-22) are now closed:
            //
            //   AGT-ATTR-001 -- an alert carries the process that wrote the file, read from the
            //                   kernel FileIO session through FileWriteAttributionCache.
            //   AGT-EN-002   -- rule 3 is evaluated: DirectoryEntropyWindow counts the distinct
            //                   files of a folder inside DirectoryShiftWindowSeconds and
            //                   EntropyDetector.AnalyzeDirectoryShift decides on them.
            //
            // One condition is left, and it is a runtime one: naming a writer needs the kernel
            // session, and that session needs administrator rights and a free system-logger slot.
            // Without it the detection still runs but no alert can name anybody, which is exactly
            // what degraded/attribution_unreliable means. So the state follows the session rather
            // than the build: active when it is running, degraded when it is not, re-published if
            // that changes while the agent runs (PublishEntropyState, called from ResolveWriter).
            PublishEntropyState(entropy);
            reachedRunningState = true;

            await ModuleTaskObserver.ObserveUntilStoppedAsync(
                stoppingToken,
                ("consumer", consumerTask),
                ("watchers", _watcherFault.Task),
                ("fault-injection", _faultInjector.WhenStopRequestedAsync(ModuleCode.Entropy, moduleToken)));
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
            await _failureReporter.ReportStoppedAsync(ModuleCode.Entropy, reachedRunningState, ex);
        }
        finally
        {
            await StopModuleAsync(moduleCts, consumerTask);
        }
    }

    /// <summary>
    /// Rule 3 (AGT-EN-002): many different files in one folder turning high-entropy at once is
    /// mass encryption. Called after each per-file alert; raises at most one folder-wide Critical
    /// per window, so a burst of fifty files is one alert naming the folder, not fifty.
    ///
    /// Never throws: the per-file alert has already left, and rule 3 failing must not cost it.
    /// </summary>
    private async Task EvaluateDirectoryShiftAsync(
        EntropyDetector detector,
        DirectoryEntropyWindow window,
        string filePath,
        EntropyAlert fileAlert,
        AttributionOutcome fileAttribution,
        AgentDbContext context,
        IAuditLogRepository auditRepo)
    {
        try
        {
            int? writerPid = fileAttribution.State == AttributionState.Resolved ? fileAttribution.ProcessId : null;
            DirectorySnapshot snapshot = window.Record(filePath, fileAlert.Delta, writerPid);
            if (snapshot.InCooldown)
            {
                return; // this folder already said it; the rest of the burst is the same event
            }

            string directory = DirectoryEntropyWindow.DirectoryOf(filePath);
            EntropyAlert? shift = detector.AnalyzeDirectoryShift(directory, snapshot.FileCount, snapshot.AverageDelta);
            if (shift is null)
            {
                return;
            }

            window.MarkFired(filePath);

            // The folder-wide alert names the writer only when every attributed file in the window
            // agrees on one process. Files nobody could attribute do not contradict it; two
            // different writers do, and then nobody is named.
            AttributionOutcome shiftAttribution = snapshot.UnanimousWriterPid is int pid
                ? (fileAttribution.State == AttributionState.Resolved && fileAttribution.ProcessId == pid
                    ? fileAttribution
                    : new AttributionOutcome(AttributionState.Resolved, pid))
                : new AttributionOutcome(AttributionState.Ambiguous);

            var mappedShift = AlertMapper.FromEntropyAlert(shift, shiftAttribution);
            if (mappedShift is not null)
            {
                _alertQueue?.TryEnqueue(mappedShift);
            }

            _logger.LogCritical(
                "ENTROPY MASS ENCRYPTION: {Count} files in {Directory} turned high-entropy within {Window}s "
                + "(average rise {Average:F2}) | Writer: {Writer}",
                snapshot.FileCount, directory, _config.Entropy?.DirectoryShiftWindowSeconds, snapshot.AverageDelta,
                shiftAttribution.State == AttributionState.Resolved
                    ? $"PID {shiftAttribution.ProcessId}"
                    : "not named (several writers or none attributed)");

            try
            {
                context.EntropyAlerts.Add(shift);
                await context.SaveChangesAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ENTROPY rule 3 alert {AlertId} sent, but its local record failed", shift.Id);
                context.Entry(shift).State = EntityState.Detached;
            }

            await auditRepo.AppendAsync("EntropyDirectoryShift",
                $"{snapshot.FileCount} files in {directory}, average entropy rise {snapshot.AverageDelta:F2}",
                "EntropyAlert", shift.Id, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ENTROPY rule 3 evaluation failed for {FilePath}; the per-file alert is unaffected", filePath);
        }
    }

    /// <summary>
    /// Whether an alert can name its writer right now: the cache is wired and the kernel session is
    /// running. Everything else about ENTROPY (reading files, rules 1-3) works either way.
    /// </summary>
    private bool CanAttributeNow =>
        _attribution is not null && _fanout is not null && _etwSession?.State == EtwSessionState.Running;

    /// <summary>
    /// Publishes active or degraded from the one runtime condition that remains (AGT-ATTR-001):
    /// the kernel session. Logs only on a change, so a stopped session says so once rather than
    /// on every alert.
    /// </summary>
    private void PublishEntropyState(EntropyOptions entropy)
    {
        bool canAttribute = CanAttributeNow;
        if (_lastPublishedCanAttribute == canAttribute)
        {
            return;
        }

        _lastPublishedCanAttribute = canAttribute;
        if (canAttribute)
        {
            _logger.LogInformation(
                "ENTROPY active: an alert names the process that wrote the file, and rule 3 is evaluated "
                + "(threshold {Shift}, window {Window}s, at least {MinFiles} files).",
                entropy.DirectoryShiftThreshold, entropy.DirectoryShiftWindowSeconds, entropy.DirectoryShiftMinFiles);
            _moduleState.Publish(ModuleCode.Entropy, ModuleState.Active);
            return;
        }

        _logger.LogWarning(
            "ENTROPY degraded: the kernel FileIO session is not running, so an encrypted write is reported "
            + "without its author (AGT-ATTR-001). Detection and rule 3 are unaffected. The session needs "
            + "administrator rights and a free system-logger slot; SENTINEL's log says which is missing.");
        _moduleState.Publish(ModuleCode.Entropy, ModuleState.Degraded, ModuleReasonCode.AttributionUnreliable);
    }

    /// <summary>
    /// Names the process that wrote <paramref name="filePath"/>, or says why nobody is named
    /// (AGT-ATTR-001). Never throws: a failed attribution must not cost the detection.
    /// </summary>
    private AttributionOutcome ResolveWriter(string filePath)
    {
        // The session can go down (stopped from outside) or come back (its restart timer) while the
        // agent runs; the module state must follow it rather than stay at whatever startup saw.
        if (_config.Entropy is not null)
        {
            PublishEntropyState(_config.Entropy);
        }

        if (_attribution is null)
        {
            return new AttributionOutcome(AttributionState.Unavailable, Reason: AttributionReason.EtwUnavailable);
        }

        try
        {
            // The session running right now, not when the write happened: a session that has since
            // stopped means the cache may be missing the record, and "could not see" is the honest
            // answer rather than "saw nothing".
            bool etwRunning = _etwSession?.State == EtwSessionState.Running;
            return _attribution.Resolve(filePath, etwRunning);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ENTROPY attribution failed for {FilePath}; the alert leaves without a writer", filePath);
            return new AttributionOutcome(AttributionState.Unavailable, Reason: AttributionReason.NoWriteEvent);
        }
    }

    /// <summary>
    /// A watcher reports an error. A buffer overflow loses events but the watcher
    /// keeps running; any other error means it has stopped and ENTROPY is blind.
    /// </summary>
    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        Exception error = e.GetException();
        if (error is InternalBufferOverflowException)
        {
            // ReportEventsLostAsync never throws, and writes at most once a minute.
            // A burst of overflows here is itself a hint of mass modification (AGT-FSW-001).
            _ = _failureReporter.ReportEventsLostAsync(ModuleCode.Entropy, error);
            return;
        }

        _watcherFault.TrySetException(error);
    }

    /// <summary>Stops every source and task of the module; best effort, never throws.</summary>
    private async Task StopModuleAsync(CancellationTokenSource moduleCts, Task? consumerTask)
    {
        foreach (FileSystemWatcher watcher in _watchers)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
        }

        _watchers.Clear();
        _fileChannel.Writer.TryComplete();
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
                _logger.LogDebug(ex, "ENTROPY consumer ended during stop");
            }
        }
    }

    private async Task BuildBaselinesAsync(CancellationToken ct)
    {
        string[] resolvedPaths = EnvironmentVariableResolver.ResolvePaths(_config.Detection.WatchPaths, _profiles, _logger);
        int total = 0;

        foreach (string dir in resolvedPaths)
        {
            if (!Directory.Exists(dir)) continue;

            string[] files;
            try { files = Directory.GetFiles(dir, "*", SearchOption.AllDirectories); }
            catch { continue; }

            using IServiceScope scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AgentDbContext>();

            foreach (string file in files)
            {
                if (ct.IsCancellationRequested) return;

                string ext = Path.GetExtension(file).ToLowerInvariant();
                long size = 0;
                try { size = new FileInfo(file).Length; } catch { continue; }

                if (size == 0 || size > (_config.Entropy?.MaxFileSizeMB ?? 100) * 1024L * 1024L) continue;

                // Skip if baseline already exists
                bool exists = await context.EntropyBaselines.AnyAsync(b => b.FilePath == file, ct);
                if (exists) continue;

                double? entropy = await _calculator.ComputeFileEntropyAsync(file, ct);
                if (entropy is null) continue;

                context.EntropyBaselines.Add(new EntropyBaseline
                {
                    FilePath = file,
                    DirectoryPath = Path.GetDirectoryName(file) ?? dir,
                    FileExtension = ext,
                    EntropyValue = entropy.Value,
                    FileSize = size
                });
                total++;

                if (total % 100 == 0)
                {
                    await context.SaveChangesAsync(ct);
                    _logger.LogInformation("ENTROPY baseline: {Count} files profiled", total);
                }
            }

            await context.SaveChangesAsync(ct);
        }

        _logger.LogInformation("ENTROPY baseline complete: {Total} files profiled", total);
    }

    private async Task ConsumeEventsAsync(EntropyOptions options, CancellationToken ct)
    {
        var detector = new EntropyDetector(options);

        // AGT-EN-002: rule 3's window. EntropyDetector.AnalyzeDirectoryShift has always existed and
        // nothing ever called it, because nothing counted how many *different* files in one folder
        // had turned high-entropy. This is that counter.
        var directoryWindow = new DirectoryEntropyWindow(options.DirectoryShiftWindowSeconds);

        await foreach (string filePath in _fileChannel.Reader.ReadAllAsync(ct))
        {
            try
            {
                // Skip canary files (SENTINEL handles those)
                if (Path.GetFileName(filePath).StartsWith(_config.Sentinel?.CanaryPrefix ?? "0001_"))
                    continue;

                long size = 0;
                try { size = new FileInfo(filePath).Length; } catch { continue; }
                if (size == 0 || size > options.MaxFileSizeMB * 1024L * 1024L) continue;

                double? currentEntropy = await _calculator.ComputeFileEntropyAsync(filePath, ct);
                if (currentEntropy is null) continue;

                using IServiceScope scope = _scopeFactory.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<AgentDbContext>();
                var auditRepo = scope.ServiceProvider.GetRequiredService<IAuditLogRepository>();

                EntropyBaseline? baseline = await context.EntropyBaselines
                    .FirstOrDefaultAsync(b => b.FilePath == filePath, ct);

                EntropyAlert? alert = detector.Analyze(filePath, currentEntropy.Value, baseline);

                if (alert is not null)
                {
                    // AGT-ATTR-001: who wrote it. Decided before the alert leaves, so the GRID gets
                    // the writer with the detection rather than in a later complement. The cache
                    // answers "unavailable" with a reason far more often than it names somebody
                    // (no ETW, a write by PID 4, two writers, a reused PID) -- and the alert says
                    // which, rather than implying nobody touched the file.
                    AttributionOutcome attribution = ResolveWriter(filePath);

                    // The GRID first (AGT-AUD-002), skipping Suppressed alerts: nothing local —
                    // database, audit — may delay or cancel the alert.
                    var mapped = AlertMapper.FromEntropyAlert(alert, attribution);
                    if (mapped is not null) _alertQueue?.TryEnqueue(mapped);

                    _logger.LogCritical(
                        "ENTROPY ALERT: Rule {RuleId} ({RuleName}) on {FilePath} | Baseline: {Baseline:F2} Current: {Current:F2} Delta: {Delta:F2} | Severity: {Severity} | Writer: {Writer}",
                        alert.RuleId, alert.RuleName, filePath,
                        alert.BaselineEntropy, alert.CurrentEntropy, alert.Delta, alert.Severity,
                        attribution.State == AttributionState.Resolved
                            ? $"{attribution.ProcessName} (PID {attribution.ProcessId})"
                            : $"not named ({attribution.StateCode}{(attribution.Reason is null ? "" : "/" + attribution.Reason)})");

                    // From here the alert has left: its local record and its audit entry are
                    // completed even during a shutdown, so a sent detection never lacks its trace.
                    try
                    {
                        context.EntropyAlerts.Add(alert);
                        await context.SaveChangesAsync(CancellationToken.None);
                    }
                    catch (Exception ex)
                    {
                        // The alert is already on its way to the GRID; the local copy is what failed.
                        _logger.LogError(ex, "ENTROPY alert {AlertId} sent, but its local record failed", alert.Id);
                        context.Entry(alert).State = EntityState.Detached;
                    }

                    // One INSERT, never a wait on the chain; never throws when it cannot record.
                    await auditRepo.AppendAsync("EntropyAlert",
                        $"Rule {alert.RuleId} ({alert.RuleName}): {filePath} entropy {alert.BaselineEntropy:F2} -> {alert.CurrentEntropy:F2}",
                        "EntropyAlert", alert.Id, CancellationToken.None);

                    // AGT-EN-002, rule 3: this file joins its folder's window; if enough *different*
                    // files in that folder have turned high-entropy inside the window, that is mass
                    // encryption and it is reported as one folder-wide Critical, not one per file.
                    await EvaluateDirectoryShiftAsync(
                        detector, directoryWindow, filePath, alert, attribution, context, auditRepo);

                    // Fire-and-forget genealogy enrichment. When the writer was attributed, start
                    // from it: its ancestry was captured while it was running, so the chain is
                    // there even though the process has since closed the file and exited. Without
                    // an attribution the enricher falls back to the file lookup.
                    Guid alertId = alert.Id;
                    int? enrichPid = attribution.State == AttributionState.Resolved ? attribution.ProcessId : null;
                    DateTime? enrichStart = attribution.ProcessCreatedAtUtc;
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            using var enrichScope = _scopeFactory.CreateScope();
                            var enricher = enrichScope.ServiceProvider.GetRequiredService<IGenealogyEnricher>();
                            if (enrichPid is int pid)
                            {
                                await enricher.EnrichFromWriterAsync(alertId, pid, enrichStart, filePath, default);
                            }
                            else
                            {
                                await enricher.EnrichAlertAsync(alertId, filePath, default);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogDebug(ex, "Genealogy enrichment failed for entropy alert {AlertId}", alertId);
                        }
                    }, CancellationToken.None);

                    // Publish EntropySignal for cross-module correlation (fire-and-forget).
                    // The signal carries the attributed writer when there is one; 0 and empty keep
                    // their old meaning -- "nobody named" -- so a consumer that correlates on PID
                    // behaves exactly as before when the attribution could not resolve.
                    if (_eventBus is not null)
                    {
                        _ = _eventBus.PublishAsync(new EntropySignal
                        {
                            SignalId = Guid.NewGuid(),
                            SourceModule = "ENTROPY",
                            EmittedAt = DateTime.UtcNow,
                            FilePath = filePath,
                            ProcessId = attribution.ProcessId ?? 0,
                            ProcessName = attribution.ProcessName ?? string.Empty,
                            EntropyValue = alert.CurrentEntropy,
                            FileSize = size,
                            EntropyAlertId = alert.Id
                        }, ct);
                    }
                }
                else if (baseline is null)
                {
                    // New file — create baseline
                    context.EntropyBaselines.Add(new EntropyBaseline
                    {
                        FilePath = filePath,
                        DirectoryPath = Path.GetDirectoryName(filePath) ?? "",
                        FileExtension = Path.GetExtension(filePath).ToLowerInvariant(),
                        EntropyValue = currentEntropy.Value,
                        FileSize = size
                    });
                    await context.SaveChangesAsync(ct);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing entropy for {FilePath}", filePath);
            }

            // Rate limit: ~100 calculations/sec via token bucket (fallback to 10ms delay)
            if (_rateLimiter is not null)
                await _rateLimiter.AcquireAsync(ct);
            else
                await Task.Delay(10, ct);
        }
    }
}
