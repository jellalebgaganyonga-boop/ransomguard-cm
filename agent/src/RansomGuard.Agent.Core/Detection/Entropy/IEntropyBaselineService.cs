using RansomGuard.Agent.Core.Persistence.Entities;

namespace RansomGuard.Agent.Core.Detection.Entropy;

/// <summary>
/// Manages per-file entropy baselines for delta-based ransomware detection.
/// Baselines are built at startup, periodically refreshed, and frozen after alerts.
/// </summary>
public interface IEntropyBaselineService
{
    /// <summary>
    /// Builds entropy baselines for all eligible files in a directory.
    /// Rate-limited, batched, and idempotent.
    /// </summary>
    Task BuildBaselineAsync(string directoryPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves the baseline for a specific file.
    /// </summary>
    Task<EntropyBaseline?> GetBaselineAsync(string filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the baseline for a file (only if no alert has been fired for it).
    /// </summary>
    Task UpdateBaselineAsync(string filePath, double newEntropy, long newSize, CancellationToken cancellationToken = default);

    /// <summary>
    /// Computes the average entropy across all baselined files in a directory.
    /// </summary>
    Task<double?> GetDirectoryAverageEntropyAsync(string directoryPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Rebuilds baseline if older than the configured rebuild interval.
    /// </summary>
    Task RebuildIfStaleAsync(string directoryPath, CancellationToken cancellationToken = default);
}
