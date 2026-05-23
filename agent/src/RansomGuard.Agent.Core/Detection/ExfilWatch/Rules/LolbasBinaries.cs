namespace RansomGuard.Agent.Core.Detection.ExfilWatch.Rules;

/// <summary>
/// Living-Off-the-Land Binaries (LOLBAS) — legitimate Windows executables
/// frequently abused by ransomware and APTs for data exfiltration.
/// 30 binaries per Sprint 4 specification.
/// </summary>
public static class LolbasBinaries
{
    /// <summary>Set of 30 LOLBAS binary names (case-insensitive).</summary>
    public static readonly IReadOnlyCollection<string> Names = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "certutil.exe", "bitsadmin.exe", "wmic.exe", "regsvr32.exe", "mshta.exe",
        "rundll32.exe", "msbuild.exe", "installutil.exe", "regasm.exe", "regsvcs.exe",
        "csc.exe", "msxsl.exe", "xwizard.exe", "esentutl.exe", "extexport.exe",
        "extrac32.exe", "findstr.exe", "hh.exe", "ieexec.exe", "imewdbld.exe",
        "makecab.exe", "msconfig.exe", "msdt.exe", "odbcconf.exe", "pcalua.exe",
        "presentationhost.exe", "print.exe", "psr.exe", "scriptrunner.exe",
        "syncappvpublishingserver.exe"
    };
}
