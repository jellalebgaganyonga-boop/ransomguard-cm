namespace RansomGuard.Agent.Core.Persistence.Entities;

/// <summary>
/// Global USB policy configuration stored in the database.
/// Controls operating mode and scan parameters.
/// </summary>
public sealed class UsbPolicy
{
    /// <summary>Unique policy identifier.</summary>
    public required Guid Id { get; init; }

    /// <summary>Operating mode: Audit, Permissive (default), Strict.</summary>
    public required UsbOperatingMode Mode { get; init; }

    /// <summary>Maximum file size in MB for content scanning.</summary>
    public required int MaxFileSizeForScanMB { get; init; }

    /// <summary>Maximum scan duration in seconds per USB device.</summary>
    public required int MaxScanDurationSeconds { get; init; }

    /// <summary>Whether to scan inside archive files.</summary>
    public required bool ScanArchiveContents { get; init; }

    /// <summary>JSON array of suspicious file extensions.</summary>
    public required string SuspiciousExtensionsJson { get; init; }

    /// <summary>Whether to block bootable USB devices.</summary>
    public required bool BlockBootableUsb { get; init; }

    /// <summary>Whether to alert on HID device connections.</summary>
    public required bool AlertOnHidDevice { get; init; }

    /// <summary>Whether to alert on USB network device connections.</summary>
    public required bool AlertOnNetworkDevice { get; init; }

    /// <summary>UTC timestamp when the policy was last updated.</summary>
    public required DateTime UpdatedAt { get; init; }
}

/// <summary>
/// USB operating mode controlling response severity.
/// </summary>
public enum UsbOperatingMode
{
    /// <summary>Log only — no blocking or quarantine actions.</summary>
    Audit,
    /// <summary>Log + quarantine suspicious files, alert on high severity. Default.</summary>
    Permissive,
    /// <summary>Block unknown devices, eject on critical findings.</summary>
    Strict
}
