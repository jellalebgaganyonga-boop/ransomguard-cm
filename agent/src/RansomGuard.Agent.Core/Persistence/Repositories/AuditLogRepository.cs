using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RansomGuard.Agent.Core.Persistence.Entities;
using RansomGuard.Agent.Core.Security.Cryptography;

namespace RansomGuard.Agent.Core.Persistence.Repositories;

/// <summary>
/// SQLite-backed repository for the immutable, hash-chained, and Ed25519-signed <see cref="AuditLog"/>.
/// </summary>
/// <remarks>
/// Appending reads the chain tail, then inserts the next link. Two concurrent appends would
/// both read the same tail and fork the chain, and the integrity check would then report a
/// tampering that never happened. Two guards prevent it:
/// <list type="bullet">
/// <item>one writer for the whole process: every append goes through <see cref="WriteGate"/>,
///   whatever the DbContext or the scope;</item>
/// <item>the chain order is <see cref="AuditLog.Sequence"/>, unique in the database: a writer in
///   another process (a CLI sub-command while the service runs) cannot take the same position;
///   it gets a constraint error, re-reads the tail and retries.</item>
/// </list>
/// </remarks>
public sealed class AuditLogRepository : IAuditLogRepository
{
    private const int MaxAppendAttempts = 5;
    private const int SqliteConstraintError = 19;

    /// <summary>Process-wide single writer of the audit chain.</summary>
    private static readonly SemaphoreSlim WriteGate = new(1, 1);

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
        await WriteGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            for (int attempt = 1; ; attempt++)
            {
                AuditLog entry = await BuildNextEntryAsync(action, details, entityType, entityId, cancellationToken)
                    .ConfigureAwait(false);
                _context.AuditLogs.Add(entry);
                try
                {
                    await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                    return;
                }
                catch (DbUpdateException ex) when (IsSequenceTaken(ex) && attempt < MaxAppendAttempts)
                {
                    // Another process appended first: drop this link and rebuild on the new tail.
                    _context.Entry(entry).State = EntityState.Detached;
                }
            }
        }
        finally
        {
            WriteGate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<AuditLog?> GetLatestAsync(CancellationToken cancellationToken = default)
    {
        return await _context.AuditLogs
            .AsNoTracking()
            .OrderByDescending(a => a.Sequence)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AuditLog>> GetByTimeRangeAsync(DateTime from, DateTime to, CancellationToken cancellationToken = default)
    {
        return await _context.AuditLogs
            .AsNoTracking()
            .Where(a => a.CreatedAt >= from && a.CreatedAt <= to)
            .OrderBy(a => a.Sequence)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> VerifyChainIntegrityAsync(CancellationToken cancellationToken = default)
    {
        List<AuditLog> entries = await _context.AuditLogs
            .AsNoTracking()
            .OrderBy(a => a.Sequence)
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

    private async Task<AuditLog> BuildNextEntryAsync(
        string action, string details, string? entityType, Guid? entityId, CancellationToken cancellationToken)
    {
        AuditLog? latest = await GetLatestAsync(cancellationToken).ConfigureAwait(false);
        string? previousHash = latest?.CurrentHash;

        DateTime timestamp = DateTime.UtcNow;
        string currentHash = AuditLog.ComputeHash(action, details, timestamp, previousHash);

        Guid entryId = Guid.NewGuid();
        string? signature = null;
        if (OperatingSystem.IsWindows() && _signer is not null)
        {
            signature = _signer.Sign(entryId, timestamp, action, previousHash, currentHash);
        }

        return new AuditLog
        {
            Id = entryId,
            Sequence = (latest?.Sequence ?? 0) + 1,
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
    }

    private static bool IsSequenceTaken(DbUpdateException ex) =>
        ex.InnerException is SqliteException { SqliteErrorCode: SqliteConstraintError };
}
