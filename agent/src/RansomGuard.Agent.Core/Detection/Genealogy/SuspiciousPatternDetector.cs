namespace RansomGuard.Agent.Core.Detection.Genealogy;

/// <summary>
/// Detects suspicious parent-child process relationships mapped to MITRE ATT and CK techniques.
/// Used by GENEALOGY to flag ransomware attack chains.
/// </summary>
public static class SuspiciousPatternDetector
{
    /// <summary>
    /// Analyzes a process tree for suspicious patterns.
    /// </summary>
    /// <param name="tree">Process tree to analyze.</param>
    /// <returns>List of suspicious pattern flags found.</returns>
    public static IReadOnlyList<SuspiciousPatternFlag> Analyze(ProcessTree tree)
    {
        var flags = new List<SuspiciousPatternFlag>();

        // Check root process
        AnalyzeProcess(tree.Root, tree.Ancestors.FirstOrDefault(), flags);

        // Check each ancestor pair
        for (int i = 0; i < tree.Ancestors.Count - 1; i++)
        {
            AnalyzeProcess(tree.Ancestors[i], tree.Ancestors[i + 1], flags);
        }

        return flags;
    }

    private static void AnalyzeProcess(ProcessSnapshot process, ProcessSnapshot? parent, List<SuspiciousPatternFlag> flags)
    {
        string name = process.ProcessName.ToLowerInvariant();
        string? parentName = parent?.ProcessName.ToLowerInvariant();
        string cmdLine = (process.CommandLine ?? "").ToLowerInvariant();

        // T1490: Inhibit System Recovery — vssadmin delete shadows
        if (name is "vssadmin" && cmdLine.Contains("delete") && cmdLine.Contains("shadow"))
        {
            flags.Add(new SuspiciousPatternFlag
            {
                TechniqueId = "T1490",
                TechniqueName = "Inhibit System Recovery",
                Description = $"vssadmin delete shadows detected (parent: {parentName ?? "unknown"})",
                Severity = "Critical"
            });
        }

        // T1490: bcdedit /set recoveryenabled no
        if (name is "bcdedit" && cmdLine.Contains("recoveryenabled") && cmdLine.Contains("no"))
        {
            flags.Add(new SuspiciousPatternFlag
            {
                TechniqueId = "T1490",
                TechniqueName = "Inhibit System Recovery",
                Description = "bcdedit disabling recovery mode",
                Severity = "Critical"
            });
        }

        // T1059.001: Office app spawning shell
        if (parentName is "winword" or "excel" or "powerpnt" or "outlook"
            && name is "cmd" or "powershell" or "pwsh" or "wscript" or "cscript" or "mshta")
        {
            flags.Add(new SuspiciousPatternFlag
            {
                TechniqueId = "T1059.001",
                TechniqueName = "Command and Scripting Interpreter: PowerShell",
                Description = $"{parentName} spawned {name} — possible macro/phishing attack",
                Severity = "High"
            });
        }

        // T1027: Obfuscated PowerShell
        if (name is "powershell" or "pwsh" && cmdLine.Contains("-encodedcommand"))
        {
            flags.Add(new SuspiciousPatternFlag
            {
                TechniqueId = "T1027",
                TechniqueName = "Obfuscated Files or Information",
                Description = "PowerShell with -EncodedCommand (obfuscated payload)",
                Severity = "High"
            });
        }

        // T1059: Unsigned exe running from TEMP or APPDATA
        string? path = process.ExecutablePath?.ToLowerInvariant();
        if (path is not null && (path.Contains("\\temp\\") || path.Contains("\\appdata\\")))
        {
            string tempOrAppdata = path.Contains("\\temp\\") ? "%TEMP%" : "%APPDATA%";
            flags.Add(new SuspiciousPatternFlag
            {
                TechniqueId = "T1059",
                TechniqueName = "Command and Scripting Interpreter",
                Description = $"Executable running from {tempOrAppdata}: {process.ProcessName}",
                Severity = "Medium"
            });
        }
    }
}

/// <summary>
/// A suspicious process pattern mapped to a MITRE ATT and CK technique.
/// </summary>
public sealed record SuspiciousPatternFlag
{
    /// <summary>MITRE ATT and CK technique ID (e.g., "T1490").</summary>
    public required string TechniqueId { get; init; }

    /// <summary>MITRE technique name.</summary>
    public required string TechniqueName { get; init; }

    /// <summary>Human-readable description of what was detected.</summary>
    public required string Description { get; init; }

    /// <summary>Severity: Critical, High, Medium.</summary>
    public required string Severity { get; init; }
}
