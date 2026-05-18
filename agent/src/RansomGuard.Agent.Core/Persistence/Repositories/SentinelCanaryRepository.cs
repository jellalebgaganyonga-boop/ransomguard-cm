using Microsoft.EntityFrameworkCore;
using RansomGuard.Agent.Core.Persistence.Entities;

namespace RansomGuard.Agent.Core.Persistence.Repositories;

/// <summary>
/// SQLite-backed repository for <see cref="SentinelCanary"/> and <see cref="CanaryAlert"/> entities.
/// </summary>
public sealed class SentinelCanaryRepository : ISentinelCanaryRepository
{
    private readonly AgentDbContext _context;

    /// <summary>
    /// Initializes a new instance of <see cref="SentinelCanaryRepository"/>.
    /// </summary>
    public SentinelCanaryRepository(AgentDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc />
    public async Task AddAsync(SentinelCanary canary, CancellationToken cancellationToken = default)
    {
        _context.SentinelCanaries.Add(canary);
        await _context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<SentinelCanary?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.SentinelCanaries
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<SentinelCanary?> GetByFilePathAsync(string filePath, CancellationToken cancellationToken = default)
    {
        return await _context.SentinelCanaries
            .FirstOrDefaultAsync(c => c.FilePath == filePath, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SentinelCanary>> GetActiveAsync(CancellationToken cancellationToken = default)
    {
        return await _context.SentinelCanaries
            .Where(c => c.Status == CanaryStatus.Active)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SentinelCanary>> GetActiveByDirectoryAsync(string directory, CancellationToken cancellationToken = default)
    {
        return await _context.SentinelCanaries
            .Where(c => c.Directory == directory && c.Status == CanaryStatus.Active)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task UpdateAsync(SentinelCanary canary, CancellationToken cancellationToken = default)
    {
        canary.UpdatedAt = DateTime.UtcNow;
        _context.SentinelCanaries.Update(canary);
        await _context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task AddAlertAsync(CanaryAlert alert, CancellationToken cancellationToken = default)
    {
        _context.CanaryAlerts.Add(alert);
        await _context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CanaryAlert>> GetAlertsAsync(CancellationToken cancellationToken = default)
    {
        return await _context.CanaryAlerts
            .AsNoTracking()
            .OrderByDescending(a => a.DetectedAt)
            .ToListAsync(cancellationToken);
    }
}
