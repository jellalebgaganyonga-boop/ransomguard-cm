using Microsoft.Extensions.Logging;
using RansomGuard.Agent.Core.Detection.IronClad.Actions;
using RansomGuard.Agent.Core.Detection.UsbGuard.Models;
using RansomGuard.Agent.Core.Detection.UsbGuard.Quarantine;
using RansomGuard.Agent.Core.Detection.UsbGuard.Scanning;
using RansomGuard.Agent.Core.Persistence.Entities;
using RansomGuard.Agent.Core.Persistence.Repositories;

namespace RansomGuard.Agent.Core.Detection.UsbGuard.Actions;

/// <summary>
/// Executes USB response actions. Decision matrix: Mode x Severity -> Action.
/// Strict+Critical tries the IronClad physical port cut first (10-second timeout).
/// <para>
/// Real since 2026-10-22 (AGT-USB-001, after V7): the alert, the IronClad cut, <c>ReadOnlyUsb</c> (the disk behind the
/// drive letter made read-only), <c>EjectUsb</c> (CM_Request_Device_Eject) and <c>QuarantineFile</c>
/// (the flagged files of the scan, encrypted into the quarantine store). <c>BlockAndEject</c> ejects
/// for real but the software "block" (refusing the key when it comes back) does not exist: the
/// result says so (<see cref="UsbActionReasonCode.BlockNotImplemented"/>, AGT-USB-001).
/// </para>
/// Every outcome, carried out or not, is written to the signed audit log as it is.
/// </summary>
public sealed class UsbActionEngine : IUsbActionEngine
{
    private static readonly TimeSpan ActionTimeout = TimeSpan.FromSeconds(10);
    private readonly ILogger<UsbActionEngine> _logger;
    private readonly IAuditLogRepository _auditLog;
    private readonly IIronCladActionEngine? _ironCladEngine;
    private readonly IUsbDeviceControl? _control;
    private readonly IQuarantineService? _quarantine;

    /// <summary>Initializes the USB action engine.</summary>
    /// <param name="logger">Logger.</param>
    /// <param name="auditLog">The signed audit log.</param>
    /// <param name="ironCladEngine">The IronClad port cut, when wired.</param>
    /// <param name="control">The device calls (read-only, eject); null: those actions report <c>no_device_control</c>.</param>
    /// <param name="quarantine">The quarantine store; null: QuarantineFile reports <c>no_quarantine_service</c>.</param>
    public UsbActionEngine(
        ILogger<UsbActionEngine> logger,
        IAuditLogRepository auditLog,
        IIronCladActionEngine? ironCladEngine = null,
        IUsbDeviceControl? control = null,
        IQuarantineService? quarantine = null)
    {
        _logger = logger;
        _auditLog = auditLog;
        _ironCladEngine = ironCladEngine;
        _control = control;
        _quarantine = quarantine;
    }

    /// <inheritdoc />
    public Task<UsbActionResult> ExecuteAsync(
        UsbDevice device, ScanSeverity severity, UsbOperatingMode mode, string reason, CancellationToken ct = default) =>
        ExecuteAsync(device, severity, mode, reason, [], ct);

    /// <inheritdoc />
    public async Task<UsbActionResult> ExecuteAsync(
        UsbDevice device, ScanSeverity severity, UsbOperatingMode mode,
        string reason, IReadOnlyList<UsbFileFinding> findings, CancellationToken ct = default)
    {
        UsbActionResult result = await SelectAndExecuteAsync(device, severity, mode, reason, findings, ct).ConfigureAwait(false);
        await AuditAsync(result, device, severity, mode, ct).ConfigureAwait(false);
        return result;
    }

