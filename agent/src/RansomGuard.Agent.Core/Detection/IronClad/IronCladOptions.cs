namespace RansomGuard.Agent.Core.Detection.IronClad;

/// <summary>
/// Configuration options for the IronClad hardware response module.
/// Default: disabled, TCP mock mode, fail-safe restore on disconnect.
/// </summary>
public sealed class IronCladOptions
{
    /// <summary>Whether IronClad is enabled. OFF by default — admin opts in after whitelist building.</summary>
    public bool Enabled { get; init; } = false;

    /// <summary>Communication backend: TcpMock (Sprint 5) or SerialPort (Sprint 8).</summary>
    public IronCladCommunicationMode CommunicationMode { get; init; } = IronCladCommunicationMode.TcpMock;

    /// <summary>TCP endpoint for mock Arduino server (host:port).</summary>
    public string TcpMockEndpoint { get; init; } = "localhost:9999";

    /// <summary>Serial port name for real Arduino hardware (e.g., "COM3"). Sprint 8.</summary>
    public string? SerialPortName { get; init; }

    /// <summary>Serial port baud rate for real Arduino hardware.</summary>
    public int SerialBaudRate { get; init; } = 9600;

    /// <summary>Number of relay-controlled USB ports on the device.</summary>
    public int RelayCount { get; init; } = 4;

    /// <summary>Timeout in seconds for individual commands sent to the device.</summary>
    public int CommandTimeoutSeconds { get; init; } = 10;

    /// <summary>Interval in seconds between heartbeat probes.</summary>
    public int HeartbeatIntervalSeconds { get; init; } = 5;

    /// <summary>Seconds without heartbeat before declaring device unreachable.</summary>
    public int HeartbeatTimeoutSeconds { get; init; } = 30;

    /// <summary>Whether to restore all ports to Active if the device connection is lost.</summary>
    public bool FailSafeRestoreOnDisconnect { get; init; } = true;

    /// <summary>Whether to require a connected device before allowing Critical actions.</summary>
    public bool RequireDeviceForCriticalActions { get; init; } = false;
}

/// <summary>
/// Communication backend mode for IronClad.
/// </summary>
public enum IronCladCommunicationMode
{
    /// <summary>TCP socket to MockArduinoServer (Sprint 5 software-only).</summary>
    TcpMock,

    /// <summary>Serial port to real Arduino hardware (Sprint 8).</summary>
    SerialPort
}
