using Microsoft.Extensions.Logging;
using RansomGuard.Agent.Core.Security;

namespace RansomGuard.Agent.Core.Detection.UsbGuard.Scanning;

/// <summary>
/// Detects autorun.inf files at USB root that contain dangerous directives.
/// Flags open=, shellexecute=, and shell\open\command= as AutoRun malware vectors.
/// Legacy Windows XP/7 attack but residual risk on hospital systems.
/// </summary>
public sealed class AutorunInfDetector
{
    private static readonly string[] DangerousDirectives =
    [
        "open=",
        "shellexecute=",
        @"shell\open\command=",
        @"shell\explore\command=",
        @"shell\find\command="
    ];

    private readonly ILogger<AutorunInfDetector> _logger;

    /// <summary>Initializes the autorun.inf detector.</summary>
    public AutorunInfDetector(ILogger<AutorunInfDetector> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Checks if an autorun.inf file exists at the drive root and contains dangerous directives.
    /// </summary>
    /// <param name="drivePath">Root path of the USB drive (e.g., "E:\").</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Scan finding, or null if no autorun.inf or it's safe.</returns>
    public async Task<AutorunFinding?> DetectAsync(string drivePath, CancellationToken ct = default)
    {
        var pathResult = PathValidator.Validate(drivePath);
        if (!pathResult.IsValid) return null;

        string autorunPath = Path.Combine(pathResult.CanonicalPath!, "autorun.inf");
        if (!File.Exists(autorunPath)) return null;

        try
        {
            string content = await File.ReadAllTextAsync(autorunPath, ct);
            var foundDirectives = new List<string>();

            foreach (string line in content.Split('\n'))
            {
                string trimmed = line.Trim();
                foreach (string directive in DangerousDirectives)
                {
                    if (trimmed.StartsWith(directive, StringComparison.OrdinalIgnoreCase))
                    {
                        foundDirectives.Add(trimmed);
                    }
                }
            }

            if (foundDirectives.Count > 0)
            {
                _logger.LogCritical("USB GUARD: autorun.inf with dangerous directives on {Drive}: {Directives}",
                    drivePath, string.Join("; ", foundDirectives));

                return new AutorunFinding
                {
                    FilePath = autorunPath,
                    Directives = foundDirectives,
                    Severity = ScanSeverity.Critical
                };
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Cannot read autorun.inf from {Drive}", drivePath);
            return null;
        }
    }

    /// <summary>
    /// Analyzes autorun.inf content directly (for unit testing).
    /// </summary>
    public static AutorunFinding? AnalyzeContent(string content, string filePath)
    {
        var foundDirectives = new List<string>();

        foreach (string line in content.Split('\n'))
        {
            string trimmed = line.Trim();
            foreach (string directive in DangerousDirectives)
            {
                if (trimmed.StartsWith(directive, StringComparison.OrdinalIgnoreCase))
                    foundDirectives.Add(trimmed);
            }
        }

        if (foundDirectives.Count == 0) return null;

        return new AutorunFinding
        {
            FilePath = filePath,
            Directives = foundDirectives,
            Severity = ScanSeverity.Critical
        };
    }
}

/// <summary>
/// Finding from autorun.inf analysis.
/// </summary>
public sealed record AutorunFinding
{
    /// <summary>Path to the autorun.inf file.</summary>
    public required string FilePath { get; init; }

    /// <summary>List of dangerous directives found.</summary>
    public required IReadOnlyList<string> Directives { get; init; }

    /// <summary>Severity of the finding.</summary>
    public required ScanSeverity Severity { get; init; }
}
