using RansomGuard.Agent.Core.Configuration;
using RansomGuard.Agent.Core.Persistence.Entities;

namespace RansomGuard.Agent.Core.Detection.Entropy.DetectionRules;

/// <summary>
/// Rule 4: Suppresses alerts for file extensions that are legitimately high-entropy
/// (compressed archives, media files). Evaluated FIRST — if it fires, no other rules run.
/// </summary>
public sealed class ExtensionWhitelistRule : IEntropyRule
{
    private readonly HashSet<string> _whitelistedExtensions;

    /// <inheritdoc />
    public int RuleId => 4;

    /// <inheritdoc />
    public string Name => "ExtensionWhitelist";

    /// <summary>
    /// Initializes with the configured whitelist extensions.
    /// </summary>
    public ExtensionWhitelistRule(EntropyOptions options)
    {
        _whitelistedExtensions = new HashSet<string>(
            options.WhitelistedExtensions, StringComparer.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public EntropyAlert? Evaluate(EntropyEvaluationContext context)
    {
        // Return a special "suppression" marker — caller checks RuleId == 4
        if (_whitelistedExtensions.Contains(context.FileExtension))
        {
            return new EntropyAlert
            {
                FilePath = context.FilePath,
                RuleId = RuleId,
                RuleName = Name,
                Severity = "Suppressed",
                BaselineEntropy = context.Baseline?.EntropyValue ?? 0,
                CurrentEntropy = context.CurrentEntropy,
                Delta = 0
            };
        }

        return null;
    }
}
