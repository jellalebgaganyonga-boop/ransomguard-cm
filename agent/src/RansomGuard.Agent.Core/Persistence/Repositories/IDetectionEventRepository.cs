using RansomGuard.Agent.Core.Persistence.Entities;

namespace RansomGuard.Agent.Core.Persistence.Repositories;

/// <summary>
/// Repository for managing <see cref="DetectionEvent"/> persistence operations.
/// </summary>
public interface IDetectionEventRepository
{
    /// <summary>
    /// Adds a new detection event to the database.
    /// </summary>
    Task AddAsync(DetectionEvent detectionEvent, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a detection event by its unique identifier.
    /// </summary>
    Task<DetectionEvent?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves detection events within a time range, ordered by timestamp descending.
    /// </summary>
    Task<IReadOnlyList<DetectionEvent>> GetByTimeRangeAsync(DateTime from, DateTime to, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the total count of detection events.
    /// </summary>
    Task<int> CountAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes events older than the specified date for retention policy.
    /// </summary>
    Task<int> DeleteOlderThanAsync(DateTime cutoff, CancellationToken cancellationToken = default);
}
