using RansomGuard.Agent.Core.Detection.Genealogy;
using RansomGuard.Agent.Core.Persistence.Entities;

namespace RansomGuard.Agent.Core.Detection.IndicatorRemoval;

/// <summary>
/// Detects indicator removal activity from a process snapshot.
/// Each detector maps to a specific MITRE ATT&amp;CK technique.
/// </summary>
public interface IIndicatorRemovalDetector
{
    /// <summary>Detector name for logging.</summary>
    string DetectorName { get; }

    /// <summary>MITRE ATT&amp;CK technique ID.</summary>
    string MitreTechniqueId { get; }

    /// <summary>
    /// Evaluates a process snapshot for indicator removal activity.
    /// Returns a detection event if suspicious, null otherwise.
    /// </summary>
    IndicatorRemovalEvent? Evaluate(ProcessSnapshot process);
}
