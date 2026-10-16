using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace RansomGuard.Agent.Core.Detection.CrossModule;

/// <summary>
/// In-memory implementation of <see cref="IDetectionEventBus"/>.
/// Publication is synchronous: there is no queue and no background dispatch.
/// <see cref="PublishAsync{TSignal}"/> invokes the subscribers of the signal type one after
/// the other, on the caller's thread, and awaits each one before moving to the next.
/// Nothing is buffered, so nothing can be dropped. A subscriber that throws is logged
/// and does not prevent the following subscribers from running; a slow subscriber,
/// however, delays both the remaining subscribers and the publisher (debt AGT-BUS-001).
/// </summary>
public sealed class InMemoryDetectionEventBus : IDetectionEventBus, IDisposable
{
    private readonly ConcurrentDictionary<Type, SubscriptionList> _subscriptions = new();
    private readonly ILogger<InMemoryDetectionEventBus> _logger;

    /// <summary>Initializes the in-memory detection event bus.</summary>
    public InMemoryDetectionEventBus(ILogger<InMemoryDetectionEventBus> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _subscriptions.Clear();
    }

    /// <inheritdoc />
    public async Task PublishAsync<TSignal>(TSignal signal, CancellationToken ct = default)
        where TSignal : IDetectionSignal
    {
        if (!_subscriptions.TryGetValue(typeof(TSignal), out var list))
            return;

        foreach (var sub in list.GetHandlers())
        {
            try
            {
                if (sub is Func<TSignal, CancellationToken, Task> typedHandler)
                {
                    await typedHandler(signal, ct);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Subscriber failed for signal {SignalType} ({SignalId})",
                    typeof(TSignal).Name, signal.SignalId);
            }
        }
    }

    /// <inheritdoc />
    public IDisposable Subscribe<TSignal>(Func<TSignal, CancellationToken, Task> handler)
        where TSignal : IDetectionSignal
    {
        var list = _subscriptions.GetOrAdd(typeof(TSignal), _ => new SubscriptionList());
        var subscription = new Subscription(list);
        list.Add(subscription, handler);
        return subscription;
    }

    /// <summary>Number of handlers currently stored, across all signal types (diagnostics and tests).</summary>
    internal int StoredHandlerCount => _subscriptions.Values.Sum(list => list.Count);

    /// <summary>
    /// Handlers of one signal type, keyed by their subscription. Unsubscribing removes the
    /// entry, so memory stays proportional to the live subscriptions: the agent runs for
    /// months without restarting. Keying by subscription rather than by delegate means the
    /// same handler subscribed twice is two subscriptions, and disposing one keeps the other.
    /// </summary>
    private sealed class SubscriptionList
    {
        private readonly ConcurrentDictionary<Subscription, Delegate> _handlers = new();

        public int Count => _handlers.Count;

        public void Add(Subscription subscription, Delegate handler) => _handlers.TryAdd(subscription, handler);

        public void Remove(Subscription subscription) => _handlers.TryRemove(subscription, out _);

        public IEnumerable<Delegate> GetHandlers() => _handlers.Values;
    }

    private sealed class Subscription : IDisposable
    {
        private readonly SubscriptionList _list;

        public Subscription(SubscriptionList list)
        {
            _list = list;
        }

        public void Dispose() => _list.Remove(this);
    }
}
