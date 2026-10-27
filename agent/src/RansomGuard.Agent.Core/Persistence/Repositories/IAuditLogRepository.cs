using RansomGuard.Agent.Core.Persistence.Entities;

namespace RansomGuard.Agent.Core.Persistence.Repositories;

/// <summary>
/// Repository for managing the immutable, hash-chained <see cref="AuditLog"/>.
/// </summary>
public interface IAuditLogRepository
{
    /// <summary>
    /// Records an audit entry: one INSERT into the waiting table, durable when this returns.
    /// It is chained (hashed, signed, ordered) afterwards, by the chain writer.
    /// </summary>
    /// <remarks>
    /// Never makes the caller wait for the chain, and never throws when the entry cannot be
    /// recorded (disk full, database locked): that is logged Critical and counted in
    /// <see cref="AuditHealth"/>, and the detection or action that called goes on (AGT-AUD-002).
    /// Without a chain writer (tools, tests) the entries are chained before this returns.
    /// </remarks>
    Task AppendAsync(string action, string details, string? entityType = null, Guid? entityId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Chains every waiting entry, in insertion order, by batches of one transaction each.
    /// Returns the number of entries chained.
    /// </summary>
    Task<int> ChainPendingAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves the most recent audit log entry.
    /// </summary>
    Task<AuditLog?> GetLatestAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves audit log entries within a time range.
    /// </summary>
    Task<IReadOnlyList<AuditLog>> GetByTimeRangeAsync(DateTime from, DateTime to, CancellationToken cancellationToken = default);

    /// <summary>
    /// The chained entries this workstation has not handed to the GRID yet, oldest first.
    /// </summary>
    /// <remarks>
    /// In sequence order, never by time: the GRID refuses a gap, and two entries can share a
    /// clock tick while a clock that is put right jumps over a whole afternoon.
    /// </remarks>
    Task<IReadOnlyList<AuditLog>> GetPendingUploadAsync(int max, CancellationToken cancellationToken = default);

    /// <summary>Marks entries as handed over, after the GRID has accepted them.</summary>
    Task MarkUploadedAsync(IReadOnlyCollection<Guid> ids, DateTime uploadedAt, CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifies the integrity of the audit log hash chain.
    /// Returns true if the chain is intact, false if tampered.
    /// </summary>
    Task<bool> VerifyChainIntegrityAsync(CancellationToken cancellationToken = default);
}
