using RansomGuard.Agent.Core.Detection.IronClad.Models;

namespace RansomGuard.Agent.Core.Detection.IronClad.Communication;

/// <summary>
/// IronClad communicator using System.IO.Ports.SerialPort for real Arduino hardware.
/// Sprint 8 implementation — all methods throw NotImplementedException in Sprint 5.
/// Class structure and constructor signature are finalized for DI registration.
/// </summary>
public sealed class SerialPortCommunicator : IIronCladCommunicator
{
    private readonly IronCladOptions _options;

    /// <summary>
    /// Initializes the serial port communicator with hardware configuration.
    /// </summary>
    /// <param name="options">IronClad options containing SerialPortName and SerialBaudRate.</param>
    public SerialPortCommunicator(IronCladOptions options)
    {
        _options = options;
    }

    /// <inheritdoc />
    public bool IsConnected => throw new NotImplementedException("Activated in Sprint 8 for real hardware deployment");

    /// <inheritdoc />
    public string? DeviceVersion => throw new NotImplementedException("Activated in Sprint 8 for real hardware deployment");

    /// <inheritdoc />
    public int PortCount => throw new NotImplementedException("Activated in Sprint 8 for real hardware deployment");

    /// <inheritdoc />
    public event EventHandler<HeartbeatEventArgs>? HeartbeatReceived;

    /// <inheritdoc />
    public event EventHandler<ConnectionLostEventArgs>? ConnectionLost;

    /// <inheritdoc />
    public Task<bool> ConnectAsync(CancellationToken cancellationToken = default)
        => throw new NotImplementedException("Activated in Sprint 8 for real hardware deployment");

    /// <inheritdoc />
    public Task DisconnectAsync(CancellationToken cancellationToken = default)
        => throw new NotImplementedException("Activated in Sprint 8 for real hardware deployment");

    /// <inheritdoc />
    public Task<IronCladResponse> SendCommandAsync(IronCladCommand command, CancellationToken cancellationToken = default)
        => throw new NotImplementedException("Activated in Sprint 8 for real hardware deployment");

    /// <inheritdoc />
    public Task<HealthStatus> CheckHealthAsync(CancellationToken cancellationToken = default)
        => throw new NotImplementedException("Activated in Sprint 8 for real hardware deployment");

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        // Suppress unused event warnings — events will be wired in Sprint 8
        _ = HeartbeatReceived;
        _ = ConnectionLost;
        return ValueTask.CompletedTask;
    }
}
