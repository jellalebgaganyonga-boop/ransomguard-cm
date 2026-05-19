namespace RansomGuard.Agent.Core.Persistence.Entities;

/// <summary>
/// Result of a USB content scan, linked to a UsbConnectionLog.
/// </summary>
public sealed class UsbScanResult
{
    /// <summary>Unique scan result identifier.</summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>The connection log this scan is for.</summary>
    public required Guid UsbConnectionLogId { get; init; }

    /// <summary>Total files scanned.</summary>
    public required int TotalFilesScanned { get; init; }

    /// <summary>Number of files flagged as suspicious.</summary>
    public required int FlaggedFilesCount { get; init; }

    /// <summary>JSON array of flagged file details.</summary>
    public required string FlaggedFilesJson { get; init; }

    /// <summary>Scan duration in milliseconds.</summary>
    public required long ScanDurationMs { get; init; }

    /// <summary>Whether the scan completed or was interrupted.</summary>
    public required bool ScanCompleted { get; init; }

    /// <summary>Highest severity found during scan.</summary>
    public required string HighestSeverity { get; init; }

    /// <summary>UTC timestamp when the scan started.</summary>
    public required DateTime ScannedAt { get; init; }
}
