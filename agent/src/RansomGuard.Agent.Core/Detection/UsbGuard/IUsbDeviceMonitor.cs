using RansomGuard.Agent.Core.Detection.UsbGuard.Models;

namespace RansomGuard.Agent.Core.Detection.UsbGuard;

/// <summary>
/// Monitors USB device connections and disconnections in real-time.
/// Events are delivered via a bounded channel for async processing.
/// </summary>
public interface IUsbDeviceMonitor
{
    /// <summary>
    /// Gets the count of currently connected USB devices being tracked.
    /// </summary>
    int ConnectedDeviceCount { get; }

    /// <summary>
    /// Gets the total number of events processed since startup.
    /// </summary>
    long TotalEventsProcessed { get; }
}
