using RansomGuard.Agent.Core.Persistence.Entities;

namespace RansomGuard.Agent.Core.Persistence.Repositories;

/// <summary>
/// Repository for managing <see cref="SentinelCanary"/> persistence operations.
/// </summary>
public interface ISentinelCanaryRepository
{
    /// <summary>
    /// Adds a new canary record.
    /// </summary>
    Task AddAsync(SentinelCanary canary, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a canary by its unique identifier.
    /// </summary>
    Task<SentinelCanary?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a canary by its file path.
    /// </summary>
    Task<SentinelCanary?> GetByFilePathAsync(string filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves all active canaries.
    /// </summary>
    Task<IReadOnlyList<SentinelCanary>> GetActiveAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves all active canaries in a specific directory.
    /// </summary>
    Task<IReadOnlyList<SentinelCanary>> GetActiveByDirectoryAsync(string directory, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing canary record.
    /// </summary>
    Task UpdateAsync(SentinelCanary canary, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a new canary alert.
    /// </summary>
    Task AddAlertAsync(CanaryAlert alert, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves all canary alerts ordered by detection time descending.
    /// </summary>
    Task<IReadOnlyList<CanaryAlert>> GetAlertsAsync(CancellationToken cancellationToken = default);
}
