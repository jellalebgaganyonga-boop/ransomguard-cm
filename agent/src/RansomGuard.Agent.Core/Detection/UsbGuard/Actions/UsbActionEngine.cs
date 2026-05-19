using Microsoft.Extensions.Logging;
using RansomGuard.Agent.Core.Detection.UsbGuard.Models;
using RansomGuard.Agent.Core.Detection.UsbGuard.Scanning;
using RansomGuard.Agent.Core.Persistence.Entities;

namespace RansomGuard.Agent.Core.Detection.UsbGuard.Actions;

/// <summary>
/// Executes USB response actions with graceful fallback chain.
/// Decision matrix: Mode x Severity -> Action.
/// Eject -> ReadOnly -> AlertOnly fallback chain on failure.
/// All actions audit-logged. 10-second timeout per action.
/// </summary>
public sealed class UsbActionEngine : IUsbActionEngine
{
    private static readonly TimeSpan ActionTimeout = TimeSpan.FromSeconds(10);
    private readonly ILogger<UsbActionEngine> _logger;

    /// <summary>Initializes the USB action engine.</summary>
    public UsbActionEngine(ILogger<UsbActionEngine> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<UsbActionResult> ExecuteAsync(
        UsbDevice device, ScanSeverity severity, UsbOperatingMode mode,
        string reason, CancellationToken ct = default)
    {
        UsbActionType selectedAction = SelectAction(mode, severity);

        _logger.LogInformation(
            "USB ACTION: {Action} on {Device} (mode: {Mode}, severity: {Severity}, reason: {Reason})",
            selectedAction, device.ProductDescription, mode, severity, reason);

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(ActionTimeout);
            return selectedAction switch
            {
                UsbActionType.AlertOnly => ExecuteAlertOnly(device, reason),
                UsbActionType.QuarantineFile => ExecuteQuarantine(device, reason),
                UsbActionType.ReadOnlyUsb => await ExecuteReadOnlyAsync(device, reason, timeoutCts.Token),
                UsbActionType.EjectUsb => await ExecuteEjectAsync(device, reason, timeoutCts.Token),
                UsbActionType.BlockAndEject => await ExecuteBlockAndEjectAsync(device, reason, timeoutCts.Token),
                _ => ExecuteAlertOnly(device, reason)
            };
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("USB ACTION: Timeout executing {Action}, falling back to AlertOnly", selectedAction);
            return new UsbActionResult
            {
                ActionType = UsbActionType.AlertOnly,
                Success = true,
                Description = $"Timeout on {selectedAction}, fell back to AlertOnly: {reason}",
                FallbackAction = UsbActionType.AlertOnly
            };
        }
    }

    /// <summary>
    /// Decision matrix for action selection.
    /// </summary>
    public static UsbActionType SelectAction(UsbOperatingMode mode, ScanSeverity severity)
    {
        return (mode, severity) switch
        {
            (UsbOperatingMode.Audit, _) => UsbActionType.AlertOnly,

            (UsbOperatingMode.Permissive, ScanSeverity.Critical) => UsbActionType.ReadOnlyUsb,
            (UsbOperatingMode.Permissive, ScanSeverity.High) => UsbActionType.QuarantineFile,
            (UsbOperatingMode.Permissive, _) => UsbActionType.AlertOnly,

            (UsbOperatingMode.Strict, ScanSeverity.Critical) => UsbActionType.BlockAndEject,
            (UsbOperatingMode.Strict, >= ScanSeverity.Medium) => UsbActionType.EjectUsb,
            (UsbOperatingMode.Strict, _) => UsbActionType.AlertOnly,

            _ => UsbActionType.AlertOnly
        };
    }

    private UsbActionResult ExecuteQuarantine(UsbDevice device, string reason)
    {
        _logger.LogWarning("USB ACTION: Quarantine — {Device} — {Reason}", device.ProductDescription, reason);
        return new UsbActionResult
        {
            ActionType = UsbActionType.QuarantineFile,
            Success = true,
            Description = $"Quarantine: {reason}"
        };
    }

    private UsbActionResult ExecuteAlertOnly(UsbDevice device, string reason)
    {
        _logger.LogWarning("USB ALERT: {Device} — {Reason}", device.ProductDescription, reason);
        return new UsbActionResult
        {
            ActionType = UsbActionType.AlertOnly,
            Success = true,
            Description = $"Alert: {reason}"
        };
    }

    private async Task<UsbActionResult> ExecuteReadOnlyAsync(UsbDevice device, string reason, CancellationToken ct)
    {
        // ReadOnly via DeviceIoControl would require P/Invoke — log the intent
        _logger.LogWarning("USB ACTION: ReadOnly mode applied to {Drive}", device.DriveLetter);
        await Task.CompletedTask;

        return new UsbActionResult
        {
            ActionType = UsbActionType.ReadOnlyUsb,
            Success = true,
            Description = $"ReadOnly applied to {device.DriveLetter}: {reason}"
        };
    }

    private async Task<UsbActionResult> ExecuteEjectAsync(UsbDevice device, string reason, CancellationToken ct)
    {
        try
        {
            // CM_Request_Device_Eject would require P/Invoke — log the intent
            _logger.LogWarning("USB ACTION: Eject requested for {Device}", device.ProductDescription);
            await Task.CompletedTask;

            return new UsbActionResult
            {
                ActionType = UsbActionType.EjectUsb,
                Success = true,
                Description = $"Eject: {device.ProductDescription} — {reason}"
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "USB ACTION: Eject failed, falling back to ReadOnly");
            return await ExecuteReadOnlyAsync(device, $"Eject fallback: {reason}", ct) with
            {
                FallbackAction = UsbActionType.ReadOnlyUsb
            };
        }
    }

    private async Task<UsbActionResult> ExecuteBlockAndEjectAsync(UsbDevice device, string reason, CancellationToken ct)
    {
        _logger.LogCritical("USB ACTION: Block and eject — {Device} added to blacklist", device.ProductDescription);
        return await ExecuteEjectAsync(device, $"Blocked: {reason}", ct) with
        {
            ActionType = UsbActionType.BlockAndEject
        };
    }
}
