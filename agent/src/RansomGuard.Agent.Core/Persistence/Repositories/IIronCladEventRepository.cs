using RansomGuard.Agent.Core.Persistence.Entities;

namespace RansomGuard.Agent.Core.Persistence.Repositories;

/// <summary>
/// Repository for IronClad command events.
/// </summary>
public interface IIronCladEventRepository
{
    /// <summary>Persists a new IronClad event.</summary>
    Task AppendAsync(IronCladEvent evt, CancellationToken cancellationToken = default);

    /// <summary>Gets the most recent events, ordered by IssuedAt descending.</summary>
    Task<IReadOnlyList<IronCladEvent>> GetRecentAsync(int count = 20, CancellationToken cancellationToken = default);

    /// <summary>Gets events triggered by a specific alert.</summary>
    Task<IReadOnlyList<IronCladEvent>> GetByAlertIdAsync(Guid alertId, CancellationToken cancellationToken = default);
}
