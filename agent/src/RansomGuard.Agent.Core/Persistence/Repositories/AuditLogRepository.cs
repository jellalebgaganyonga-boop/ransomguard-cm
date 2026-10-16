using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RansomGuard.Agent.Core.Persistence.Entities;
using RansomGuard.Agent.Core.Security.Cryptography;

namespace RansomGuard.Agent.Core.Persistence.Repositories;

/// <summary>
/// SQLite-backed repository for the immutable, hash-chained, and Ed25519-signed <see cref="AuditLog"/>.
/// </summary>
/// <remarks>
/// Recording and chaining are two steps (AGT-AUD-002):
/// <list type="bullet">
/// <item><see cref="AppendAsync"/> inserts into <see cref="AgentDbContext.PendingAuditEntries"/>:
///   one INSERT, durable at once, no wait on the chain. A detection or an action is never held
///   up by the audit, and a crash after it loses nothing.</item>
/// <item><see cref="ChainPendingAsync"/> moves the waiting entries into the chain, in insertion
///   order, by batches of <see cref="ChainBatchSize"/> in one transaction (the links and the
///   removal of the waiting rows commit together). One writer per chain: the gate is per
///   database, so unrelated databases never wait for each other. The order is
///   <see cref="AuditLog.Sequence"/>, unique in the database: a writer in another process (a CLI
///   sub-command while the service runs) gets a constraint error, rolls back and retries on the
///   new tail.</item>
/// </list>
/// </remarks>
public sealed class AuditLogRepository : IAuditLogRepository
{
    /// <summary>Entries chained per transaction.</summary>
    public const int ChainBatchSize = 500;

    private const int MaxChainAttempts = 5;
    private const int SqliteConstraintError = 19;

    private readonly AgentDbContext _context;
    private readonly AuditLogSigner? _signer;
    private readonly IAuditChainWriter? _chainWriter;
    private readonly AuditHealth? _health;
    private readonly ILogger _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="AuditLogRepository"/>.
    /// </summary>
    /// <param name="context">Database context.</param>
    /// <param name="signer">Optional Ed25519 signer. If null, entries are created without signatures.</param>
    /// <param name="chainWriter">
    /// The background chain writer. Null (tools, tests): <see cref="AppendAsync"/> chains before it returns.
    /// </param>
    /// <param name="health">Counts the entries that could not be recorded, for the heartbeat.</param>
    /// <param name="logger">Logger for recording failures.</param>
    public AuditLogRepository(
        AgentDbContext context,
        AuditLogSigner? signer = null,
        IAuditChainWriter? chainWriter = null,
        AuditHealth? health = null,
        ILogger<AuditLogRepository>? logger = null)
    {
        _context = context;
        _signer = signer;
        _chainWriter = chainWriter;
        _health = health;
        _logger = logger ?? NullLogger<AuditLogRepository>.Instance;
    }

    /// <inheritdoc />
    public async Task AppendAsync(string action, string details, string? entityType = null, Guid? entityId = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var entry = new PendingAuditEntry
        {
            Action = action,
            Details = details,
            EntityType = entityType,
            EntityId = entityId,
            CreatedAt = DateTime.UtcNow,
        };
        _context.PendingAuditEntries.Add(entry);
        try
        {
            // Not cancellable half-way: once a caller records an event, it is recorded.
            await _context.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Nothing is rethrown: the detection or the action that is recording must go on.
            // The hole in the trail is not silent: Critical log, and the heartbeat count.
            _context.Entry(entry).State = EntityState.Detached;
            _health?.RecordFailure();
            _logger.LogCritical(ex,
                "Audit entry {Action} could not be recorded — the detection or action goes on, this trace is lost",
                action);
            return;
        }
        finally
        {
            if (_context.Entry(entry).State != EntityState.Detached)
            {
                _context.Entry(entry).State = EntityState.Detached;
            }
        }

        if (_chainWriter is not null)
        {
            _chainWriter.RequestChaining();
        }
        else
        {
            await ChainPendingAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task<int> ChainPendingAsync(CancellationToken cancellationToken = default)
    {
        SemaphoreSlim gate = AuditChainGate.For(_context);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            int chained = 0;
            while (true)
            {
                int batch = await ChainOneBatchAsync(cancellationToken).ConfigureAwait(false);
                if (batch == 0)
                {
                    return chained;
                }

                chained += batch;
            }
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<int> ChainOneBatchAsync(CancellationToken cancellationToken)
    {
        for (int attempt = 1; ; attempt++)
        {
            List<PendingAuditEntry> waiting = await _context.PendingAuditEntries
                .AsNoTracking()
                .OrderBy(p => p.Id)
                .Take(ChainBatchSize)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            if (waiting.Count == 0)
            {
                return 0;
            }

            AuditLog? tail = await GetLatestAsync(cancellationToken).ConfigureAwait(false);
            string? previousHash = tail?.CurrentHash;
            long sequence = tail?.Sequence ?? 0;
            var links = new List<AuditLog>(waiting.Count);
            foreach (PendingAuditEntry entry in waiting)
            {
                AuditLog link = BuildLink(entry, previousHash, ++sequence);
                links.Add(link);
                previousHash = link.CurrentHash;
            }

            List<long> ids = waiting.Select(p => p.Id).ToList();
            await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            _context.AuditLogs.AddRange(links);
            try
            {
                await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                await _context.PendingAuditEntries
                    .Where(p => ids.Contains(p.Id))
                    .ExecuteDeleteAsync(cancellationToken)
                    .ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return waiting.Count;
            }
            catch (DbUpdateException ex) when (IsSequenceTaken(ex) && attempt < MaxChainAttempts)
            {
                // Another process chained first: nothing of this batch is kept; re-read and retry.
                await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                foreach (AuditLog link in links)
                {
                    _context.Entry(link).State = EntityState.Detached;
                }
            }
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

    /// <summary>
    /// The next link for a waiting entry. Its timestamp is the moment the event was recorded,
    /// not the moment it is chained: a late chaining does not move the event in time.
    /// </summary>
    private AuditLog BuildLink(PendingAuditEntry entry, string? previousHash, long sequence)
    {
        DateTime timestamp = DateTime.SpecifyKind(entry.CreatedAt, DateTimeKind.Utc);
        string currentHash = AuditLog.ComputeHash(entry.Action, entry.Details, timestamp, previousHash);

        Guid entryId = Guid.NewGuid();
        string? signature = null;
        if (OperatingSystem.IsWindows() && _signer is not null)
        {
            signature = _signer.Sign(entryId, timestamp, entry.Action, previousHash, currentHash);
        }

        return new AuditLog
        {
            Id = entryId,
            Sequence = sequence,
            Action = entry.Action,
            Details = entry.Details,
            EntityType = entry.EntityType,
            EntityId = entry.EntityId,
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
