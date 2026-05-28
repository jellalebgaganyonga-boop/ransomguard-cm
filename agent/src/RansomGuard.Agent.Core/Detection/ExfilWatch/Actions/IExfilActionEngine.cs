using RansomGuard.Agent.Core.Detection.ExfilWatch.Rules;

namespace RansomGuard.Agent.Core.Detection.ExfilWatch.Actions;

/// <summary>
/// Engine that selects and executes exfiltration response actions based on
/// severity decision matrix. Handles cloud whitelist suppression, timeouts,
/// graceful fallback, and audit logging.
/// </summary>
public interface IExfilActionEngine
{
    /// <summary>
    /// Evaluates and executes appropriate response actions for an exfiltration finding.
    /// Decision matrix: Low/Medium → AlertOnly, High → Alert+Throttle, Critical → Alert+Throttle+Block.
    /// Cloud-whitelisted destinations suppressed unless over threshold.
    /// </summary>
    Task ExecuteAsync(ExfilFinding finding, CancellationToken ct);
}
