using Microsoft.EntityFrameworkCore;
using RansomGuard.Agent.Core.Persistence.Entities;

namespace RansomGuard.Agent.Core.Persistence.Repositories;

/// <summary>
/// SQLite-backed repository for <see cref="DetectionEvent"/> entities.
/// </summary>
public sealed class DetectionEventRepository : IDetectionEventRepository
{
    private readonly AgentDbContext _context;

    /// <summary>
    /// Initializes a new instance of <see cref="DetectionEventRepository"/>.
    /// </summary>
    public DetectionEventRepository(AgentDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc />
    public async Task AddAsync(DetectionEvent detectionEvent, CancellationToken cancellationToken = default)
    {
        _context.DetectionEvents.Add(detectionEvent);
        await _context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<DetectionEvent?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.DetectionEvents
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DetectionEvent>> GetByTimeRangeAsync(DateTime from, DateTime to, CancellationToken cancellationToken = default)
    {
        return await _context.DetectionEvents
            .AsNoTracking()
            .Where(e => e.Timestamp >= from && e.Timestamp <= to)
            .OrderByDescending(e => e.Timestamp)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<int> CountAsync(CancellationToken cancellationToken = default)
    {
        return await _context.DetectionEvents.CountAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<int> DeleteOlderThanAsync(DateTime cutoff, CancellationToken cancellationToken = default)
    {
        return await _context.DetectionEvents
            .Where(e => e.CreatedAt < cutoff)
            .ExecuteDeleteAsync(cancellationToken);
    }
}
