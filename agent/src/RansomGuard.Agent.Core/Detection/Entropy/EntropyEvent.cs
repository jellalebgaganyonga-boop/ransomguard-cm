namespace RansomGuard.Agent.Core.Detection.Entropy;

/// <summary>
/// Represents a file change event queued for entropy analysis.
/// </summary>
public sealed record EntropyEvent
{
    /// <summary>Full path of the changed file.</summary>
    public required string FilePath { get; init; }

    /// <summary>UTC timestamp when the event was observed.</summary>
    public required DateTime ObservedAt { get; init; }
}
