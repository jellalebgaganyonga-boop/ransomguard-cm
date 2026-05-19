using RansomGuard.Agent.Core.Detection.UsbGuard.Models;

namespace RansomGuard.Agent.Core.Detection.UsbGuard.Wmi;

/// <summary>
/// Subscribes to WMI events for USB PnP device and logical disk changes.
/// </summary>
public interface IWmiEventSubscriber : IAsyncDisposable
{
    /// <summary>
    /// Starts listening for WMI USB events and writing them to the provided channel.
    /// </summary>
    void Start(System.Threading.Channels.ChannelWriter<UsbConnectionEvent> writer, CancellationToken cancellationToken);

    /// <summary>
    /// Stops all WMI event watchers and releases resources.
    /// </summary>
    Task StopAsync();
}