    /// <summary>The decision matrix: mode x severity -> action.</summary>
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
        UsbDevice device, ScanSeverity severity, UsbOperatingMode mode, string reason,
        IReadOnlyList<UsbFileFinding> findings, CancellationToken ct)
    {
        // Strict + Critical: IronClad physical port cut first -- the one blocking action that cuts power.
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

        return selectedAction switch
        {
            UsbActionType.AlertOnly => ExecuteAlertOnly(device, reason),
            UsbActionType.ReadOnlyUsb => await ReadOnlyAsync(device, reason, ct).ConfigureAwait(false),
            UsbActionType.EjectUsb => await EjectAsync(UsbActionType.EjectUsb, device, reason, ct).ConfigureAwait(false),
            UsbActionType.QuarantineFile => await QuarantineAsync(device, severity, reason, findings, ct).ConfigureAwait(false),
            UsbActionType.BlockAndEject => await BlockAndEjectAsync(device, reason, ct).ConfigureAwait(false),
            _ => ExecuteAlertOnly(device, reason),
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

    // The key stays plugged, every write on it fails from now on: the examination goes on, the
    // malware on it cannot spread, the file it would have written is not written.
    private async Task<UsbActionResult> ReadOnlyAsync(UsbDevice device, string reason, CancellationToken ct)
    {
        if (_control is null)
        {
            return NotDone(UsbActionType.ReadOnlyUsb, device, UsbActionReasonCode.NoDeviceControl, "no device control wired", reason);
        }

        if (string.IsNullOrEmpty(device.DriveLetter))
        {
            return NotDone(UsbActionType.ReadOnlyUsb, device, UsbActionReasonCode.NoDriveLetter, "the device has no drive letter", reason);
        }

        UsbControlResult outcome = await BoundedAsync(() => _control.SetReadOnly(device.DriveLetter), ct).ConfigureAwait(false);
        if (!outcome.Success)
        {
            return NotDone(UsbActionType.ReadOnlyUsb, device, UsbActionReasonCode.DeviceControlFailed, outcome.Error!, reason);
        }

        _logger.LogWarning("USB ACTION [ReadOnly]: {Drive} ({Device}) is now read-only — {Reason}", device.DriveLetter, device.ProductDescription, reason);
        return new UsbActionResult
        {
            ActionType = UsbActionType.ReadOnlyUsb,
            Success = true,
            Description = $"Drive {device.DriveLetter} set read-only (every write now fails): {reason}"
        };
    }

    private async Task<UsbActionResult> EjectAsync(UsbActionType reportedAs, UsbDevice device, string reason, CancellationToken ct)
    {
        if (_control is null)
        {
            return NotDone(reportedAs, device, UsbActionReasonCode.NoDeviceControl, "no device control wired", reason);
        }

        UsbControlResult outcome = await BoundedAsync(() => _control.Eject(device.DeviceInstanceId), ct).ConfigureAwait(false);
        if (!outcome.Success)
        {
            return NotDone(reportedAs, device, UsbActionReasonCode.DeviceControlFailed, outcome.Error!, reason);
        }

        _logger.LogWarning("USB ACTION [Eject]: {Device} ({Instance}) ejected — {Reason}", device.ProductDescription, device.DeviceInstanceId, reason);
        return new UsbActionResult
        {
            ActionType = reportedAs,
            Success = true,
            Description = $"Device {device.ProductDescription} ejected: {reason}"
        };
    }

    // Eject for real; the software block (refusing the same key when it comes back) does not exist,
    // and the result says so rather than claiming a block (AGT-USB-001).
    private async Task<UsbActionResult> BlockAndEjectAsync(UsbDevice device, string reason, CancellationToken ct)
    {
        UsbActionResult ejected = await EjectAsync(UsbActionType.BlockAndEject, device, reason, ct).ConfigureAwait(false);
        if (!ejected.Success)
        {
            return ejected;
        }

        _logger.LogWarning("USB ACTION [BlockAndEject]: {Device} ejected; the software block is not implemented (AGT-USB-001)", device.ProductDescription);
        return new UsbActionResult
        {
            ActionType = UsbActionType.BlockAndEject,
            Success = false,
            ReasonCode = UsbActionReasonCode.BlockNotImplemented,
            FallbackAction = UsbActionType.EjectUsb,
            Description = $"Device {device.ProductDescription} ejected; the block on re-insertion is NOT implemented: {reason}"
        };
    }

    // The flagged files of the scan (the ones that carry the alert's severity) leave the key,
    // encrypted into the quarantine store, restorable from there.
    private async Task<UsbActionResult> QuarantineAsync(
        UsbDevice device, ScanSeverity severity, string reason, IReadOnlyList<UsbFileFinding> findings, CancellationToken ct)
    {
        if (_quarantine is null)
        {
            return NotDone(UsbActionType.QuarantineFile, device, UsbActionReasonCode.NoQuarantineService, "no quarantine service wired", reason);
        }

        List<UsbFileFinding> targets = findings.Where(f => f.Severity >= ScanSeverity.High).ToList();
        if (targets.Count == 0)
        {
            return NotDone(UsbActionType.QuarantineFile, device, UsbActionReasonCode.NoFileToQuarantine,
                "the scan names no file at this severity", reason);
        }

        var done = new List<string>();
        var failed = new List<string>();
        foreach (UsbFileFinding finding in targets.DistinctBy(f => f.FilePath, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(ActionTimeout);
                QuarantineSeverity level = Enum.TryParse(finding.Severity.ToString(), out QuarantineSeverity parsed) ? parsed : QuarantineSeverity.High;
                await _quarantine.QuarantineAsync(finding.FilePath, $"{finding.FindingType}: {finding.Description}", level, device.SerialNumber, timeoutCts.Token)
                    .ConfigureAwait(false);
                done.Add(finding.FilePath);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                _logger.LogError(ex, "USB ACTION [Quarantine]: {File} could not be quarantined", finding.FilePath);
                failed.Add($"{finding.FilePath} ({ex.GetType().Name})");
            }
        }

        if (failed.Count > 0)
        {
            return NotDone(UsbActionType.QuarantineFile, device, UsbActionReasonCode.QuarantineFailed,
                $"{done.Count} quarantined, {failed.Count} not: {string.Join("; ", failed)}", reason);
        }

        _logger.LogWarning("USB ACTION [Quarantine]: {Count} file(s) from {Device} quarantined — {Reason}", done.Count, device.ProductDescription, reason);
        return new UsbActionResult
        {
            ActionType = UsbActionType.QuarantineFile,
            Success = true,
            Description = $"{done.Count} file(s) quarantined from {device.DriveLetter}: {string.Join("; ", done.Select(Path.GetFileName))} — {reason}"
        };
    }

    private UsbActionResult NotDone(UsbActionType action, UsbDevice device, string reasonCode, string why, string reason)
    {
        _logger.LogError(
            "USB ACTION NOT EXECUTED: {Action} selected for {Device} — {Why} ({ReasonCode}); nothing was done ({Reason})",
            action, device.ProductDescription, why, reasonCode, reason);
        return new UsbActionResult
        {
            ActionType = action,
            Success = false,
            ReasonCode = reasonCode,
            Description = $"{action} selected but NOT executed ({reasonCode}: {why}): {reason}"
        };
    }

    // A device call that hangs (a driver that never answers) must not hold the monitor: 10 s, then failed.
    private static async Task<UsbControlResult> BoundedAsync(Func<UsbControlResult> call, CancellationToken ct)
    {
        Task<UsbControlResult> work = Task.Run(call, ct);
        Task finished = await Task.WhenAny(work, Task.Delay(ActionTimeout, ct)).ConfigureAwait(false);
        if (finished != work)
        {
            return UsbControlResult.Failed($"no answer from the device after {ActionTimeout.TotalSeconds} s");
        }

        try
        {
            return await work.ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return UsbControlResult.Failed($"{ex.GetType().Name}: {ex.Message}");
        }
    }

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
