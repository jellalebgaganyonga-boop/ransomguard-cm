using RansomGuard.Agent.Core.Persistence.Entities;

namespace RansomGuard.Agent.Core.Persistence.Repositories;

/// <summary>
/// Repository for IronClad device relay state tracking.
/// </summary>
public interface IIronCladDeviceStateRepository
{
    /// <summary>Gets the current state of all ports.</summary>
    Task<IReadOnlyList<IronCladDeviceState>> GetCurrentStatesAsync(CancellationToken cancellationToken = default);

    /// <summary>Updates (upserts) the state for a specific port.</summary>
    Task UpdateStateAsync(IronCladDeviceState state, CancellationToken cancellationToken = default);
}
