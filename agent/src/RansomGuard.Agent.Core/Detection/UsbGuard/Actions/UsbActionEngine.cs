using Microsoft.Extensions.Logging;
using RansomGuard.Agent.Core.Detection.IronClad.Actions;
using RansomGuard.Agent.Core.Detection.UsbGuard.Models;
using RansomGuard.Agent.Core.Detection.UsbGuard.Scanning;
using RansomGuard.Agent.Core.Persistence.Entities;
using RansomGuard.Agent.Core.Persistence.Repositories;

namespace RansomGuard.Agent.Core.Detection.UsbGuard.Actions;

/// <summary>
/// Executes USB response actions. Decision matrix: Mode x Severity -> Action.
/// Strict+Critical tries the IronClad physical port cut first (10-second timeout).
/// <para>
/// Only two outcomes are real today: the alert, and the IronClad cut. ReadOnly, Quarantine,
/// Eject and the software BlockAndEject are NOT implemented: they return
/// <c>Success = false</c> with <see cref="UsbActionReasonCode.NotImplemented"/>, never a claim
/// that something was done (debt AGT-USB-001).
/// </para>
/// Every outcome, carried out or not, is written to the signed audit log as it is.
/// </summary>
public sealed class UsbActionEngine : IUsbActionEngine
{
    private static readonly TimeSpan ActionTimeout = TimeSpan.FromSeconds(10);
    private readonly ILogger<UsbActionEngine> _logger;
    private readonly IAuditLogRepository _auditLog;
    private readonly IIronCladActionEngine? _ironCladEngine;

    /// <summary>Initializes the USB action engine with optional IronClad integration.</summary>
    public UsbActionEngine(
        ILogger<UsbActionEngine> logger, IAuditLogRepository auditLog, IIronCladActionEngine? ironCladEngine = null)
    {
        _logger = logger;
        _auditLog = auditLog;
        _ironCladEngine = ironCladEngine;
    }

    /// <inheritdoc />
    public async Task<UsbActionResult> ExecuteAsync(
        UsbDevice device, ScanSeverity severity, UsbOperatingMode mode,
        string reason, CancellationToken ct = default)
    {
        UsbActionResult result = await SelectAndExecuteAsync(device, severity, mode, reason, ct).ConfigureAwait(false);
        await AuditAsync(result, device, severity, mode, ct).ConfigureAwait(false);
        return result;
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

    private async Task<UsbActionResult> SelectAndExecuteAsync(
        UsbDevice device, ScanSeverity severity, UsbOperatingMode mode, string reason, CancellationToken ct)
    {
        // Strict + Critical: IronClad physical port cut first -- the one real blocking action.
        if (mode == UsbOperatingMode.Strict && severity == ScanSeverity.Critical
            && _ironCladEngine is not null && _ironCladEngine.IsAvailable)
        {
            var portNumber = MapDriveLetterToPort(device.DriveLetter);
            if (portNumber > 0)
            {
                try
                {
                    using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    timeoutCts.CancelAfter(ActionTimeout);
                    var ironCladResult = await _ironCladEngine.CutUsbPortAsync(
                        portNumber,
                        $"USB Critical severity: {device.ProductDescription}, reason: {reason}",
                        null, timeoutCts.Token).ConfigureAwait(false);

                    if (ironCladResult.IsSuccess)
                    {
                        _logger.LogCritical("IronClad CUT executed for USB device {Device} on port {Port}",
                            device.ProductDescription, portNumber);
                        return new UsbActionResult
                        {
                            ActionType = UsbActionType.BlockAndEject,
                            Success = true,
                            Description = $"IronClad physical port cut on port {portNumber}: {reason}"
                        };
                    }

                    _logger.LogWarning("IronClad CUT failed ({Outcome}), falling back to software action",
                        ironCladResult.Outcome);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    _logger.LogWarning("IronClad CUT timed out after {Timeout}, falling back to software action",
                        ActionTimeout);
                }
            }
        }

        UsbActionType selectedAction = SelectAction(mode, severity);

        _logger.LogInformation(
            "USB ACTION: {Action} on {Device} (mode: {Mode}, severity: {Severity}, reason: {Reason})",
            selectedAction, device.ProductDescription, mode, severity, reason);

        return selectedAction == UsbActionType.AlertOnly
            ? ExecuteAlertOnly(device, reason)
            : NotImplemented(selectedAction, device, reason);
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

    /// <summary>
    /// ReadOnly, Quarantine, Eject and the software BlockAndEject: selected, never carried out.
    /// Reported as such -- nothing was done to the device.
    /// </summary>
    private UsbActionResult NotImplemented(UsbActionType action, UsbDevice device, string reason)
    {
        _logger.LogWarning(
            "USB ACTION NOT EXECUTED: {Action} selected for {Device} but not implemented — nothing was done ({Reason})",
            action, device.ProductDescription, reason);
        return new UsbActionResult
        {
            ActionType = action,
            Success = false,
            ReasonCode = UsbActionReasonCode.NotImplemented,
            Description = $"{action} selected but NOT executed (not implemented): {reason}"
        };
    }

    /// <summary>Writes the outcome, as it is, to the signed audit log.</summary>
    private async Task AuditAsync(
        UsbActionResult result, UsbDevice device, ScanSeverity severity, UsbOperatingMode mode, CancellationToken ct)
    {
        string outcome = result.Success ? "executed" : result.ReasonCode ?? "failed";
        string details =
            $"action={result.ActionType} outcome={outcome} mode={mode} severity={severity} " +
            $"device=\"{device.ProductDescription}\" drive={device.DriveLetter ?? "-"} | {result.Description}";
        try
        {
            await _auditLog.AppendAsync("UsbAction", details, entityType: "UsbDevice", cancellationToken: ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The alert must not be lost because the audit write failed; the failure is loud.
            _logger.LogError(ex, "USB GUARD: audit entry could not be written for {Details}", details);
        }
    }

    /// <summary>Maps a drive letter to a physical relay port number. Returns 0 if no mapping.</summary>
    private static int MapDriveLetterToPort(string? driveLetter)
    {
        if (string.IsNullOrEmpty(driveLetter)) return 0;
        var letter = driveLetter.TrimEnd(':', '\\', '/').ToUpperInvariant();
        return letter switch
        {
            "E" => 1,
            "F" => 2,
            "G" => 3,
            "H" => 4,
            _ => 0
        };
    }
}
