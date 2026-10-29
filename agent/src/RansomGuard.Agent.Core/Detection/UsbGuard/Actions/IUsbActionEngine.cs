using RansomGuard.Agent.Core.Detection.UsbGuard.Models;
using RansomGuard.Agent.Core.Detection.UsbGuard.Scanning;
using RansomGuard.Agent.Core.Persistence.Entities;

namespace RansomGuard.Agent.Core.Detection.UsbGuard.Actions;

/// <summary>
/// Executes response actions for USB threats based on operating mode and severity.
/// Actions: AlertOnly, QuarantineFile, ReadOnlyUsb, EjectUsb, BlockAndEject.
/// </summary>
public interface IUsbActionEngine
{
    /// <summary>
    /// Determines and executes the appropriate action based on mode and severity.
    /// </summary>
    /// <param name="device">The USB device involved.</param>
    /// <param name="severity">Severity of the finding.</param>
    /// <param name="mode">Current operating mode.</param>
    /// <param name="reason">Description of the finding.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The action that was executed.</returns>
    Task<UsbActionResult> ExecuteAsync(UsbDevice device, ScanSeverity severity,
        UsbOperatingMode mode, string reason, CancellationToken ct = default);

    /// <summary>
    /// The same, with the files the scan flagged: <c>QuarantineFile</c> takes the ones at High or
    /// above off the key. Without them it reports <see cref="UsbActionReasonCode.NoFileToQuarantine"/>.
    /// </summary>
    Task<UsbActionResult> ExecuteAsync(UsbDevice device, ScanSeverity severity,
        UsbOperatingMode mode, string reason, IReadOnlyList<UsbFileFinding> findings, CancellationToken ct = default);
}

/// <summary>
/// Result of a USB action execution.
/// </summary>
public sealed record UsbActionResult
{
    /// <summary>The action type that was executed.</summary>
    public required UsbActionType ActionType { get; init; }

    /// <summary>Whether the action was actually carried out. Never true for an action that did not happen.</summary>
    public required bool Success { get; init; }

    /// <summary>Description of the action taken.</summary>
    public required string Description { get; init; }

    /// <summary>Fallback action executed if primary failed, null if no fallback needed.</summary>
    public UsbActionType? FallbackAction { get; init; }

    /// <summary>Why the action was not carried out (see <see cref="UsbActionReasonCode"/>); null on success.</summary>
    public string? ReasonCode { get; init; }
}

/// <summary>Stable reason codes for a USB action that was not carried out.</summary>
public static class UsbActionReasonCode
{
    /// <summary><c>BlockAndEject</c>: the key was ejected, the block on re-insertion does not exist (AGT-USB-001).</summary>
    public const string BlockNotImplemented = "block_not_implemented";

    /// <summary>No <see cref="IUsbDeviceControl"/> wired: read-only and eject cannot be asked of the system.</summary>
    public const string NoDeviceControl = "no_device_control";

    /// <summary>The device has no drive letter: nothing to set read-only.</summary>
    public const string NoDriveLetter = "no_drive_letter";

    /// <summary>The system call failed or did not answer within the timeout; the description carries the error.</summary>
    public const string DeviceControlFailed = "device_control_failed";

    /// <summary>No quarantine service wired: no file can leave the key.</summary>
    public const string NoQuarantineService = "no_quarantine_service";

    /// <summary>The scan named no file at High or above: nothing to quarantine.</summary>
    public const string NoFileToQuarantine = "no_file_to_quarantine";

    /// <summary>At least one flagged file could not be quarantined; the description names them.</summary>
    public const string QuarantineFailed = "quarantine_failed";
}

/// <summary>
/// Types of USB response actions.
/// </summary>
public enum UsbActionType
{
    /// <summary>Alert only — log and notify, no blocking.</summary>
    AlertOnly,
    /// <summary>Quarantine the suspicious file.</summary>
    QuarantineFile,
    /// <summary>Set USB drive to read-only mode.</summary>
    ReadOnlyUsb,
    /// <summary>Safely eject the USB device.</summary>
    EjectUsb,
    /// <summary>Block and eject — add to permanent blacklist then eject.</summary>
    BlockAndEject
}
