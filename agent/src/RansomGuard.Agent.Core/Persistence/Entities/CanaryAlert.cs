namespace RansomGuard.Agent.Core.Persistence.Entities;

/// <summary>
/// High-confidence alert generated when a SENTINEL canary file is tampered with.
/// Any canary alert is a strong indicator of ransomware or malicious activity.
/// </summary>
public sealed class CanaryAlert
{
    /// <summary>
    /// Unique alert identifier.
    /// </summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>
    /// The canary that triggered this alert.
    /// </summary>
    public required Guid CanaryId { get; init; }

    /// <summary>
    /// Full path of the affected canary file.
    /// </summary>
    public required string CanaryPath { get; init; }

    /// <summary>
    /// Type of canary alert.
    /// </summary>
    public required CanaryAlertType AlertType { get; init; }

    /// <summary>
    /// Severity of the alert.
    /// </summary>
    public AlertSeverity Severity { get; init; } = AlertSeverity.Critical;

    /// <summary>
    /// Process ID of the offending process, if attribution succeeded.
    /// </summary>
    public int? OffendingProcessId { get; set; }

    /// <summary>
    /// Process name of the offending process, if attribution succeeded.
    /// </summary>
    public string? OffendingProcessName { get; set; }

    /// <summary>
    /// Executable path of the offending process, if attribution succeeded.
    /// </summary>
    public string? OffendingProcessPath { get; set; }

    /// <summary>
    /// UTC timestamp when the alert was detected.
    /// </summary>
    public DateTime DetectedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// UTC timestamp when the record was created.
    /// </summary>
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// UTC timestamp when the record was last updated.
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Type of canary file alert.
/// </summary>
public enum CanaryAlertType
{
    /// <summary>Canary file content was modified.</summary>
    CanaryModified,
    /// <summary>Canary file was deleted.</summary>
    CanaryDeleted,
    /// <summary>Canary file was renamed.</summary>
    CanaryRenamed
}

/// <summary>
/// Alert severity levels.
/// </summary>
public enum AlertSeverity
{
    /// <summary>Low severity — informational.</summary>
    Low,
    /// <summary>Medium severity — requires attention.</summary>
    Medium,
    /// <summary>High severity — likely threat.</summary>
    High,
    /// <summary>Critical severity — confirmed threat, immediate action required.</summary>
    Critical
}
