using Microsoft.Extensions.Logging;
using RansomGuard.Agent.Core.Configuration;
using RansomGuard.Agent.Core.Detection.ExfilWatch.Models;

namespace RansomGuard.Agent.Core.Detection.ExfilWatch.Rules;

/// <summary>
/// Evaluates all EXFIL WATCH detection rules against a network event.
/// Returns all findings (a single event may trigger multiple rules).
/// </summary>
public sealed class ExfilRuleEngine
{
    private readonly IReadOnlyList<IExfilDetectionRule> _rules;
    private readonly ILogger<ExfilRuleEngine> _logger;

    /// <summary>Initializes the rule engine with all 8 detection rules.</summary>
    public ExfilRuleEngine(ILogger<ExfilRuleEngine> logger)
    {
        _logger = logger;
        _rules =
        [
            new VolumeAnomalyRule(),
            new DnsTunnelingRule(),
            new AfterHoursExfilRule(),
            new CloudUploadSpikeRule(),
            new BeaconingRule(),
            new RareDestinationRule(),
            new DestinationSprayRule(),
            new LargeSingleTransferRule()
        ];
    }

    /// <summary>Number of registered rules.</summary>
    public int RuleCount => _rules.Count;

    /// <summary>
    /// Evaluates all rules against the given event.
    /// </summary>
    public IReadOnlyList<ExfilFinding> Evaluate(
        NetworkEvent networkEvent, IDataVolumeTracker tracker, ExfilWatchOptions options)
    {
        var findings = new List<ExfilFinding>();

        foreach (var rule in _rules)
        {
            try
            {
                var finding = rule.Evaluate(networkEvent, tracker, options);
                if (finding is not null)
                {
                    findings.Add(finding);
                    _logger.LogWarning("EXFIL WATCH [{Rule}]: {Description}",
                        finding.RuleName, finding.Description);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "EXFIL WATCH: Rule {Rule} threw exception", rule.RuleName);
            }
        }

        return findings;
    }
}
