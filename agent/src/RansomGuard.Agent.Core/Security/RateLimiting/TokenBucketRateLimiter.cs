using System.Threading.RateLimiting;

namespace RansomGuard.Agent.Core.Security.RateLimiting;

/// <summary>
/// Token bucket rate limiter wrapping the .NET 8 BCL System.Threading.RateLimiting primitive.
/// Provides a reusable, thread-safe rate limiter with configurable throughput and burst capacity.
/// </summary>
public sealed class TokenBucketRateLimiter : IOperationRateLimiter, IDisposable
{
    private readonly System.Threading.RateLimiting.TokenBucketRateLimiter _inner;

    /// <summary>
    /// Creates a new token bucket rate limiter.
    /// </summary>
    /// <param name="operationsPerSecond">Target sustained throughput.</param>
    /// <param name="burstCapacity">Maximum burst size. Defaults to operationsPerSecond.</param>
    public TokenBucketRateLimiter(int operationsPerSecond, int burstCapacity = 0)
    {
        if (operationsPerSecond <= 0)
            throw new ArgumentOutOfRangeException(nameof(operationsPerSecond), "Must be positive");

        if (burstCapacity <= 0) burstCapacity = operationsPerSecond;
        OperationsPerSecond = operationsPerSecond;

        _inner = new System.Threading.RateLimiting.TokenBucketRateLimiter(
            new TokenBucketRateLimiterOptions
            {
                TokenLimit = burstCapacity,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = burstCapacity * 10,
                ReplenishmentPeriod = TimeSpan.FromMilliseconds(1000.0 / operationsPerSecond),
                TokensPerPeriod = 1,
                AutoReplenishment = true
            });
    }

    /// <inheritdoc />
    public async ValueTask<bool> AcquireAsync(CancellationToken cancellationToken = default)
    {
        using var lease = await _inner.AcquireAsync(1, cancellationToken);
        return lease.IsAcquired;
    }

    /// <inheritdoc />
    public bool TryAcquire()
    {
        using var lease = _inner.AttemptAcquire(1);
        return lease.IsAcquired;
    }

    /// <inheritdoc />
    public int AvailableTokens => (int)(_inner.GetStatistics()?.CurrentAvailablePermits ?? 0);

    /// <inheritdoc />
    public int OperationsPerSecond { get; }

    /// <summary>
    /// Releases all resources used by the rate limiter.
    /// </summary>
    public void Dispose() => _inner.Dispose();
}
