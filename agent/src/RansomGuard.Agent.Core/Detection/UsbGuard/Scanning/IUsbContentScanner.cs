using RansomGuard.Agent.Core.Detection.UsbGuard.Models;
using RansomGuard.Agent.Core.Persistence.Entities;

namespace RansomGuard.Agent.Core.Detection.UsbGuard.Scanning;

/// <summary>
/// Orchestrates all USB content sub-scanners against a mounted USB drive.
/// </summary>
public interface IUsbContentScanner
{
    /// <summary>
    /// Scans all files on a USB drive according to the given policy.
    /// Honors cancellation for rapid-unplug scenarios.
    /// </summary>
    Task<UsbScanReport> ScanAsync(
        UsbDevice device,
        UsbPolicy policy,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Aggregated result of a USB content scan.
/// </summary>
public sealed record UsbScanReport
{
    /// <summary>Unique scan identifier.</summary>
    public required Guid Id { get; init; }

    /// <summary>Device tracking ID.</summary>
    public required Guid UsbDeviceId { get; init; }

    /// <summary>Drive letter scanned (e.g., "E:").</summary>
    public required string DriveLetter { get; init; }

    /// <summary>Total files scanned.</summary>
    public required int FilesScanned { get; init; }

    /// <summary>Files skipped (size limit, canary, rate limit).</summary>
    public required int FilesSkipped { get; init; }

    /// <summary>All findings from all sub-scanners.</summary>
    public required IReadOnlyList<UsbFileFinding> Findings { get; init; }

    /// <summary>Highest severity across all findings.</summary>
    public required ScanSeverity OverallSeverity { get; init; }

    /// <summary>Total scan duration.</summary>
    public required TimeSpan Duration { get; init; }

    /// <summary>UTC time the scan started.</summary>
    public required DateTime ScannedAt { get; init; }

    /// <summary>True if scan was interrupted by cancellation (rapid unplug).</summary>
    public required bool WasCancelled { get; init; }
}

/// <summary>
/// Individual finding from a USB file scan.
/// </summary>
public sealed record UsbFileFinding
{
    /// <summary>Full path of the scanned file.</summary>
    public required string FilePath { get; init; }

    /// <summary>Type of finding (MagicByteMismatch, Autorun, SuspiciousLnk, ArchiveThreat, HighEntropy).</summary>
    public required string FindingType { get; init; }

    /// <summary>Human-readable description.</summary>
    public required string Description { get; init; }

    /// <summary>Severity of this finding.</summary>
    public required ScanSeverity Severity { get; init; }

    /// <summary>Name of the detector that produced this finding.</summary>
    public required string DetectorName { get; init; }
}
