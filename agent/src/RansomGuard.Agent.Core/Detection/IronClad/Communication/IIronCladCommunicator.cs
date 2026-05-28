using RansomGuard.Agent.Core.Detection.IronClad.Models;

namespace RansomGuard.Agent.Core.Detection.IronClad.Communication;

/// <summary>
/// Abstraction for communication with the IronClad relay controller.
/// Sprint 5: TCP mock (localhost:9999). Sprint 8: SerialPort to real Arduino.
/// </summary>
public interface IIronCladCommunicator : IAsyncDisposable
{
    /// <summary>Whether the communicator has an active connection to the device.</summary>
    bool IsConnected { get; }

    /// <summary>Firmware version reported by the device, null until first VERSION query.</summary>
    string? DeviceVersion { get; }

    /// <summary>Number of relay ports on the connected device.</summary>
    int PortCount { get; }

    /// <summary>Raised when a heartbeat response is received from the device.</summary>
    event EventHandler<HeartbeatEventArgs>? HeartbeatReceived;

    /// <summary>Raised when the connection to the device is lost.</summary>
    event EventHandler<ConnectionLostEventArgs>? ConnectionLost;

    /// <summary>Establishes a connection to the IronClad device.</summary>
    Task<bool> ConnectAsync(CancellationToken cancellationToken = default);

    /// <summary>Gracefully disconnects from the device.</summary>
    Task DisconnectAsync(CancellationToken cancellationToken = default);

    /// <summary>Sends a command and awaits the device response.</summary>
    Task<IronCladResponse> SendCommandAsync(IronCladCommand command, CancellationToken cancellationToken = default);

    /// <summary>Queries the device for a full health status snapshot.</summary>
    Task<HealthStatus> CheckHealthAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Event data for heartbeat received events.
/// </summary>
public sealed record HeartbeatEventArgs
{
    /// <summary>UTC timestamp of the heartbeat.</summary>
    public required DateTime Timestamp { get; init; }

    /// <summary>Round-trip latency measured for this heartbeat.</summary>
    public required TimeSpan Latency { get; init; }
}

/// <summary>
/// Event data for connection lost events.
/// </summary>
public sealed record ConnectionLostEventArgs
{
    /// <summary>UTC timestamp when the connection was lost.</summary>
    public required DateTime DisconnectedAt { get; init; }

    /// <summary>Reason for the disconnection.</summary>
    public required string Reason { get; init; }
}
