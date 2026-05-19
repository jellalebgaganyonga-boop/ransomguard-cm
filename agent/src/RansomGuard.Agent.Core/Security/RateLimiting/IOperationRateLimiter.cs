namespace RansomGuard.Agent.Core.Security.RateLimiting;

/// <summary>
/// Token bucket rate limiter for controlling operation throughput.
/// Used to prevent resource exhaustion in detection pipelines.
/// </summary>
public interface IOperationRateLimiter
{
    /// <summary>
    /// Acquires permission to perform one operation. Blocks until token available or cancellation.
    /// </summary>
    ValueTask<bool> AcquireAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Tries to acquire without waiting. Returns true if token available immediately.
    /// </summary>
    bool TryAcquire();

    /// <summary>
    /// Current token count available.
    /// </summary>
    int AvailableTokens { get; }

    /// <summary>
    /// Rate in operations per second.
    /// </summary>
    int OperationsPerSecond { get; }
}
