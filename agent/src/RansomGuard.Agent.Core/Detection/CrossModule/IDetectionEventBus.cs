namespace RansomGuard.Agent.Core.Detection.CrossModule;

/// <summary>
/// Cross-module pub/sub event bus for detection signal correlation.
/// Enables modules like ENTROPY and EXFIL WATCH to share signals without direct coupling.
/// </summary>
public interface IDetectionEventBus
{
    /// <summary>
    /// Publishes a detection signal to all subscribers of the signal type.
    /// Fire-and-forget: subscriber failures are logged but do not block the publisher.
    /// </summary>
    Task PublishAsync<TSignal>(TSignal signal, CancellationToken ct = default)
        where TSignal : IDetectionSignal;

    /// <summary>
    /// Subscribes to a signal type. Returns a disposable to unsubscribe.
    /// </summary>
    IDisposable Subscribe<TSignal>(Func<TSignal, CancellationToken, Task> handler)
        where TSignal : IDetectionSignal;
}

/// <summary>
/// Marker interface for detection signals published on the event bus.
/// </summary>
public interface IDetectionSignal
{
    /// <summary>Unique signal identifier.</summary>
    Guid SignalId { get; }

    /// <summary>Source module name (e.g., "ENTROPY", "SENTINEL").</summary>
    string SourceModule { get; }

    /// <summary>UTC timestamp when the signal was emitted.</summary>
    DateTime EmittedAt { get; }
}
