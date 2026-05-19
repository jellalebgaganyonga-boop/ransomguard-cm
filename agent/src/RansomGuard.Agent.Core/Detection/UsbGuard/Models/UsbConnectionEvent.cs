namespace RansomGuard.Agent.Core.Detection.UsbGuard.Models;

/// <summary>
/// Represents a USB connection lifecycle event captured by WMI.
/// </summary>
public sealed record UsbConnectionEvent
{
    /// <summary>Unique event identifier.</summary>
    public required Guid Id { get; init; }

    /// <summary>Type of connection event (Connected, Disconnected, Mounted, Unmounted).</summary>
    public required UsbConnectionEventType EventType { get; init; }

    /// <summary>The USB device associated with this event.</summary>
    public required UsbDevice Device { get; init; }

    /// <summary>UTC timestamp when the event occurred.</summary>
    public required DateTime OccurredAt { get; init; }
}

/// <summary>
/// Type of USB connection event.
/// </summary>
public enum UsbConnectionEventType
{
    /// <summary>USB device physically connected.</summary>
    Connected,
    /// <summary>USB device physically disconnected.</summary>
    Disconnected,
    /// <summary>Removable disk drive letter assigned.</summary>
    Mounted,
    /// <summary>Removable disk drive letter removed.</summary>
    Unmounted
}
