using Microsoft.EntityFrameworkCore;
using RansomGuard.Agent.Core.Persistence.Entities;

namespace RansomGuard.Agent.Core.Persistence.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IIronCladEventRepository"/>.
/// </summary>
public sealed class IronCladEventRepository : IIronCladEventRepository
{
    private readonly AgentDbContext _db;

    /// <summary>Initializes with database context.</summary>
    public IronCladEventRepository(AgentDbContext db) => _db = db;

    /// <inheritdoc />
    public async Task AppendAsync(IronCladEvent evt, CancellationToken cancellationToken = default)
    {
        _db.IronCladEvents.Add(evt);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<IronCladEvent>> GetRecentAsync(int count = 20, CancellationToken cancellationToken = default)
    {
        return await _db.IronCladEvents
            .OrderByDescending(e => e.IssuedAt)
            .Take(count)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<IronCladEvent>> GetByAlertIdAsync(Guid alertId, CancellationToken cancellationToken = default)
    {
        return await _db.IronCladEvents
            .Where(e => e.SourceAlertId == alertId)
            .OrderByDescending(e => e.IssuedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
