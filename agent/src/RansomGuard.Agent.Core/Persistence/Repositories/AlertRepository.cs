using Microsoft.EntityFrameworkCore;
using RansomGuard.Agent.Core.Persistence.Entities;

namespace RansomGuard.Agent.Core.Persistence.Repositories;

/// <summary>
/// SQLite-backed repository for <see cref="Alert"/> entities.
/// </summary>
public sealed class AlertRepository : IAlertRepository
{
    private readonly AgentDbContext _context;

    /// <summary>
    /// Initializes a new instance of <see cref="AlertRepository"/>.
    /// </summary>
    public AlertRepository(AgentDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc />
    public async Task AddAsync(Alert alert, CancellationToken cancellationToken = default)
    {
        _context.Alerts.Add(alert);
        await _context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Alert?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.Alerts
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Alert>> GetUnacknowledgedAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Alerts
            .AsNoTracking()
            .Where(a => !a.Acknowledged)
            .OrderByDescending(a => a.Severity)
            .ThenByDescending(a => a.Timestamp)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task AcknowledgeAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await _context.Alerts
            .Where(a => a.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.Acknowledged, true)
                .SetProperty(a => a.UpdatedAt, DateTime.UtcNow),
                cancellationToken);
    }
}
