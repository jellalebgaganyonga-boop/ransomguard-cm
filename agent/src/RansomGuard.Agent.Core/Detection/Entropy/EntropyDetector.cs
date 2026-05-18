using RansomGuard.Agent.Core.Configuration;
using RansomGuard.Agent.Core.Persistence.Entities;

namespace RansomGuard.Agent.Core.Detection.Entropy;

/// <summary>
/// Multi-signal entropy detection engine. Analyzes file entropy against baselines
/// and fires alerts based on 4 rules:
/// 1. Absolute high entropy on text-like file
/// 2. Sudden entropy delta from baseline
/// 3. Directory-wide entropy shift (mass encryption)
/// 4. Whitelist suppression for legitimate high-entropy formats
/// </summary>
public sealed class EntropyDetector
{
    private readonly EntropyOptions _options;

    /// <summary>
    /// Initializes the detector with configuration.
    /// </summary>
    public EntropyDetector(EntropyOptions options)
    {
        _options = options;
    }

    /// <summary>
    /// Analyzes a file's current entropy against its baseline.
    /// Returns an alert if any detection rule fires, or null if benign.
    /// </summary>
    /// <param name="filePath">Path to the file.</param>
    /// <param name="currentEntropy">Current entropy measurement.</param>
    /// <param name="baseline">Previous baseline measurement, or null if no baseline.</param>
    /// <returns>EntropyAlert if detection rule fired, null otherwise.</returns>
    public EntropyAlert? Analyze(string filePath, double currentEntropy, EntropyBaseline? baseline)
    {
        string extension = Path.GetExtension(filePath).ToLowerInvariant();

        // Rule 4: Whitelist — skip legitimately high-entropy formats
        if (_options.WhitelistedExtensions.Contains(extension))
        {
            return null;
        }

        double baselineEntropy = baseline?.EntropyValue ?? 0.0;
        double delta = currentEntropy - baselineEntropy;

        // Rule 1: Absolute high entropy on susceptible file
        if (_options.SusceptibleExtensions.Contains(extension)
            && currentEntropy > _options.AbsoluteThreshold
            && baselineEntropy < 6.0)
        {
            return CreateAlert(filePath, 1, "AbsoluteHighEntropy", "Critical",
                baselineEntropy, currentEntropy, delta);
        }

        // Rule 2: Sudden entropy delta
        if (baseline is not null
            && delta > _options.DeltaThreshold
            && currentEntropy > 7.0)
        {
            return CreateAlert(filePath, 2, "SuddenEntropyDelta", "High",
                baselineEntropy, currentEntropy, delta);
        }

        return null;
    }

    /// <summary>
    /// Analyzes directory-wide entropy shift for mass encryption detection (Rule 3).
    /// Called when multiple files in a directory have changed recently.
    /// </summary>
    /// <param name="directoryPath">Directory being analyzed.</param>
    /// <param name="recentAlerts">Number of entropy alerts in this directory within the time window.</param>
    /// <param name="averageDelta">Average entropy delta across affected files.</param>
    /// <returns>Critical alert if mass encryption detected, null otherwise.</returns>
    public EntropyAlert? AnalyzeDirectoryShift(string directoryPath, int recentAlerts, double averageDelta)
    {
        if (recentAlerts >= _options.DirectoryShiftMinFiles
            && averageDelta > _options.DirectoryShiftThreshold)
        {
            return CreateAlert(directoryPath, 3, "DirectoryWideEntropyShift", "Critical",
                0.0, averageDelta, averageDelta);
        }

        return null;
    }

    private static EntropyAlert CreateAlert(string filePath, int ruleId, string ruleName,
        string severity, double baseline, double current, double delta) => new()
    {
        FilePath = filePath,
        RuleId = ruleId,
        RuleName = ruleName,
        Severity = severity,
        BaselineEntropy = baseline,
        CurrentEntropy = current,
        Delta = delta
    };
}
