using Microsoft.EntityFrameworkCore;
using RansomGuard.Agent.Core.Persistence.Entities;

namespace RansomGuard.Agent.Core.Persistence.Repositories;

/// <summary>
/// SQLite-backed repository for <see cref="EntropyBaseline"/> entities.
/// </summary>
public sealed class EntropyBaselineRepository : IEntropyBaselineRepository
{
    private readonly AgentDbContext _context;

    /// <summary>
    /// Initializes the repository.
    /// </summary>
    public EntropyBaselineRepository(AgentDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc />
    public async Task<EntropyBaseline?> GetByPathAsync(string filePath, CancellationToken cancellationToken = default)
    {
        return await _context.EntropyBaselines
            .FirstOrDefaultAsync(b => b.FilePath == filePath, cancellationToken);
    }

    /// <inheritdoc />
    public async Task UpsertAsync(EntropyBaseline baseline, CancellationToken cancellationToken = default)
    {
        EntropyBaseline? existing = await _context.EntropyBaselines
            .FirstOrDefaultAsync(b => b.FilePath == baseline.FilePath, cancellationToken);

        if (existing is not null)
        {
            _context.Entry(existing).CurrentValues.SetValues(baseline);
        }
        else
        {
            _context.EntropyBaselines.Add(baseline);
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<double?> GetDirectoryAverageAsync(string directoryPath, CancellationToken cancellationToken = default)
    {
        bool any = await _context.EntropyBaselines
            .AnyAsync(b => b.DirectoryPath == directoryPath, cancellationToken);

        if (!any) return null;

        return await _context.EntropyBaselines
            .Where(b => b.DirectoryPath == directoryPath)
            .AverageAsync(b => b.EntropyValue, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<EntropyBaseline>> GetByDirectoryAsync(string directoryPath, CancellationToken cancellationToken = default)
    {
        return await _context.EntropyBaselines
            .Where(b => b.DirectoryPath == directoryPath)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<int> CountAsync(CancellationToken cancellationToken = default)
    {
        return await _context.EntropyBaselines.CountAsync(cancellationToken);
    }
}
