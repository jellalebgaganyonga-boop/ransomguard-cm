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
}

/// <summary>
/// Result of a USB action execution.
/// </summary>
public sealed record UsbActionResult
{
    /// <summary>The action type that was executed.</summary>
    public required UsbActionType ActionType { get; init; }

    /// <summary>Whether the action succeeded.</summary>
    public required bool Success { get; init; }

    /// <summary>Description of the action taken.</summary>
    public required string Description { get; init; }

    /// <summary>Fallback action executed if primary failed, null if no fallback needed.</summary>
    public UsbActionType? FallbackAction { get; init; }
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
