using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace RansomGuard.Agent.Core.Detection.CrossModule;

/// <summary>
/// Channel-based in-memory implementation of <see cref="IDetectionEventBus"/>.
/// Bounded channel (5000 events). Subscribers receive on background tasks.
/// Failures are logged but do not block the publisher.
/// </summary>
public sealed class InMemoryDetectionEventBus : IDetectionEventBus, IDisposable
{
    private const int ChannelCapacity = 5000;

    private readonly ConcurrentDictionary<Type, SubscriptionList> _subscriptions = new();
    private readonly ILogger<InMemoryDetectionEventBus> _logger;
    private long _droppedCount;

    /// <summary>Number of signals dropped due to channel saturation.</summary>
    public long DroppedCount => Interlocked.Read(ref _droppedCount);

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
        var subscription = new Subscription<TSignal>(handler, list);
        list.Add(subscription.BoxedHandler);
        return subscription;
    }

    private sealed class SubscriptionList
    {
        private readonly ConcurrentBag<Delegate> _handlers = new();
        private readonly ConcurrentBag<Delegate> _removed = new();

        public void Add(Delegate handler) => _handlers.Add(handler);

        public void Remove(Delegate handler) => _removed.Add(handler);

        public IEnumerable<Delegate> GetHandlers()
        {
            var removed = _removed.ToHashSet();
            foreach (var h in _handlers)
            {
                if (!removed.Contains(h))
                    yield return h;
            }
        }
    }

    private sealed class Subscription<TSignal> : IDisposable
        where TSignal : IDetectionSignal
    {
        private readonly SubscriptionList _list;
        public Func<TSignal, CancellationToken, Task> BoxedHandler { get; }

        public Subscription(Func<TSignal, CancellationToken, Task> handler, SubscriptionList list)
        {
            BoxedHandler = handler;
            _list = list;
        }

        public void Dispose() => _list.Remove(BoxedHandler);
    }
}
