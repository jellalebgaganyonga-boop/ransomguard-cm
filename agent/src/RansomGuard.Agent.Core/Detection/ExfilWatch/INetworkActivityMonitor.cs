using System.Threading.Channels;
using RansomGuard.Agent.Core.Detection.ExfilWatch.Models;

namespace RansomGuard.Agent.Core.Detection.ExfilWatch;

/// <summary>
/// Captures network activity events from the operating system (ETW, socket tracing).
/// Events are written to a Channel for async consumption by the detection pipeline.
/// </summary>
public interface INetworkActivityMonitor
{
    /// <summary>
    /// Starts capturing network events and writing them to the provided channel.
    /// </summary>
    /// <param name="writer">Channel writer for captured events.</param>
    /// <param name="cancellationToken">Token to stop capture.</param>
    void Start(ChannelWriter<NetworkEvent> writer, CancellationToken cancellationToken);

    /// <summary>
    /// Stops the capture session and releases ETW resources.
    /// </summary>
    Task StopAsync();

    /// <summary>
    /// Whether the monitor is currently capturing events.
    /// </summary>
    bool IsCapturing { get; }

    /// <summary>
    /// Total events captured since start.
    /// </summary>
    long TotalEventsCaptured { get; }
}
