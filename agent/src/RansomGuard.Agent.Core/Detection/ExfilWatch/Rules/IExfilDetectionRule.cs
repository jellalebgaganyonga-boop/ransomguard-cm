using RansomGuard.Agent.Core.Detection.ExfilWatch.Models;

namespace RansomGuard.Agent.Core.Detection.ExfilWatch.Rules;

/// <summary>
/// Interface for EXFIL WATCH detection rules.
/// Each rule evaluates a network event against the data volume tracker
/// and returns findings when suspicious patterns are detected.
/// </summary>
public interface IExfilDetectionRule
{
    /// <summary>Rule name for logging and alert attribution.</summary>
    string RuleName { get; }

    /// <summary>
    /// Evaluates a network event for suspicious exfiltration patterns.
    /// </summary>
    /// <param name="networkEvent">The event to evaluate.</param>
    /// <param name="tracker">Data volume tracker for window queries.</param>
    /// <param name="options">ExfilWatch configuration.</param>
    /// <returns>Finding if suspicious, null if clean.</returns>
    ExfilFinding? Evaluate(NetworkEvent networkEvent, IDataVolumeTracker tracker,
        Core.Configuration.ExfilWatchOptions options);
}

/// <summary>
/// Finding from an exfiltration detection rule.
/// </summary>
public sealed record ExfilFinding
{
    /// <summary>Rule that produced this finding.</summary>
    public required string RuleName { get; init; }

    /// <summary>Severity: Low, Medium, High, Critical.</summary>
    public required ExfilSeverity Severity { get; init; }

    /// <summary>Human-readable description.</summary>
    public required string Description { get; init; }

    /// <summary>Process ID.</summary>
    public required int ProcessId { get; init; }

    /// <summary>Process name.</summary>
    public required string ProcessName { get; init; }

    /// <summary>Destination address or domain.</summary>
    public required string Destination { get; init; }

    /// <summary>Destination port.</summary>
    public int? DestinationPort { get; init; }

    /// <summary>Bytes transferred in the detection window.</summary>
    public required long BytesTransferred { get; init; }

    /// <summary>MITRE ATT&amp;CK technique ID.</summary>
    public required string MitreId { get; init; }

    /// <summary>UTC timestamp when the finding was created (detection time).</summary>
    public DateTime DetectedAt { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// Severity levels for exfiltration findings.
/// </summary>
public enum ExfilSeverity
{
    /// <summary>Informational — logged but no action.</summary>
    Low,
    /// <summary>Suspicious — warrants review.</summary>
    Medium,
    /// <summary>Likely exfiltration — alert + throttle.</summary>
    High,
    /// <summary>Active exfiltration — alert + block.</summary>
    Critical
}
