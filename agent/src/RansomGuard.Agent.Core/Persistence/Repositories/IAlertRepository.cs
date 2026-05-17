using RansomGuard.Agent.Core.Persistence.Entities;

namespace RansomGuard.Agent.Core.Persistence.Repositories;

/// <summary>
/// Repository for managing <see cref="Alert"/> persistence operations.
/// </summary>
public interface IAlertRepository
{
    /// <summary>
    /// Adds a new alert to the database.
    /// </summary>
    Task AddAsync(Alert alert, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves an alert by its unique identifier.
    /// </summary>
    Task<Alert?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves unacknowledged alerts, ordered by severity descending then timestamp.
    /// </summary>
    Task<IReadOnlyList<Alert>> GetUnacknowledgedAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks an alert as acknowledged.
    /// </summary>
    Task AcknowledgeAsync(Guid id, CancellationToken cancellationToken = default);
}
