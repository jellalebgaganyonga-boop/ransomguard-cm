using RansomGuard.Agent.Core.Persistence.Entities;

namespace RansomGuard.Agent.Core.Detection.ExfilWatch;

/// <summary>
/// 3-phase adaptive baseline service for network activity.
/// Learning (7 days) -> ActiveDetection (locked) -> DriftDetected (poisoning alert).
/// </summary>
public interface INetworkBaselineService
{
    /// <summary>Gets or creates the baseline for the specified scope.</summary>
    Task<NetworkBaseline> GetOrCreateBaselineAsync(string scope, CancellationToken ct);

    /// <summary>Records a network observation and updates the appropriate metric.</summary>
    Task RecordObservationAsync(string dimension, BaselineMetricType type, long bytes, CancellationToken ct);

    /// <summary>Returns true if the specified scope is still in learning phase.</summary>
    Task<bool> IsLearningPhaseAsync(string scope, CancellationToken ct);

    /// <summary>Returns true if the dimension has been observed during learning.</summary>
    Task<bool> IsKnownDimensionAsync(string dimension, BaselineMetricType type, CancellationToken ct);

    /// <summary>Gets the baseline metric for a dimension, or null if not tracked.</summary>
    Task<NetworkBaselineMetric?> GetMetricAsync(string dimension, BaselineMetricType type, CancellationToken ct);

    /// <summary>Resets the specified scope back to Learning phase (admin only).</summary>
    Task ResetBaselineAsync(string scope, CancellationToken ct);
}
