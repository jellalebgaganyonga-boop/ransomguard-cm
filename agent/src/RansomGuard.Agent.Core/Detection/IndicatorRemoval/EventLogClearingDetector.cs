using RansomGuard.Agent.Core.Detection.Genealogy;
using RansomGuard.Agent.Core.Persistence.Entities;

namespace RansomGuard.Agent.Core.Detection.IndicatorRemoval;

/// <summary>
/// Detects event log clearing via wevtutil cl or Clear-EventLog.
/// MITRE T1070.001 — Indicator Removal: Clear Windows Event Logs.
/// </summary>
public sealed class EventLogClearingDetector : IIndicatorRemovalDetector
{
    /// <inheritdoc />
    public string DetectorName => "EventLogClearing";

    /// <inheritdoc />
    public string MitreTechniqueId => "T1070.001";

    /// <inheritdoc />
    public IndicatorRemovalEvent? Evaluate(ProcessSnapshot process)
    {
        string name = process.ProcessName.ToLowerInvariant();
        string cmdLine = (process.CommandLine ?? "").ToLowerInvariant();

        // wevtutil cl <logname> — clears a Windows event log
        if (name is "wevtutil" or "wevtutil.exe" && cmdLine.Contains(" cl "))
        {
            string logName = ExtractLogName(cmdLine);

            if (ItWhitelist.IsWhitelisted(process.ProcessName, process.ExecutablePath, process.CommandLine))
                return CreateSuppressed(process, logName);

            return new IndicatorRemovalEvent
            {
                EventType = IndicatorRemovalType.EventLogClearing,
                MitreTechniqueId = MitreTechniqueId,
                ProcessId = process.ProcessId,
                ProcessName = process.ProcessName,
                CommandLine = process.CommandLine ?? "",
                TargetResource = logName,
                Severity = "Critical",
                Description = $"Event log clearing: wevtutil cl {logName} (PID {process.ProcessId})",
                ActionTaken = "Alert"
            };
        }

        // PowerShell Clear-EventLog
        if (name is "powershell" or "powershell.exe" or "pwsh" or "pwsh.exe"
            && cmdLine.Contains("clear-eventlog"))
        {
            string logName = ExtractPowerShellLogName(cmdLine);

            if (ItWhitelist.IsWhitelisted(process.ProcessName, process.ExecutablePath, process.CommandLine))
                return CreateSuppressed(process, logName);

            return new IndicatorRemovalEvent
            {
                EventType = IndicatorRemovalType.EventLogClearing,
                MitreTechniqueId = MitreTechniqueId,
                ProcessId = process.ProcessId,
                ProcessName = process.ProcessName,
                CommandLine = process.CommandLine ?? "",
                TargetResource = logName,
                Severity = "Critical",
                Description = $"Event log clearing: Clear-EventLog {logName} (PID {process.ProcessId})",
                ActionTaken = "Alert"
            };
        }

        return null;
    }

    private static string ExtractLogName(string cmdLine)
    {
        // "wevtutil cl Security" → "Security"
        int clIdx = cmdLine.IndexOf(" cl ", StringComparison.Ordinal);
        if (clIdx < 0) return "unknown";
        string after = cmdLine[(clIdx + 4)..].Trim();
        int spaceIdx = after.IndexOf(' ');
        return spaceIdx > 0 ? after[..spaceIdx] : after;
    }

    private static string ExtractPowerShellLogName(string cmdLine)
    {
        // "Clear-EventLog -LogName Security" → "Security"
        int idx = cmdLine.IndexOf("-logname", StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return "unknown";
        string after = cmdLine[(idx + 9)..].Trim();
        int spaceIdx = after.IndexOf(' ');
        return spaceIdx > 0 ? after[..spaceIdx] : after;
    }

    private IndicatorRemovalEvent CreateSuppressed(ProcessSnapshot process, string logName) => new()
    {
        EventType = IndicatorRemovalType.EventLogClearing,
        MitreTechniqueId = MitreTechniqueId,
        ProcessId = process.ProcessId,
        ProcessName = process.ProcessName,
        CommandLine = process.CommandLine ?? "",
        TargetResource = logName,
        Severity = "Low",
        Description = $"Event log clearing suppressed (whitelisted): {logName}",
        ActionTaken = "Suppressed",
        WhitelistSuppressed = true
    };
}
