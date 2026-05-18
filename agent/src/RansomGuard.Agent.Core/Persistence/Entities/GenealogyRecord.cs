namespace RansomGuard.Agent.Core.Persistence.Entities;

/// <summary>
/// Stores a serialized process tree and suspicious pattern analysis for an alert.
/// Links to the alert that triggered the genealogy capture.
/// </summary>
public sealed class GenealogyRecord
{
    /// <summary>Unique genealogy record identifier.</summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>The alert this genealogy enriches.</summary>
    public required Guid AlertId { get; init; }

    /// <summary>UTC timestamp when the genealogy was captured.</summary>
    public DateTime CapturedAt { get; init; } = DateTime.UtcNow;

    /// <summary>Serialized ProcessTree as JSON.</summary>
    public required string ProcessTreeJson { get; init; }

    /// <summary>Serialized list of SuspiciousPatternFlag as JSON.</summary>
    public required string SuspiciousPatternsJson { get; init; }

    /// <summary>Root process ID of the captured tree.</summary>
    public required int RootProcessId { get; init; }

    /// <summary>Root process name.</summary>
    public required string RootProcessName { get; init; }

    /// <summary>Human-readable summary of the process chain.</summary>
    public required string Summary { get; init; }
}
