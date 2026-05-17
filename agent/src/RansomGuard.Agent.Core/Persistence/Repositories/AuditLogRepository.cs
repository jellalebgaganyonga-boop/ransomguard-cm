using Microsoft.EntityFrameworkCore;
using RansomGuard.Agent.Core.Persistence.Entities;

namespace RansomGuard.Agent.Core.Persistence.Repositories;

/// <summary>
/// SQLite-backed repository for the immutable, hash-chained <see cref="AuditLog"/>.
/// </summary>
public sealed class AuditLogRepository : IAuditLogRepository
{
    private readonly AgentDbContext _context;

    /// <summary>
    /// Initializes a new instance of <see cref="AuditLogRepository"/>.
    /// </summary>
    public AuditLogRepository(AgentDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc />
    public async Task AppendAsync(string action, string details, string? entityType = null, Guid? entityId = null, CancellationToken cancellationToken = default)
    {
        AuditLog? latest = await GetLatestAsync(cancellationToken);
        string? previousHash = latest?.CurrentHash;

        DateTime timestamp = DateTime.UtcNow;
        string currentHash = AuditLog.ComputeHash(action, details, timestamp, previousHash);

        var entry = new AuditLog
        {
            Action = action,
            Details = details,
            EntityType = entityType,
            EntityId = entityId,
            PreviousHash = previousHash,
            CurrentHash = currentHash,
            CreatedAt = timestamp,
            UpdatedAt = timestamp
        };

        _context.AuditLogs.Add(entry);
        await _context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<AuditLog?> GetLatestAsync(CancellationToken cancellationToken = default)
    {
        return await _context.AuditLogs
            .AsNoTracking()
            .OrderByDescending(a => a.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AuditLog>> GetByTimeRangeAsync(DateTime from, DateTime to, CancellationToken cancellationToken = default)
    {
        return await _context.AuditLogs
            .AsNoTracking()
            .Where(a => a.CreatedAt >= from && a.CreatedAt <= to)
            .OrderBy(a => a.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> VerifyChainIntegrityAsync(CancellationToken cancellationToken = default)
    {
        List<AuditLog> entries = await _context.AuditLogs
            .AsNoTracking()
            .OrderBy(a => a.CreatedAt)
            .ToListAsync(cancellationToken);

        if (entries.Count == 0)
        {
            return true;
        }

        // Verify first entry has no previous hash
        if (entries[0].PreviousHash is not null)
        {
            return false;
        }

        // Verify each entry's hash matches computed hash
        string? expectedPreviousHash = null;
        foreach (AuditLog entry in entries)
        {
            if (entry.PreviousHash != expectedPreviousHash)
            {
                return false;
            }

            string computedHash = AuditLog.ComputeHash(
                entry.Action, entry.Details, entry.CreatedAt, entry.PreviousHash);

            if (entry.CurrentHash != computedHash)
            {
                return false;
            }

            expectedPreviousHash = entry.CurrentHash;
        }

        return true;
    }
}
