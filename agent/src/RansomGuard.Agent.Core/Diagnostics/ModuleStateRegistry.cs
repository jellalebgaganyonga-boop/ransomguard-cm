using System.Collections.Concurrent;

namespace RansomGuard.Agent.Core.Diagnostics;

/// <summary>
/// Runtime state of a protection module. Replaces the old config-derived status:
/// a module that is enabled in config but failed to start must read as down, not
/// up — the worst lie a security product can tell its operator.
/// </summary>
public enum ModuleState
{
    /// <summary>Started and running.</summary>
    Active,

    /// <summary>Enabled but not running (failed to start, stopped, or missing a prerequisite).</summary>
    Inactive,

    /// <summary>Turned off in configuration — not a fault.</summary>
    DisabledByConfig,

    /// <summary>Running but not fully trustworthy (e.g. GENEALOGY attribution is unreliable).</summary>
    Degraded,
}

/// <summary>Stable module identifiers sent in the heartbeat. Never translated.</summary>
public static class ModuleCode
{
    /// <summary>Semantic medical canary files.</summary>
    public const string Sentinel = "SENTINEL";

    /// <summary>Shannon-entropy mass-encryption detection.</summary>
    public const string Entropy = "ENTROPY";

    /// <summary>Process genealogy enricher (not a detector).</summary>
    public const string Genealogy = "GENEALOGY";

    /// <summary>USB device scanning and policy.</summary>
    public const string UsbGuard = "USB_GUARD";

    /// <summary>Network exfiltration watch (kernel ETW).</summary>
    public const string ExfilWatch = "EXFIL_WATCH";

    /// <summary>Agent self-protection (not yet implemented).</summary>
    public const string AntiTampering = "ANTI_TAMPERING";

    /// <summary>The six entries the agent always reports, in display order.</summary>
    public static readonly IReadOnlyList<string> All =
        [Sentinel, Entropy, Genealogy, UsbGuard, ExfilWatch, AntiTampering];
}

/// <summary>
/// Closed set of reason codes. The agent never sends a sentence — the console
/// maps these to FR/EN labels.
/// </summary>
public static class ModuleReasonCode
{
    /// <summary>Requires administrator rights that the agent does not have.</summary>
    public const string AdminRightsMissing = "admin_rights_missing";

    /// <summary>Turned off in configuration.</summary>
    public const string DisabledByConfig = "disabled_by_config";

    /// <summary>Failed before reaching its running state.</summary>
    public const string InitFailed = "init_failed";

    /// <summary>Crashed after having started.</summary>
    public const string StoppedUnexpectedly = "stopped_unexpectedly";

    /// <summary>Capability not implemented yet.</summary>
    public const string NotImplemented = "not_implemented";

    /// <summary>Running but its output is not dependable.</summary>
    public const string AttributionUnreliable = "attribution_unreliable";

    /// <summary>
    /// Detects but cannot carry out the blocking actions its mode requires (USB GUARD in
    /// Strict). Console labels: FR « Blocage non implémenté », EN « Blocking not implemented ».
    /// </summary>
    public const string ActionsNotImplemented = "actions_not_implemented";
}

/// <summary>One module's current state.</summary>
public sealed record ModuleStateEntry(string Code, ModuleState State, string? ReasonCode, DateTime ChangedAt);

/// <summary>Where each module publishes its real runtime state, read by the heartbeat.</summary>
public interface IModuleStateRegistry
{
    /// <summary>
    /// Record a module's state. Does nothing if the state and reason are unchanged,
    /// so ChangedAt marks a real transition.
    /// </summary>
    void Publish(string code, ModuleState state, string? reasonCode = null);

    /// <summary>Current state of all six modules, in display order.</summary>
    IReadOnlyList<ModuleStateEntry> Snapshot();
}

/// <summary>
/// In-memory, thread-safe registry. Singleton for the process.
/// </summary>
/// <remarks>
/// Seeded so the heartbeat always reports all six entries, even before a module
/// has started:
/// <list type="bullet">
/// <item>the four detection modules start Inactive/init_failed — the safe default
///   for a security product is "not protecting until proven otherwise"; each
///   flips itself to Active once it reaches its "monitor active" point;</item>
/// <item>GENEALOGY is Degraded/attribution_unreliable: it is an enricher, not a
///   detector, and its process attribution measured 0/120 (see AttributionBench).
///   It becomes Active when ETW attribution exists and is measured;</item>
/// <item>ANTI_TAMPERING is Inactive/not_implemented: its services are registered
///   but never started (dead code). It joins the posture when the Watchdog ships.</item>
/// </list>
/// </remarks>
public sealed class InMemoryModuleStateRegistry : IModuleStateRegistry
{
    private readonly ConcurrentDictionary<string, ModuleStateEntry> _entries = new(StringComparer.Ordinal);
    private readonly Func<DateTime> _clock;

    /// <summary>Initializes the registry with honest startup defaults.</summary>
    public InMemoryModuleStateRegistry(Func<DateTime>? clock = null)
    {
        _clock = clock ?? (() => DateTime.UtcNow);
        DateTime now = _clock();

        _entries[ModuleCode.Sentinel] = new(ModuleCode.Sentinel, ModuleState.Inactive, ModuleReasonCode.InitFailed, now);
        _entries[ModuleCode.Entropy] = new(ModuleCode.Entropy, ModuleState.Inactive, ModuleReasonCode.InitFailed, now);
        _entries[ModuleCode.UsbGuard] = new(ModuleCode.UsbGuard, ModuleState.Inactive, ModuleReasonCode.InitFailed, now);
        _entries[ModuleCode.ExfilWatch] = new(ModuleCode.ExfilWatch, ModuleState.Inactive, ModuleReasonCode.InitFailed, now);
        _entries[ModuleCode.Genealogy] = new(ModuleCode.Genealogy, ModuleState.Degraded, ModuleReasonCode.AttributionUnreliable, now);
        _entries[ModuleCode.AntiTampering] = new(ModuleCode.AntiTampering, ModuleState.Inactive, ModuleReasonCode.NotImplemented, now);
    }

    /// <inheritdoc />
    public void Publish(string code, ModuleState state, string? reasonCode = null)
    {
        _entries.AddOrUpdate(
            code,
            _ => new ModuleStateEntry(code, state, reasonCode, _clock()),
            (_, existing) =>
                existing.State == state && existing.ReasonCode == reasonCode
                    ? existing  // no real change — keep the original ChangedAt
                    : new ModuleStateEntry(code, state, reasonCode, _clock()));
    }

    /// <inheritdoc />
    public IReadOnlyList<ModuleStateEntry> Snapshot() =>
        ModuleCode.All
            .Where(_entries.ContainsKey)
            .Select(code => _entries[code])
            .ToList();
}
