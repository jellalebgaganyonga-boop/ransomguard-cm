namespace RansomGuard.Agent.Core.Persistence.Entities;

/// <summary>
/// An audit entry recorded but not chained yet (AGT-AUD-002).
/// </summary>
/// <remarks>
/// Recording an entry is a single INSERT here, durable as soon as it returns: a detection or
/// an action never waits for the signed chain. The chain writer moves these rows into
/// <see cref="AuditLog"/>, in <see cref="Id"/> order, in batches; a crash between the two
/// steps loses nothing, the next start chains what is left first.
/// </remarks>
public sealed class PendingAuditEntry
{
    /// <summary>Insertion order: the order of the chain. Assigned by the database.</summary>
    public long Id { get; init; }

    /// <summary>The action, as it will appear in the chain.</summary>
    public required string Action { get; init; }

    /// <summary>The details, as they will appear in the chain.</summary>
    public required string Details { get; init; }

    /// <summary>Optional entity type.</summary>
    public string? EntityType { get; init; }

    /// <summary>Optional entity id.</summary>
    public Guid? EntityId { get; init; }

    /// <summary>
    /// When the event was recorded, UTC. Becomes the chained entry's timestamp (hashed and
    /// signed), so a late chaining does not move the event in time.
    /// </summary>
    public DateTime CreatedAt { get; init; }
}
