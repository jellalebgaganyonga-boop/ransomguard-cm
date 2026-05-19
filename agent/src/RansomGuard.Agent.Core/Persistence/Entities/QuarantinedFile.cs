namespace RansomGuard.Agent.Core.Persistence.Entities;

/// <summary>
/// A file quarantined by the USB GUARD module.
/// Encrypted with AES-256-GCM and stored in an isolated directory with DACL restrictions.
/// CWE-311 mitigation: sensitive files encrypted at rest.
/// CWE-285 mitigation: DACL restricts access to SYSTEM and Administrators.
/// </summary>
public sealed class QuarantinedFile
{
    /// <summary>Unique quarantine identifier.</summary>
    public required Guid Id { get; init; }

    /// <summary>Original file path before quarantine.</summary>
    public required string OriginalPath { get; init; }

    /// <summary>Path in quarantine directory ({Guid}.qrt).</summary>
    public required string QuarantinePath { get; init; }

    /// <summary>SHA-256 hash of the original file before encryption.</summary>
    public required string OriginalSha256 { get; init; }

    /// <summary>SHA-256 hash of the quarantined (encrypted) file.</summary>
    public required string QuarantineSha256 { get; init; }

    /// <summary>Original file size in bytes.</summary>
    public required long OriginalSize { get; init; }

    /// <summary>Reason the file was quarantined.</summary>
    public required string QuarantineReason { get; init; }

    /// <summary>Severity of the finding that triggered quarantine.</summary>
    public required QuarantineSeverity Severity { get; init; }

    /// <summary>Hashed serial number of the source USB device.</summary>
    public required string SourceUsbSerial { get; init; }

    /// <summary>UTC timestamp when the file was quarantined.</summary>
    public required DateTime QuarantinedAt { get; init; }

    /// <summary>User who triggered the quarantine.</summary>
    public required string QuarantinedByUser { get; init; }

    /// <summary>UTC timestamp when the file was restored, null if still quarantined.</summary>
    public DateTime? RestoredAt { get; set; }

    /// <summary>User who restored the file.</summary>
    public string? RestoredByUser { get; set; }

    /// <summary>Retention deadline — file auto-deleted after this date.</summary>
    public required DateTime RetainUntil { get; init; }

    /// <summary>Whether the quarantined file is encrypted (should always be true).</summary>
    public required bool IsEncrypted { get; init; }
}

/// <summary>
/// Severity of a quarantined file finding.
/// </summary>
public enum QuarantineSeverity
{
    /// <summary>Low-severity finding.</summary>
    Low,
    /// <summary>Medium-severity finding.</summary>
    Medium,
    /// <summary>High-severity finding.</summary>
    High,
    /// <summary>Critical-severity finding.</summary>
    Critical
}
