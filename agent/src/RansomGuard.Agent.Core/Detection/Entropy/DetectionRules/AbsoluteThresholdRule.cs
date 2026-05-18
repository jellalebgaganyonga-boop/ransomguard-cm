using RansomGuard.Agent.Core.Configuration;
using RansomGuard.Agent.Core.Persistence.Entities;

namespace RansomGuard.Agent.Core.Detection.Entropy.DetectionRules;

/// <summary>
/// Rule 1: Fires when a text-like file has absolute entropy above threshold
/// AND its baseline was below 6.0 (was not already encrypted).
/// Indicates fresh encryption of a previously readable document.
/// </summary>
public sealed class AbsoluteThresholdRule : IEntropyRule
{
    private readonly double _threshold;
    private readonly HashSet<string> _susceptibleExtensions;

    /// <inheritdoc />
    public int RuleId => 1;

    /// <inheritdoc />
    public string Name => "AbsoluteHighEntropy";

    /// <summary>
    /// Initializes with configured thresholds.
    /// </summary>
    public AbsoluteThresholdRule(EntropyOptions options)
    {
        _threshold = options.AbsoluteThreshold;
        _susceptibleExtensions = new HashSet<string>(
            options.SusceptibleExtensions, StringComparer.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public EntropyAlert? Evaluate(EntropyEvaluationContext context)
    {
        if (!_susceptibleExtensions.Contains(context.FileExtension))
        {
            return null;
        }

        double baselineEntropy = context.Baseline?.EntropyValue ?? 0.0;

        if (context.CurrentEntropy > _threshold && baselineEntropy < 6.0)
        {
            return new EntropyAlert
            {
                FilePath = context.FilePath,
                RuleId = RuleId,
                RuleName = Name,
                Severity = "Critical",
                BaselineEntropy = baselineEntropy,
                CurrentEntropy = context.CurrentEntropy,
                Delta = context.CurrentEntropy - baselineEntropy
            };
        }

        return null;
    }
}
