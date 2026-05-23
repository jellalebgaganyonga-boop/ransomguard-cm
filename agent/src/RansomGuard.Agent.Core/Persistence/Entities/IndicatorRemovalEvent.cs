namespace RansomGuard.Agent.Core.Persistence.Entities;

/// <summary>
/// Alert generated when indicator removal activity is detected.
/// Maps to MITRE ATT&amp;CK T1070 (Indicator Removal) and T1562 (Impair Defenses).
/// </summary>
public sealed class IndicatorRemovalEvent
{
    /// <summary>Unique event identifier.</summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>Type of indicator removal detected.</summary>
    public required IndicatorRemovalType EventType { get; init; }

    /// <summary>MITRE ATT&amp;CK technique ID.</summary>
    public required string MitreTechniqueId { get; init; }

    /// <summary>Process ID that performed the action.</summary>
    public required int ProcessId { get; init; }

    /// <summary>Process name at time of detection.</summary>
    public required string ProcessName { get; init; }

    /// <summary>Full command line captured via WMI.</summary>
    public required string CommandLine { get; init; }

    /// <summary>Target resource (log name, journal path, task name, registry key).</summary>
    public required string TargetResource { get; init; }

    /// <summary>Alert severity: Medium, High, Critical.</summary>
    public required string Severity { get; init; }

    /// <summary>Human-readable description of the detection.</summary>
    public required string Description { get; init; }

    /// <summary>Action taken: Alert, Block.</summary>
    public required string ActionTaken { get; init; }

    /// <summary>UTC timestamp when detected.</summary>
    public DateTime DetectedAt { get; init; } = DateTime.UtcNow;

    /// <summary>Cross-link to GenealogyRecord for process attribution.</summary>
    public Guid? GenealogyId { get; init; }

    /// <summary>Whether this event was suppressed by IT whitelist.</summary>
    public bool WhitelistSuppressed { get; init; }

    /// <summary>Kill chain correlation ID (shared across multi-stage events).</summary>
    public Guid? KillChainCorrelationId { get; set; }
}

/// <summary>
/// Types of indicator removal detected.
/// </summary>
public enum IndicatorRemovalType
{
    /// <summary>Event log clearing via wevtutil or Clear-EventLog.</summary>
    EventLogClearing,

    /// <summary>USN journal deletion via fsutil usn deletejournal.</summary>
    UsnJournalClearing,

    /// <summary>Windows Defender tampering via Set-MpPreference.</summary>
    DefenderTampering,

    /// <summary>Security scheduled task deletion via schtasks /delete.</summary>
    ScheduledTaskTampering,

    /// <summary>Multi-stage kill chain correlation (vssadmin + wevtutil + fsutil).</summary>
    KillChainCorrelation
}
