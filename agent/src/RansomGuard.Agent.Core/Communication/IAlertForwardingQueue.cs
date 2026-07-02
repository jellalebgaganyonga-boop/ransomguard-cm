using RansomGuard.Agent.Core.Communication.Models;

namespace RansomGuard.Agent.Core.Communication;

/// <summary>
/// Abstraction for the alert forwarding queue.
/// Modules enqueue pre-mapped AlertIngestRequest payloads for async delivery to GRID.
/// </summary>
public interface IAlertForwardingQueue
{
    /// <summary>
    /// Enqueue an alert for forwarding to GRID. Non-blocking fire-and-forget.
    /// Returns false if the queue is full (alert dropped — logged by caller).
    /// </summary>
    bool TryEnqueue(AlertIngestRequest request);
}
