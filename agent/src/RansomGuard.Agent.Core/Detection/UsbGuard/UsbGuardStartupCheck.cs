using Microsoft.Extensions.Logging;
using RansomGuard.Agent.Core.Diagnostics;
using RansomGuard.Agent.Core.Persistence.Entities;

namespace RansomGuard.Agent.Core.Detection.UsbGuard;

/// <summary>
/// Startup guard rail for USB GUARD in Strict mode. Strict selects ejection for every key
/// scored Medium or above; with an empty whitelist that includes the facility's own
/// legitimate keys. This is a visible warning, not a block: the operator chose Strict in
/// writing, the agent makes the consequence explicit.
/// </summary>
public static class UsbGuardStartupCheck
{
    /// <summary>Warning written when Strict runs with an empty whitelist.</summary>
    public const string EmptyWhitelistWarning =
        "USB GUARD is in Strict mode with an EMPTY whitelist: every USB device scored Medium or " +
        "above gets the Strict action (ejection; physical port cut for Critical), including the " +
        "facility's legitimate keys. Populate the whitelist before running Strict in production " +
        "(see docs/modules/usb-guard.md)";

    /// <summary>Warning written when the whitelist cannot be read at startup.</summary>
    public const string UnreadableWhitelistWarning =
        "USB GUARD could not read the whitelist at startup. While it stays unreadable, every USB " +
        "device connection fails before the scan: the device is LET THROUGH, neither scanned nor " +
        "blocked, in every mode including Strict (see docs/modules/usb-guard.md)";

    /// <summary>
    /// Posture USB GUARD reports once running: Degraded / actions_not_implemented in every mode
    /// while the actions are missing (V7, AGT-USB-001). Strict promises eject and block,
    /// Permissive promises read-only and quarantine, and none of them happens (only the IronClad
    /// cut is real); Audit promises alert only and reads the same, so that "active" never means
    /// "USB GUARD acts" anywhere. It becomes Active when the actions of the mode exist.
    /// </summary>
    public static (ModuleState State, string? ReasonCode) RunningPosture(UsbOperatingMode mode) =>
        (ModuleState.Degraded, ModuleReasonCode.ActionsNotImplemented);

    /// <summary>
    /// In Strict mode, reads the whitelist and logs a warning when it holds no active entry or
    /// cannot be read. Returns true when a warning was written. Other modes are not checked.
    /// </summary>
    public static async Task<bool> WarnIfStrictWithEmptyWhitelistAsync(
        UsbOperatingMode mode, IUsbWhitelistService whitelist, ILogger logger, CancellationToken ct = default)
    {
        if (mode != UsbOperatingMode.Strict)
            return false;

        IReadOnlyList<UsbWhitelistEntry> active;
        try
        {
            active = await whitelist.ListActiveAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, UnreadableWhitelistWarning);
            return true;
        }

        if (active.Count > 0)
            return false;

        logger.LogWarning(EmptyWhitelistWarning);
        return true;
    }
}
