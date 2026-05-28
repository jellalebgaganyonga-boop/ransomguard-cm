namespace RansomGuard.Agent.Core.Detection.IronClad.Models;

/// <summary>
/// Health status snapshot of the IronClad relay controller device.
/// </summary>
public sealed record HealthStatus
{
    /// <summary>Whether the agent is currently connected to the device.</summary>
    public required bool IsConnected { get; init; }

    /// <summary>Firmware version string, null if not yet queried.</summary>
    public required string? DeviceVersion { get; init; }

    /// <summary>Number of relay-controlled ports on the device.</summary>
    public required int PortCount { get; init; }

    /// <summary>Current state of each port, keyed by port number (1-based).</summary>
    public required IReadOnlyDictionary<int, PortState> PortStates { get; init; }

    /// <summary>UTC timestamp of the last successful heartbeat.</summary>
    public required DateTime LastHeartbeatAt { get; init; }

    /// <summary>Average round-trip latency over recent heartbeats.</summary>
    public required TimeSpan AverageLatency { get; init; }

    /// <summary>Number of consecutive missed heartbeats since last successful one.</summary>
    public required long MissedHeartbeats { get; init; }
}
