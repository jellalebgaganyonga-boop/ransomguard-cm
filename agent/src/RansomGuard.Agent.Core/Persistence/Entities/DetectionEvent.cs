namespace RansomGuard.Agent.Core.Persistence.Entities;

/// <summary>
/// Represents a file system event detected by the monitoring engine.
/// </summary>
public sealed class DetectionEvent
{
    /// <summary>
    /// Unique event identifier.
    /// </summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>
    /// Type of file event (Created, Modified, Deleted, Renamed).
    /// </summary>
    public required string EventType { get; init; }

    /// <summary>
    /// Full path of the affected file.
    /// </summary>
    public required string FilePath { get; init; }

    /// <summary>
    /// Previous file path (for rename events only).
    /// </summary>
    public string? OldFilePath { get; init; }

    /// <summary>
    /// UTC timestamp when the event was detected.
    /// </summary>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// UTC timestamp when the record was created in the database.
    /// </summary>
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// UTC timestamp when the record was last updated.
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
