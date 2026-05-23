using RansomGuard.Agent.Core.Detection.Genealogy;
using RansomGuard.Agent.Core.Persistence.Entities;

namespace RansomGuard.Agent.Core.Detection.IndicatorRemoval;

/// <summary>
/// Detects USN journal clearing via fsutil usn deletejournal.
/// MITRE T1070.004 — Indicator Removal: File Deletion (USN Journal).
/// </summary>
public sealed class UsnJournalClearingDetector : IIndicatorRemovalDetector
{
    /// <inheritdoc />
    public string DetectorName => "UsnJournalClearing";

    /// <inheritdoc />
    public string MitreTechniqueId => "T1070.004";

    /// <inheritdoc />
    public IndicatorRemovalEvent? Evaluate(ProcessSnapshot process)
    {
        string name = process.ProcessName.ToLowerInvariant();
        string cmdLine = (process.CommandLine ?? "").ToLowerInvariant();

        if (name is not ("fsutil" or "fsutil.exe"))
            return null;

        if (!cmdLine.Contains("usn") || !cmdLine.Contains("deletejournal"))
            return null;

        string drive = ExtractDrive(cmdLine);

        if (ItWhitelist.IsWhitelisted(process.ProcessName, process.ExecutablePath, process.CommandLine))
        {
            return new IndicatorRemovalEvent
            {
                EventType = IndicatorRemovalType.UsnJournalClearing,
                MitreTechniqueId = MitreTechniqueId,
                ProcessId = process.ProcessId,
                ProcessName = process.ProcessName,
                CommandLine = process.CommandLine ?? "",
                TargetResource = drive,
                Severity = "Low",
                Description = $"USN journal clearing suppressed (whitelisted): {drive}",
                ActionTaken = "Suppressed",
                WhitelistSuppressed = true
            };
        }

        return new IndicatorRemovalEvent
        {
            EventType = IndicatorRemovalType.UsnJournalClearing,
            MitreTechniqueId = MitreTechniqueId,
            ProcessId = process.ProcessId,
            ProcessName = process.ProcessName,
            CommandLine = process.CommandLine ?? "",
            TargetResource = drive,
            Severity = "Critical",
            Description = $"USN journal deletion: fsutil usn deletejournal on {drive} (PID {process.ProcessId})",
            ActionTaken = "Alert"
        };
    }

    private static string ExtractDrive(string cmdLine)
    {
        // "fsutil usn deletejournal /d C:" → "C:"
        foreach (var part in cmdLine.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part.Length == 2 && part[1] == ':')
                return part.ToUpperInvariant();
        }
        return "unknown";
    }
}
