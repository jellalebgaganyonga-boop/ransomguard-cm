namespace RansomGuard.Agent.Core.Persistence.Entities;

/// <summary>
/// Represents a high-confidence security alert generated from detection events.
/// </summary>
public sealed class Alert
{
    /// <summary>
    /// Unique alert identifier.
    /// </summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>
    /// Alert severity (Low, Medium, High, Critical).
    /// </summary>
    public required string Severity { get; init; }

    /// <summary>
    /// Human-readable alert title.
    /// </summary>
    public required string Title { get; init; }

    /// <summary>
    /// Detailed description of the alert.
    /// </summary>
    public required string Description { get; init; }

    /// <summary>
    /// The detection event that triggered this alert, if applicable.
    /// </summary>
    public Guid? SourceEventId { get; init; }

    /// <summary>
    /// Whether the alert has been acknowledged by a user.
    /// </summary>
    public bool Acknowledged { get; set; }

    /// <summary>
    /// UTC timestamp when the alert was generated.
    /// </summary>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// UTC timestamp when the record was created.
    /// </summary>
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// UTC timestamp when the record was last updated.
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
