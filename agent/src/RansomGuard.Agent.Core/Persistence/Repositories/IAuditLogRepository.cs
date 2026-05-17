using RansomGuard.Agent.Core.Persistence.Entities;

namespace RansomGuard.Agent.Core.Persistence.Repositories;

/// <summary>
/// Repository for managing the immutable, hash-chained <see cref="AuditLog"/>.
/// </summary>
public interface IAuditLogRepository
{
    /// <summary>
    /// Appends a new audit log entry with hash chain integrity.
    /// </summary>
    Task AppendAsync(string action, string details, string? entityType = null, Guid? entityId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves the most recent audit log entry.
    /// </summary>
    Task<AuditLog?> GetLatestAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves audit log entries within a time range.
    /// </summary>
    Task<IReadOnlyList<AuditLog>> GetByTimeRangeAsync(DateTime from, DateTime to, CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifies the integrity of the audit log hash chain.
    /// Returns true if the chain is intact, false if tampered.
    /// </summary>
    Task<bool> VerifyChainIntegrityAsync(CancellationToken cancellationToken = default);
}
