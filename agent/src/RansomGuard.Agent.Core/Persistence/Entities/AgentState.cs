namespace RansomGuard.Agent.Core.Persistence.Entities;

/// <summary>
/// Tracks agent lifecycle state (running, stopped, etc.) with metadata.
/// </summary>
public sealed class AgentState
{
    /// <summary>
    /// Unique state record identifier.
    /// </summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>
    /// Current state (Starting, Running, Stopping, Stopped, Error).
    /// </summary>
    public required string State { get; set; }

    /// <summary>
    /// Agent version at the time of this state change.
    /// </summary>
    public required string AgentVersion { get; init; }

    /// <summary>
    /// Machine hostname.
    /// </summary>
    public required string Hostname { get; init; }

    /// <summary>
    /// Additional state metadata (JSON).
    /// </summary>
    public string? Metadata { get; set; }

    /// <summary>
    /// UTC timestamp when the record was created.
    /// </summary>
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// UTC timestamp when the record was last updated.
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
