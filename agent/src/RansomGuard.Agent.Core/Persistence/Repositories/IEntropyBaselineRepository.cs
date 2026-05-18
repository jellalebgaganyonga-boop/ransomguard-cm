using RansomGuard.Agent.Core.Persistence.Entities;

namespace RansomGuard.Agent.Core.Persistence.Repositories;

/// <summary>
/// Repository for entropy baseline persistence operations.
/// </summary>
public interface IEntropyBaselineRepository
{
    /// <summary>
    /// Gets a baseline by exact file path.
    /// </summary>
    Task<EntropyBaseline?> GetByPathAsync(string filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Inserts or updates a baseline (upsert by FilePath).
    /// </summary>
    Task UpsertAsync(EntropyBaseline baseline, CancellationToken cancellationToken = default);

    /// <summary>
    /// Computes the average entropy for all files in a directory.
    /// </summary>
    Task<double?> GetDirectoryAverageAsync(string directoryPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all baselines in a directory.
    /// </summary>
    Task<IReadOnlyList<EntropyBaseline>> GetByDirectoryAsync(string directoryPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the total number of baselined files.
    /// </summary>
    Task<int> CountAsync(CancellationToken cancellationToken = default);
}
