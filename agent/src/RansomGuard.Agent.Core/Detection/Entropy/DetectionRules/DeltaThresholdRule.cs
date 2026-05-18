using RansomGuard.Agent.Core.Configuration;
using RansomGuard.Agent.Core.Persistence.Entities;

namespace RansomGuard.Agent.Core.Detection.Entropy.DetectionRules;

/// <summary>
/// Rule 2: Fires when entropy delta from baseline exceeds threshold
/// AND current entropy is above 7.0 (confirms encryption, not just noise).
/// </summary>
public sealed class DeltaThresholdRule : IEntropyRule
{
    private readonly double _deltaThreshold;

    /// <inheritdoc />
    public int RuleId => 2;

    /// <inheritdoc />
    public string Name => "SuddenEntropyDelta";

    /// <summary>
    /// Initializes with configured delta threshold.
    /// </summary>
    public DeltaThresholdRule(EntropyOptions options)
    {
        _deltaThreshold = options.DeltaThreshold;
    }

    /// <inheritdoc />
    public EntropyAlert? Evaluate(EntropyEvaluationContext context)
    {
        if (context.Baseline is null) return null;

        double delta = context.CurrentEntropy - context.Baseline.EntropyValue;

        if (delta > _deltaThreshold && context.CurrentEntropy > 7.0)
        {
            return new EntropyAlert
            {
                FilePath = context.FilePath,
                RuleId = RuleId,
                RuleName = Name,
                Severity = "High",
                BaselineEntropy = context.Baseline.EntropyValue,
                CurrentEntropy = context.CurrentEntropy,
                Delta = delta
            };
        }

        return null;
    }
}
