namespace RansomGuard.Agent.Core.Detection.IndicatorRemoval;

/// <summary>
/// Whitelist of legitimate IT administration scripts and processes
/// that perform indicator-removal-like actions as part of normal operations.
/// Suppresses false positives without disabling detection.
/// </summary>
public static class ItWhitelist
{
    private static readonly HashSet<string> WhitelistedProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "sccm_agent.exe", "intune_agent.exe", "ccmexec.exe",
        "svchost.exe", "tiworker.exe", "trustedinstaller.exe",
        "mpcmdrun.exe", "windowsupdate.exe", "wuauclt.exe"
    };

    private static readonly string[] WhitelistedPathPrefixes =
    {
        @"C:\Windows\System32\",
        @"C:\Windows\SysWOW64\",
        @"C:\Program Files\Microsoft Configuration Manager\",
        @"C:\Program Files\Microsoft Intune\",
        @"C:\ProgramData\Microsoft\Windows Defender\"
    };

    private static readonly string[] WhitelistedCommandPatterns =
    {
        "schtasks /create",   // Creating tasks is legitimate, only deletion is suspicious
        "/backup",            // Backup operations, not clearing
        "wevtutil epl",       // Exporting logs, not clearing
        "wevtutil qe"         // Querying logs, not clearing
    };

    /// <summary>
    /// Returns true if the process + command line represents a legitimate IT operation.
    /// </summary>
    public static bool IsWhitelisted(string processName, string? executablePath, string? commandLine)
    {
        // Known IT management processes
        if (WhitelistedProcessNames.Contains(processName))
            return true;

        // Processes running from known IT management paths
        if (executablePath is not null)
        {
            foreach (var prefix in WhitelistedPathPrefixes)
            {
                if (executablePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        // Commands that look similar but are legitimate
        if (commandLine is not null)
        {
            foreach (var pattern in WhitelistedCommandPatterns)
            {
                if (commandLine.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        return false;
    }
}
