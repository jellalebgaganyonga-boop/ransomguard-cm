using RansomGuard.Agent.Core.Detection.ExfilWatch.Rules;

namespace RansomGuard.Agent.Core.Detection.ExfilWatch.Actions;

/// <summary>
/// Interface for an individual exfiltration response action.
/// Actions are executed by <see cref="IExfilActionEngine"/> based on the severity decision matrix.
/// </summary>
public interface IExfilAction
{
    /// <summary>Action name for audit logging.</summary>
    string ActionName { get; }

    /// <summary>
    /// Executes the action in response to an exfiltration finding.
    /// </summary>
    Task<ExfilActionResult> ExecuteAsync(ExfilFinding finding, CancellationToken ct);
}

/// <summary>
/// Result of an exfiltration action execution attempt.
/// </summary>
public sealed record ExfilActionResult
{
    /// <summary>Whether the action completed successfully.</summary>
    public required bool Success { get; init; }

    /// <summary>Name of the action that was attempted.</summary>
    public required string ActionName { get; init; }

    /// <summary>Human-readable details of the action result.</summary>
    public required string Details { get; init; }
}
