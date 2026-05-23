using RansomGuard.Agent.Core.Detection.Genealogy;
using RansomGuard.Agent.Core.Persistence.Entities;

namespace RansomGuard.Agent.Core.Detection.IndicatorRemoval;

/// <summary>
/// Detects deletion of security-related scheduled tasks via schtasks /delete.
/// MITRE T1562.001 — Impair Defenses: Disable or Modify Tools.
/// </summary>
public sealed class SchedTaskTamperingDetector : IIndicatorRemovalDetector
{
    private static readonly string[] SecurityTaskPatterns =
    {
        "windows defender",
        "windowsdefender",
        "microsoft antimalware",
        "security health",
        "securityhealth",
        "windows update",
        "windowsupdate",
        "microsoft\\windows\\windowsupdate",
        "backupmonitor",
        "systemrestore",
        "shadowcopy",
        "vssadmin"
    };

    /// <inheritdoc />
    public string DetectorName => "SchedTaskTampering";

    /// <inheritdoc />
    public string MitreTechniqueId => "T1562.001";

    /// <inheritdoc />
    public IndicatorRemovalEvent? Evaluate(ProcessSnapshot process)
    {
        string name = process.ProcessName.ToLowerInvariant();
        string cmdLine = (process.CommandLine ?? "").ToLowerInvariant();

        if (name is not ("schtasks" or "schtasks.exe"))
            return null;

        if (!cmdLine.Contains("/delete"))
            return null;

        // Check if the task being deleted is security-related
        string? matchedTask = null;
        foreach (var pattern in SecurityTaskPatterns)
        {
            if (cmdLine.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            {
                matchedTask = pattern;
                break;
            }
        }

        if (matchedTask is null)
            return null; // Non-security task deletion — not suspicious

        string taskName = ExtractTaskName(cmdLine);

        if (ItWhitelist.IsWhitelisted(process.ProcessName, process.ExecutablePath, process.CommandLine))
        {
            return new IndicatorRemovalEvent
            {
                EventType = IndicatorRemovalType.ScheduledTaskTampering,
                MitreTechniqueId = MitreTechniqueId,
                ProcessId = process.ProcessId,
                ProcessName = process.ProcessName,
                CommandLine = process.CommandLine ?? "",
                TargetResource = taskName,
                Severity = "Low",
                Description = $"Security task deletion suppressed (whitelisted): {taskName}",
                ActionTaken = "Suppressed",
                WhitelistSuppressed = true
            };
        }

        return new IndicatorRemovalEvent
        {
            EventType = IndicatorRemovalType.ScheduledTaskTampering,
            MitreTechniqueId = MitreTechniqueId,
            ProcessId = process.ProcessId,
            ProcessName = process.ProcessName,
            CommandLine = process.CommandLine ?? "",
            TargetResource = taskName,
            Severity = "High",
            Description = $"Security task deletion: schtasks /delete targeting {taskName} (PID {process.ProcessId})",
            ActionTaken = "Alert"
        };
    }

    private static string ExtractTaskName(string cmdLine)
    {
        // "schtasks /delete /tn "Windows Defender Scheduled Scan" /f" → task name
        int tnIdx = cmdLine.IndexOf("/tn", StringComparison.OrdinalIgnoreCase);
        if (tnIdx < 0) return "unknown";
        string after = cmdLine[(tnIdx + 3)..].Trim();

        // Handle quoted task names
        if (after.StartsWith('"'))
        {
            int endQuote = after.IndexOf('"', 1);
            return endQuote > 0 ? after[1..endQuote] : after[1..];
        }

        int spaceIdx = after.IndexOf(' ');
        return spaceIdx > 0 ? after[..spaceIdx] : after;
    }
}
