using Microsoft.EntityFrameworkCore;
using RansomGuard.Agent.Core.Persistence.Entities;

namespace RansomGuard.Agent.Core.Persistence.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IIronCladDeviceStateRepository"/>.
/// </summary>
public sealed class IronCladDeviceStateRepository : IIronCladDeviceStateRepository
{
    private readonly AgentDbContext _db;

    /// <summary>Initializes with database context.</summary>
    public IronCladDeviceStateRepository(AgentDbContext db) => _db = db;

    /// <inheritdoc />
    public async Task<IReadOnlyList<IronCladDeviceState>> GetCurrentStatesAsync(CancellationToken cancellationToken = default)
    {
        return await _db.IronCladDeviceStates
            .OrderBy(s => s.PortNumber)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task UpdateStateAsync(IronCladDeviceState state, CancellationToken cancellationToken = default)
    {
        var existing = await _db.IronCladDeviceStates
            .FirstOrDefaultAsync(s => s.PortNumber == state.PortNumber, cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            existing.State = state.State;
            existing.LastChangedAt = state.LastChangedAt;
            existing.LastEventId = state.LastEventId;
            existing.Reason = state.Reason;
        }
        else
        {
            _db.IronCladDeviceStates.Add(state);
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
