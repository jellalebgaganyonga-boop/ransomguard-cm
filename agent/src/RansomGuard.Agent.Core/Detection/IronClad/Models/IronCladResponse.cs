namespace RansomGuard.Agent.Core.Detection.IronClad.Models;

/// <summary>
/// Response received from the IronClad relay controller.
/// </summary>
public sealed record IronCladResponse
{
    /// <summary>Correlation ID matching the originating command.</summary>
    public required Guid CommandId { get; init; }

    /// <summary>Type of response.</summary>
    public required IronCladResponseType Type { get; init; }

    /// <summary>Raw wire payload as received from the device.</summary>
    public required string RawPayload { get; init; }

    /// <summary>UTC timestamp when the response was received.</summary>
    public required DateTime ReceivedAt { get; init; }

    /// <summary>Whether this response acknowledges a successful command execution.</summary>
    public required bool IsAcknowledgement { get; init; }

    /// <summary>Error code if response is an error (e.g., E001, E002).</summary>
    public string? ErrorCode { get; init; }

    /// <summary>Human-readable error message.</summary>
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// Types of responses from the IronClad relay controller.
/// </summary>
public enum IronCladResponseType
{
    /// <summary>Command acknowledged and executed successfully.</summary>
    Ack,

    /// <summary>Port status report.</summary>
    Status,

    /// <summary>Firmware version report.</summary>
    Version,

    /// <summary>Heartbeat response (pong).</summary>
    Pong,

    /// <summary>Error response with code and message.</summary>
    Error
}
