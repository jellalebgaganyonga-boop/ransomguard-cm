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

    /// <summary>
    /// Enqueues the attribution complement of an alert already enqueued (ADR-025). Same queue,
    /// same order, same persistence and retries as the alerts: it never overtakes its alert.
    /// </summary>
    bool TryEnqueueAttribution(string clientMessageId, AttributionComplementRequest complement);

    /// <summary>
    /// Queues the chain of processes behind the alert of this client_message_id. It goes through
    /// the same queue as the alert, so a GRID that is down holds it and it is retried, instead of
    /// the chain being the one thing an outage loses.
    /// </summary>
    bool TryEnqueueGenealogy(string clientMessageId, GenealogyComplementRequest complement);
}
