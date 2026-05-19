using Microsoft.Extensions.Logging;

namespace RansomGuard.Agent.Core.Detection.UsbGuard.Scanning;

/// <summary>
/// Detects suspicious .lnk (Windows shortcut) files that target dangerous executables
/// or contain URL-based targets. Stuxnet-style attack vector.
/// Parses ShellLinkHeader (76 bytes) per MS-SHLLINK specification.
/// </summary>
public sealed class SuspiciousLnkDetector
{
    private static readonly HashSet<string> DangerousTargets = new(StringComparer.OrdinalIgnoreCase)
    {
        "cmd.exe", "powershell.exe", "pwsh.exe", "mshta.exe",
        "rundll32.exe", "regsvr32.exe", "wscript.exe", "cscript.exe",
        "certutil.exe", "bitsadmin.exe"
    };

    private readonly ILogger<SuspiciousLnkDetector> _logger;

    /// <summary>Initializes the LNK detector.</summary>
    public SuspiciousLnkDetector(ILogger<SuspiciousLnkDetector> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Analyzes a .lnk file for suspicious target patterns.
    /// </summary>
    /// <param name="filePath">Path to the .lnk file.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Finding if suspicious, null otherwise.</returns>
    public async Task<LnkFinding?> AnalyzeAsync(string filePath, CancellationToken ct = default)
    {
        try
        {
            byte[] content = await File.ReadAllBytesAsync(filePath, ct);
            return AnalyzeBytes(content, filePath);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Cannot read LNK file: {FilePath}", filePath);
            return null;
        }
    }

    /// <summary>
    /// Analyzes LNK bytes for suspicious patterns (for unit testing).
    /// </summary>
    public static LnkFinding? AnalyzeBytes(ReadOnlySpan<byte> content, string filePath)
    {
        // LNK must start with 0x4C000000 (76-byte header class ID)
        if (content.Length < 76 || content[0] != 0x4C || content[1] != 0x00 ||
            content[2] != 0x00 || content[3] != 0x00)
            return null;

        // Extract readable strings from the LNK for target analysis
        string text = ExtractStrings(content);
        string textLower = text.ToLowerInvariant();

        var reasons = new List<string>();

        // Check for dangerous target executables
        foreach (string target in DangerousTargets)
        {
            if (textLower.Contains(target, StringComparison.OrdinalIgnoreCase))
                reasons.Add($"Targets {target}");
        }

        // Check for URL-based targets
        if (textLower.Contains("http://") || textLower.Contains("https://") || textLower.Contains("file://"))
            reasons.Add("Contains URL target");

        if (reasons.Count == 0) return null;

        return new LnkFinding
        {
            FilePath = filePath,
            Reasons = reasons,
            Severity = reasons.Any(r => r.Contains("cmd.exe") || r.Contains("powershell"))
                ? ScanSeverity.Critical
                : ScanSeverity.High
        };
    }

    private static string ExtractStrings(ReadOnlySpan<byte> data)
    {
        // Extract ASCII strings of length >= 4 from binary data
        var sb = new System.Text.StringBuilder();
        int run = 0;
        foreach (byte b in data)
        {
            if (b >= 0x20 && b < 0x7F)
            {
                sb.Append((char)b);
                run++;
            }
            else
            {
                if (run >= 4) sb.Append(' ');
                run = 0;
            }
        }
        return sb.ToString();
    }
}

/// <summary>
/// Finding from LNK file analysis.
/// </summary>
public sealed record LnkFinding
{
    /// <summary>Path to the LNK file.</summary>
    public required string FilePath { get; init; }

    /// <summary>Reasons the LNK is suspicious.</summary>
    public required IReadOnlyList<string> Reasons { get; init; }

    /// <summary>Severity of the finding.</summary>
    public required ScanSeverity Severity { get; init; }
}
