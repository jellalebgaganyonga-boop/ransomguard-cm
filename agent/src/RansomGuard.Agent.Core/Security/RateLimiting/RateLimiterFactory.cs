using System.Collections.Concurrent;

namespace RansomGuard.Agent.Core.Security.RateLimiting;

/// <summary>
/// Factory for named rate limiters. Provides singleton limiters per use case.
/// Register as singleton in DI.
/// </summary>
public sealed class RateLimiterFactory : IDisposable
{
    /// <summary>Rate limiter for entropy baseline builds (50 ops/sec).</summary>
    public const string EntropyBaselineBuild = "entropy-baseline-build";

    /// <summary>Rate limiter for entropy computation (100 ops/sec).</summary>
    public const string EntropyComputation = "entropy-computation";

    /// <summary>Rate limiter for genealogy enrichment (50 ops/sec).</summary>
    public const string GenealogyEnrichment = "genealogy-enrichment";

    /// <summary>Rate limiter for USB content scanning (50 files/sec).</summary>
    public const string UsbScan = "usb-scan";

    /// <summary>Rate limiter for ETW event processing (100 events/sec).</summary>
    public const string EtwProcessing = "etw-processing";

    private static readonly Dictionary<string, (int OpsPerSec, int Burst)> Defaults = new()
    {
        [EntropyBaselineBuild] = (50, 50),
        [EntropyComputation] = (100, 100),
        [GenealogyEnrichment] = (50, 50),
        [UsbScan] = (50, 50),
        [EtwProcessing] = (100, 100)
    };

    private readonly ConcurrentDictionary<string, TokenBucketRateLimiter> _limiters = new();

    /// <summary>
    /// Gets or creates a named rate limiter. Thread-safe.
    /// </summary>
    public IOperationRateLimiter GetLimiter(string name)
    {
        return _limiters.GetOrAdd(name, key =>
        {
            if (Defaults.TryGetValue(key, out var config))
            {
                return new TokenBucketRateLimiter(config.OpsPerSec, config.Burst);
            }

            throw new ArgumentException($"Unknown rate limiter: {key}", nameof(name));
        });
    }

    /// <summary>
    /// Disposes all created rate limiters.
    /// </summary>
    public void Dispose()
    {
        foreach (var limiter in _limiters.Values)
        {
            limiter.Dispose();
        }
        _limiters.Clear();
    }
}
