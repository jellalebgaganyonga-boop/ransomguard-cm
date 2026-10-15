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
    private readonly IModuleStateRegistry _moduleState;
    private readonly IModuleFailureReporter _failureReporter;
    private readonly IModuleFaultInjector _faultInjector;
    private readonly TimeSpan _startupDelay;
    private readonly TaskCompletionSource _watcherFault = new(TaskCreationOptions.RunContinuationsAsynchronously);

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
        IModuleFaultInjector? faultInjector = null)
    {
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
        bool reachedActive = false;
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
            string[] resolvedPaths = EnvironmentVariableResolver.ResolvePaths(_config.Detection.WatchPaths);
            foreach (string dir in resolvedPaths)
            {
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

            // Zero watcher is not "active": the module would see no modification at all.
            if (_watchers.Count == 0)
            {
                _logger.LogError("ENTROPY watches nothing: none of the {Count} configured folders exists",
                    resolvedPaths.Length);
                _moduleState.Publish(ModuleCode.Entropy, ModuleState.Inactive, ModuleReasonCode.NoWatchDirectory);
                return;
            }

            _logger.LogInformation("ENTROPY monitor active on {DirCount} directories", _watchers.Count);
            _moduleState.Publish(ModuleCode.Entropy, ModuleState.Active);
            reachedActive = true;

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
            await _failureReporter.ReportStoppedAsync(ModuleCode.Entropy, reachedActive, ex);
        }
        finally
        {
            await StopModuleAsync(moduleCts, consumerTask);
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
        string[] resolvedPaths = EnvironmentVariableResolver.ResolvePaths(_config.Detection.WatchPaths);
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
                    // The GRID first (AGT-AUD-002), skipping Suppressed alerts: nothing local —
                    // database, audit — may delay or cancel the alert.
                    var mapped = AlertMapper.FromEntropyAlert(alert);
                    if (mapped is not null) _alertQueue?.TryEnqueue(mapped);

                    _logger.LogCritical(
                        "ENTROPY ALERT: Rule {RuleId} ({RuleName}) on {FilePath} | Baseline: {Baseline:F2} Current: {Current:F2} Delta: {Delta:F2} | Severity: {Severity}",
                        alert.RuleId, alert.RuleName, filePath,
                        alert.BaselineEntropy, alert.CurrentEntropy, alert.Delta, alert.Severity);

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

                    // Fire-and-forget genealogy enrichment for process attribution
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            using var enrichScope = _scopeFactory.CreateScope();
                            var enricher = enrichScope.ServiceProvider.GetRequiredService<IGenealogyEnricher>();
                            await enricher.EnrichAlertAsync(alert.Id, filePath, default);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogDebug(ex, "Genealogy enrichment failed for entropy alert {AlertId}", alert.Id);
                        }
                    }, CancellationToken.None);

                    // Publish EntropySignal for cross-module correlation (fire-and-forget)
                    if (_eventBus is not null)
                    {
                        _ = _eventBus.PublishAsync(new EntropySignal
                        {
                            SignalId = Guid.NewGuid(),
                            SourceModule = "ENTROPY",
                            EmittedAt = DateTime.UtcNow,
                            FilePath = filePath,
                            ProcessId = 0,
                            ProcessName = string.Empty,
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
