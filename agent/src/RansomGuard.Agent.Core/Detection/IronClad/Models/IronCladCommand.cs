namespace RansomGuard.Agent.Core.Detection.IronClad.Models;

/// <summary>
/// Command sent from the agent to the IronClad relay controller.
/// </summary>
public sealed record IronCladCommand
{
    /// <summary>Unique command identifier for correlation with responses.</summary>
    public required Guid Id { get; init; }

    /// <summary>Action to execute on the relay controller.</summary>
    public required IronCladAction Action { get; init; }

    /// <summary>Action parameter (e.g., port number for CutPort/RestorePort).</summary>
    public required string Parameter { get; init; }

    /// <summary>UTC timestamp when the command was issued.</summary>
    public required DateTime IssuedAt { get; init; }

    /// <summary>Maximum time to wait for a response before timing out.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(10);
}

/// <summary>
/// Actions supported by the IronClad relay controller.
/// </summary>
public enum IronCladAction
{
    /// <summary>Cut power to a specific USB port relay.</summary>
    CutPort,

    /// <summary>Restore power to a specific USB port relay.</summary>
    RestorePort,

    /// <summary>Cut power to all USB port relays.</summary>
    CutAll,

    /// <summary>Restore power to all USB port relays.</summary>
    RestoreAll,

    /// <summary>Query current relay state for all ports.</summary>
    Status,

    /// <summary>Query firmware version.</summary>
    Version,

    /// <summary>Liveness check.</summary>
    Heartbeat
}
