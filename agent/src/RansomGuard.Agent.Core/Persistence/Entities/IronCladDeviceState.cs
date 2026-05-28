namespace RansomGuard.Agent.Core.Persistence.Entities;

/// <summary>
/// Persisted state of a single IronClad relay port.
/// Tracks the last known state for reconciliation on agent restart.
/// </summary>
public sealed class IronCladDeviceState
{
    /// <summary>Unique record identifier.</summary>
    public required Guid Id { get; init; }

    /// <summary>Port number (1-based).</summary>
    public required int PortNumber { get; init; }

    /// <summary>Current relay state (stored as string).</summary>
    public required string State { get; set; }

    /// <summary>UTC timestamp of the last state change.</summary>
    public required DateTime LastChangedAt { get; set; }

    /// <summary>Event ID that caused this state change.</summary>
    public Guid? LastEventId { get; set; }

    /// <summary>Reason for the current state.</summary>
    public required string Reason { get; set; }
}
