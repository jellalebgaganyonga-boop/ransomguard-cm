using System.Security.Cryptography;
using System.Text;

namespace RansomGuard.Agent.Core.Persistence.Entities;

/// <summary>
/// Immutable, hash-chained audit trail entry. Each row contains the hash of the previous row
/// to form a tamper-evident chain — any modification to historical entries breaks the chain.
/// </summary>
public sealed class AuditLog
{
    /// <summary>
    /// Unique audit entry identifier.
    /// </summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>
    /// Action performed (e.g., "AgentStarted", "FileDetected", "AlertCreated").
    /// </summary>
    public required string Action { get; init; }

    /// <summary>
    /// Detailed description of the action.
    /// </summary>
    public required string Details { get; init; }

    /// <summary>
    /// The entity type this action relates to (e.g., "DetectionEvent", "Alert").
    /// </summary>
    public string? EntityType { get; init; }

    /// <summary>
    /// The entity ID this action relates to.
    /// </summary>
    public Guid? EntityId { get; init; }

    /// <summary>
    /// SHA-256 hash of the previous audit log entry. Null for the first entry.
    /// </summary>
    public string? PreviousHash { get; init; }

    /// <summary>
    /// SHA-256 hash of this entry (computed from Action + Details + Timestamp + PreviousHash).
    /// </summary>
    public required string CurrentHash { get; init; }

    /// <summary>
    /// Ed25519 signature over the canonical payload (Base64-encoded, 88 chars).
    /// Null for entries created before Ed25519 signing was enabled.
    /// </summary>
    public string? Signature { get; init; }

    /// <summary>
    /// UTC timestamp when the record was created.
    /// </summary>
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// UTC timestamp when the record was last updated (same as CreatedAt for immutable entries).
    /// </summary>
    public DateTime UpdatedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Computes the SHA-256 hash for this audit entry given its content and the previous hash.
    /// </summary>
    /// <param name="action">The action performed.</param>
    /// <param name="details">The details of the action.</param>
    /// <param name="timestamp">The timestamp of the entry.</param>
    /// <param name="previousHash">The hash of the previous entry, or null for the first.</param>
    /// <returns>Hex-encoded SHA-256 hash.</returns>
    public static string ComputeHash(string action, string details, DateTime timestamp, string? previousHash)
    {
        string normalizedTimestamp = timestamp.ToString("yyyy-MM-dd HH:mm:ss.fffffff", System.Globalization.CultureInfo.InvariantCulture);
        string input = $"{action}|{details}|{normalizedTimestamp}|{previousHash ?? "GENESIS"}";
        byte[] hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }
}
