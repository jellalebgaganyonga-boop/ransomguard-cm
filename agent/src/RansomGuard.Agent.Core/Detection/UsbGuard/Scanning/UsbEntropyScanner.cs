using Microsoft.Extensions.Logging;
using RansomGuard.Agent.Core.Detection.Entropy;

namespace RansomGuard.Agent.Core.Detection.UsbGuard.Scanning;

/// <summary>
/// Reuses Sprint 3 EntropyCalculator to detect encrypted content on USB devices.
/// Flags files with entropy > 7.9 that are not in the high-entropy whitelist.
/// </summary>
public sealed class UsbEntropyScanner
{
    private const double HighEntropyThreshold = 7.9;
    private const long MinFileSizeForScan = 100 * 1024; // 100 KB

    private static readonly HashSet<string> HighEntropyWhitelist = new(StringComparer.OrdinalIgnoreCase)
    {
        ".zip", ".7z", ".rar", ".gz", ".bz2", ".xz",
        ".jpg", ".jpeg", ".png", ".gif", ".webp",
        ".mp3", ".mp4", ".avi", ".mkv", ".flac", ".wav",
        ".iso", ".cab"
    };

    private readonly IEntropyCalculator _calculator;
    private readonly ILogger<UsbEntropyScanner> _logger;

    /// <summary>Initializes the USB entropy scanner.</summary>
    public UsbEntropyScanner(IEntropyCalculator calculator, ILogger<UsbEntropyScanner> logger)
    {
        _calculator = calculator;
        _logger = logger;
    }

    /// <summary>
    /// Checks if a file has suspiciously high entropy (possible encryption).
    /// </summary>
    /// <param name="filePath">Path to the file.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Finding if high entropy detected, null otherwise.</returns>
    public async Task<EntropyFinding?> ScanAsync(string filePath, CancellationToken ct = default)
    {
        try
        {
            var fileInfo = new FileInfo(filePath);
            if (!fileInfo.Exists || fileInfo.Length < MinFileSizeForScan) return null;

            string ext = Path.GetExtension(filePath).ToLowerInvariant();
            if (HighEntropyWhitelist.Contains(ext)) return null;

            double? entropy = await _calculator.ComputeFileEntropyAsync(filePath, ct);
            if (entropy is null || entropy.Value <= HighEntropyThreshold) return null;

            _logger.LogWarning("USB GUARD: High entropy {Entropy:F2} on {File}", entropy.Value, filePath);

            return new EntropyFinding
            {
                FilePath = filePath,
                Entropy = entropy.Value,
                FileExtension = ext,
                Severity = ScanSeverity.Medium
            };
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Entropy scan failed for {File}", filePath);
            return null;
        }
    }
}

/// <summary>
/// Finding from entropy scan.
/// </summary>
public sealed record EntropyFinding
{
    /// <summary>Path to the file.</summary>
    public required string FilePath { get; init; }

    /// <summary>Measured entropy value (bits/byte).</summary>
    public required double Entropy { get; init; }

    /// <summary>File extension.</summary>
    public required string FileExtension { get; init; }

    /// <summary>Severity (Medium for encrypted content on USB).</summary>
    public required ScanSeverity Severity { get; init; }
}
