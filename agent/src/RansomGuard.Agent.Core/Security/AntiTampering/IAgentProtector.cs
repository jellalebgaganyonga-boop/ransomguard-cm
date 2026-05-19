namespace RansomGuard.Agent.Core.Security.AntiTampering;

/// <summary>
/// Protects the agent process against tampering by malware.
/// Composes multiple sub-protectors: registry watcher, code integrity, debugger detection.
/// </summary>
public interface IAgentProtector
{
    /// <summary>
    /// Starts all protection monitors.
    /// </summary>
    Task StartProtectionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs an immediate integrity check across all sub-protectors.
    /// </summary>
    Task<TamperingStatus> CheckIntegrityAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of a tampering integrity check.
/// </summary>
public sealed record TamperingStatus
{
    /// <summary>Whether the agent is intact (no tampering detected).</summary>
    public required bool IsIntact { get; init; }

    /// <summary>List of tampering indicators found.</summary>
    public required IReadOnlyList<string> TamperingIndicators { get; init; }

    /// <summary>UTC timestamp of the check.</summary>
    public required DateTime CheckedAt { get; init; }
}
