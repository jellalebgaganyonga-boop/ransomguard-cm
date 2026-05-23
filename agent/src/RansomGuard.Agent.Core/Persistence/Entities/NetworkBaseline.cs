namespace RansomGuard.Agent.Core.Persistence.Entities;

/// <summary>
/// Persisted adaptive baseline for network activity detection.
/// Tracks 3-phase lifecycle: Learning -> ActiveDetection -> DriftDetected.
/// </summary>
public sealed class NetworkBaseline
{
    /// <summary>Unique baseline identifier.</summary>
    public required Guid Id { get; init; }

    /// <summary>Scope of this baseline ("global" or process name).</summary>
    public required string Scope { get; init; }

    /// <summary>Current lifecycle phase.</summary>
    public required BaselinePhase Phase { get; set; }

    /// <summary>UTC timestamp when learning phase started.</summary>
    public required DateTime LearningStartedAt { get; init; }

    /// <summary>UTC timestamp when learning completed (null if still learning).</summary>
    public DateTime? LearningCompletedAt { get; set; }

    /// <summary>UTC timestamp when drift was detected (null if no drift).</summary>
    public DateTime? DriftDetectedAt { get; set; }

    /// <summary>Total observation count across all metrics.</summary>
    public required int ObservationCount { get; set; }

    /// <summary>Confidence score: log10(ObservationCount) clamped 0-1.</summary>
    public required double ConfidenceScore { get; set; }

    /// <summary>UTC timestamp of last update.</summary>
    public required DateTime LastUpdatedAt { get; set; }
}

/// <summary>
/// Lifecycle phases for adaptive network baseline.
/// </summary>
public enum BaselinePhase
{
    /// <summary>First 7 days — collecting data, no alerts.</summary>
    Learning,

    /// <summary>After learning — baseline locked, detection active.</summary>
    ActiveDetection,

    /// <summary>Drift > 50% detected — suspected baseline poisoning.</summary>
    DriftDetected
}
