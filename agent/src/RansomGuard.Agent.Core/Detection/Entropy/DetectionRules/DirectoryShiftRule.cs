using RansomGuard.Agent.Core.Configuration;
using RansomGuard.Agent.Core.Persistence.Entities;

namespace RansomGuard.Agent.Core.Detection.Entropy.DetectionRules;

/// <summary>
/// Rule 3: Fires when multiple files in the same directory show entropy increase
/// within a short time window. This is the strongest ransomware indicator —
/// mass encryption of a directory.
/// </summary>
public sealed class DirectoryShiftRule : IEntropyRule
{
    private readonly double _shiftThreshold;
    private readonly int _minFiles;

    /// <inheritdoc />
    public int RuleId => 3;

    /// <inheritdoc />
    public string Name => "DirectoryWideEntropyShift";

    /// <summary>
    /// Initializes with configured shift parameters.
    /// </summary>
    public DirectoryShiftRule(EntropyOptions options)
    {
        _shiftThreshold = options.DirectoryShiftThreshold;
        _minFiles = options.DirectoryShiftMinFiles;
    }

    /// <inheritdoc />
    public EntropyAlert? Evaluate(EntropyEvaluationContext context)
    {
        if (context.RecentDirectoryAlertCount >= _minFiles
            && context.AverageDirectoryDelta > _shiftThreshold)
        {
            return new EntropyAlert
            {
                FilePath = context.FilePath,
                RuleId = RuleId,
                RuleName = Name,
                Severity = "Critical",
                BaselineEntropy = 0,
                CurrentEntropy = context.AverageDirectoryDelta,
                Delta = context.AverageDirectoryDelta
            };
        }

        return null;
    }
}
