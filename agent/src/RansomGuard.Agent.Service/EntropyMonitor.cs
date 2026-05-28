using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RansomGuard.Agent.Core.Configuration;
using RansomGuard.Agent.Core.Detection;
using RansomGuard.Agent.Core.Detection.Entropy;
using RansomGuard.Agent.Core.Persistence;
using RansomGuard.Agent.Core.Persistence.Entities;
using RansomGuard.Agent.Core.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
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

    /// <summary>
    /// Initializes the entropy monitor.
    /// </summary>
    public EntropyMonitor(
        ILogger<EntropyMonitor> logger,
        IOptionsMonitor<AgentConfiguration> config,
        IServiceScopeFactory scopeFactory,
        IFileEventDeduplicator deduplicator,
        IEntropyCalculator calculator,
        RateLimiterFactory? rateLimiterFactory = null,
        IDetectionEventBus? eventBus = null)
    {
        _logger = logger;
        _config = config.CurrentValue;
        _scopeFactory = scopeFactory;
        _deduplicator = deduplicator;
        _calculator = calculator;
        _rateLimiter = rateLimiterFactory?.GetLimiter(RateLimiterFactory.EntropyComputation);
        _eventBus = eventBus;
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
        EntropyOptions? entropy = _config.Entropy;
        if (entropy is null || !entropy.Enabled)
        {
            _logger.LogInformation("ENTROPY module is disabled");
            return;
        }

        // Wait for SENTINEL deployment and baseline scan
        await Task.Delay(5000, stoppingToken);

        // Build initial baselines
        await BuildBaselinesAsync(stoppingToken);

        // Start consumer
        Task consumerTask = ConsumeEventsAsync(entropy, stoppingToken);

        // Set up FSW on watch directories
        string[] resolvedPaths = EnvironmentVariableResolver.ResolvePaths(_config.Detection.WatchPaths);
        foreach (string dir in resolvedPaths)
        {
            if (!Directory.Exists(dir)) continue;

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

            _watchers.Add(watcher);
        }

        _logger.LogInformation("ENTROPY monitor active on {DirCount} directories", _watchers.Count);

        // Keep alive
        try { await Task.Delay(Timeout.Infinite, stoppingToken); }
        catch (OperationCanceledException) { }

        _fileChannel.Writer.TryComplete();
        await consumerTask;

        foreach (var w in _watchers) { w.EnableRaisingEvents = false; w.Dispose(); }
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
                    context.EntropyAlerts.Add(alert);
                    await context.SaveChangesAsync(ct);

                    await auditRepo.AppendAsync("EntropyAlert",
                        $"Rule {alert.RuleId} ({alert.RuleName}): {filePath} entropy {alert.BaselineEntropy:F2} -> {alert.CurrentEntropy:F2}",
                        "EntropyAlert", alert.Id, ct);

                    _logger.LogCritical(
                        "ENTROPY ALERT: Rule {RuleId} ({RuleName}) on {FilePath} | Baseline: {Baseline:F2} Current: {Current:F2} Delta: {Delta:F2} | Severity: {Severity}",
                        alert.RuleId, alert.RuleName, filePath,
                        alert.BaselineEntropy, alert.CurrentEntropy, alert.Delta, alert.Severity);

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
