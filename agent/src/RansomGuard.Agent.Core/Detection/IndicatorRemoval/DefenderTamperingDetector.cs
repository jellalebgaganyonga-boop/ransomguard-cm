using RansomGuard.Agent.Core.Detection.Genealogy;
using RansomGuard.Agent.Core.Persistence.Entities;

namespace RansomGuard.Agent.Core.Detection.IndicatorRemoval;

/// <summary>
/// Detects Windows Defender tampering via Set-MpPreference with disable flags.
/// MITRE T1562.001 — Impair Defenses: Disable or Modify Tools.
/// </summary>
public sealed class DefenderTamperingDetector : IIndicatorRemovalDetector
{
    private static readonly string[] DisablePatterns =
    {
        "-disablerealtimemonitoring $true",
        "-disablebehaviormonitoring $true",
        "-disableblockatfirstseen $true",
        "-disableioavprotection $true",
        "-disablescriptscanning $true",
        "-disableintrusionpreventionsystem $true",
        "-submitsamplesconsent 2",     // NeverSend
        "-mapsreporting 0"             // Disabled
    };

    /// <inheritdoc />
    public string DetectorName => "DefenderTampering";

    /// <inheritdoc />
    public string MitreTechniqueId => "T1562.001";

    /// <inheritdoc />
    public IndicatorRemovalEvent? Evaluate(ProcessSnapshot process)
    {
        string name = process.ProcessName.ToLowerInvariant();
        string cmdLine = (process.CommandLine ?? "").ToLowerInvariant();

        if (name is not ("powershell" or "powershell.exe" or "pwsh" or "pwsh.exe"))
            return null;

        if (!cmdLine.Contains("set-mppreference"))
            return null;

        // Find which disable flag was used
        string? matchedPattern = null;
        foreach (var pattern in DisablePatterns)
        {
            if (cmdLine.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            {
                matchedPattern = pattern;
                break;
            }
        }

        if (matchedPattern is null)
            return null;

        if (ItWhitelist.IsWhitelisted(process.ProcessName, process.ExecutablePath, process.CommandLine))
        {
            return new IndicatorRemovalEvent
            {
                EventType = IndicatorRemovalType.DefenderTampering,
                MitreTechniqueId = MitreTechniqueId,
                ProcessId = process.ProcessId,
                ProcessName = process.ProcessName,
                CommandLine = process.CommandLine ?? "",
                TargetResource = matchedPattern,
                Severity = "Low",
                Description = $"Defender tampering suppressed (whitelisted): {matchedPattern}",
                ActionTaken = "Suppressed",
                WhitelistSuppressed = true
            };
        }

        return new IndicatorRemovalEvent
        {
            EventType = IndicatorRemovalType.DefenderTampering,
            MitreTechniqueId = MitreTechniqueId,
            ProcessId = process.ProcessId,
            ProcessName = process.ProcessName,
            CommandLine = process.CommandLine ?? "",
            TargetResource = matchedPattern,
            Severity = "Critical",
            Description = $"Defender tampering: Set-MpPreference {matchedPattern} (PID {process.ProcessId})",
            ActionTaken = "Alert"
        };
    }
}
