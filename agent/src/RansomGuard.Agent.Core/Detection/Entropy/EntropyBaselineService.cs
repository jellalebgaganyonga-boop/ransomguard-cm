using Microsoft.Extensions.Logging;
using RansomGuard.Agent.Core.Persistence.Entities;
using RansomGuard.Agent.Core.Persistence.Repositories;
using RansomGuard.Agent.Core.Security.RateLimiting;

namespace RansomGuard.Agent.Core.Detection.Entropy;

/// <summary>
/// Manages per-file entropy baselines. Builds baselines at startup by scanning
/// directories, rate-limited at 50 files/sec via TokenBucketRateLimiter.
/// Baselines are frozen after alert to preserve forensic evidence.
/// </summary>
public sealed class EntropyBaselineService : IEntropyBaselineService
{
    private readonly IEntropyBaselineRepository _repository;
    private readonly IEntropyCalculator _calculator;
    private readonly ILogger<EntropyBaselineService> _logger;
    private readonly IOperationRateLimiter? _rateLimiter;

    /// <summary>
    /// Initializes the baseline service.
    /// </summary>
    public EntropyBaselineService(
        IEntropyBaselineRepository repository,
        IEntropyCalculator calculator,
        ILogger<EntropyBaselineService> logger,
        IOperationRateLimiter? rateLimiter = null)
    {
        _repository = repository;
        _calculator = calculator;
        _logger = logger;
        _rateLimiter = rateLimiter;
    }

    /// <inheritdoc />
    public async Task BuildBaselineAsync(string directoryPath, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(directoryPath)) return;

        string[] files;
        try { files = Directory.GetFiles(directoryPath, "*", SearchOption.AllDirectories); }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cannot enumerate {Directory} for baseline", directoryPath);
            return;
        }

        int processed = 0;
        foreach (string file in files)
        {
            if (cancellationToken.IsCancellationRequested) break;

            string fileName = Path.GetFileName(file);
            if (fileName.StartsWith("0001_")) continue; // Skip SENTINEL canaries

            long size;
            try { size = new FileInfo(file).Length; } catch { continue; }
            if (size == 0 || size > 100 * 1024L * 1024L) continue; // Skip empty and > 100 MB

            // Check if baseline already exists
            EntropyBaseline? existing = await _repository.GetByPathAsync(file, cancellationToken);
            if (existing is not null) continue;

            double? entropy = await _calculator.ComputeFileEntropyAsync(file, cancellationToken);
            if (entropy is null) continue;

            await _repository.UpsertAsync(new EntropyBaseline
            {
                FilePath = file,
                DirectoryPath = Path.GetDirectoryName(file) ?? directoryPath,
                FileExtension = Path.GetExtension(file).ToLowerInvariant(),
                EntropyValue = entropy.Value,
                FileSize = size
            }, cancellationToken);

            processed++;

            if (processed % 100 == 0)
            {
                _logger.LogInformation("ENTROPY baseline: {Count}/{Total} files in {Directory}",
                    processed, files.Length, directoryPath);
            }

            // Rate limit: ~50 files/sec via token bucket (fallback to 20ms delay)
            if (_rateLimiter is not null)
                await _rateLimiter.AcquireAsync(cancellationToken);
            else
                await Task.Delay(20, cancellationToken);
        }

        _logger.LogInformation("ENTROPY baseline complete for {Directory}: {Count} files profiled", directoryPath, processed);
    }

    /// <inheritdoc />
    public async Task<EntropyBaseline?> GetBaselineAsync(string filePath, CancellationToken cancellationToken = default)
    {
        return await _repository.GetByPathAsync(filePath, cancellationToken);
    }

    /// <inheritdoc />
    public async Task UpdateBaselineAsync(string filePath, double newEntropy, long newSize, CancellationToken cancellationToken = default)
    {
        EntropyBaseline? existing = await _repository.GetByPathAsync(filePath, cancellationToken);
        if (existing is null) return;

        await _repository.UpsertAsync(new EntropyBaseline
        {
            Id = existing.Id,
            FilePath = filePath,
            DirectoryPath = existing.DirectoryPath,
            FileExtension = existing.FileExtension,
            EntropyValue = newEntropy,
            FileSize = newSize,
            CapturedAt = existing.CapturedAt,
            LastVerifiedAt = DateTime.UtcNow
        }, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<double?> GetDirectoryAverageEntropyAsync(string directoryPath, CancellationToken cancellationToken = default)
    {
        return await _repository.GetDirectoryAverageAsync(directoryPath, cancellationToken);
    }

    /// <inheritdoc />
    public async Task RebuildIfStaleAsync(string directoryPath, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<EntropyBaseline> baselines = await _repository.GetByDirectoryAsync(directoryPath, cancellationToken);
        if (baselines.Count == 0)
        {
            await BuildBaselineAsync(directoryPath, cancellationToken);
            return;
        }

        DateTime oldest = baselines.Min(b => b.CapturedAt);
        if ((DateTime.UtcNow - oldest).TotalDays > 7)
        {
            _logger.LogInformation("ENTROPY baseline stale (>{Days} days) for {Directory}, rebuilding",
                7, directoryPath);
            await BuildBaselineAsync(directoryPath, cancellationToken);
        }
    }
}
