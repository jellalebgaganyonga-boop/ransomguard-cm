using Microsoft.EntityFrameworkCore;
using RansomGuard.Agent.Core.Persistence.Entities;
using RansomGuard.Agent.Core.Security.Cryptography;

namespace RansomGuard.Agent.Core.Persistence.Repositories;

/// <summary>
/// SQLite-backed repository for the immutable, hash-chained, and Ed25519-signed <see cref="AuditLog"/>.
/// </summary>
public sealed class AuditLogRepository : IAuditLogRepository
{
    private readonly AgentDbContext _context;
    private readonly AuditLogSigner? _signer;

    /// <summary>
    /// Initializes a new instance of <see cref="AuditLogRepository"/>.
    /// </summary>
    /// <param name="context">Database context.</param>
    /// <param name="signer">Optional Ed25519 signer. If null, entries are created without signatures.</param>
    public AuditLogRepository(AgentDbContext context, AuditLogSigner? signer = null)
    {
        _context = context;
        _signer = signer;
    }

    /// <inheritdoc />
    public async Task AppendAsync(string action, string details, string? entityType = null, Guid? entityId = null, CancellationToken cancellationToken = default)
    {
        AuditLog? latest = await GetLatestAsync(cancellationToken);
        string? previousHash = latest?.CurrentHash;

        DateTime timestamp = DateTime.UtcNow;
        string currentHash = AuditLog.ComputeHash(action, details, timestamp, previousHash);

        Guid entryId = Guid.NewGuid();
        string? signature = null;
        if (OperatingSystem.IsWindows() && _signer is not null)
        {
            signature = _signer.Sign(entryId, timestamp, action, previousHash, currentHash);
        }

        var entry = new AuditLog
        {
            Id = entryId,
            Action = action,
            Details = details,
            EntityType = entityType,
            EntityId = entityId,
            PreviousHash = previousHash,
            CurrentHash = currentHash,
            Signature = signature,
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

        if (entries[0].PreviousHash is not null)
        {
            return false;
        }

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

            // Verify Ed25519 signature if present
            if (entry.Signature is not null && _signer is not null && OperatingSystem.IsWindows())
            {
                bool sigValid = _signer.Verify(
                    entry.Id, entry.CreatedAt, entry.Action,
                    entry.PreviousHash, entry.CurrentHash, entry.Signature);

                if (!sigValid)
                {
                    return false;
                }
            }

            expectedPreviousHash = entry.CurrentHash;
        }

        return true;
    }
}
