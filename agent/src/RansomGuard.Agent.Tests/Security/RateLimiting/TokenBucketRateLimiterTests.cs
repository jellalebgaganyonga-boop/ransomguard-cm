using System.Diagnostics;
using RansomGuard.Agent.Core.Security.RateLimiting;
using Shouldly;

namespace RansomGuard.Agent.Tests.Security.RateLimiting;

/// <summary>
/// Tests for <see cref="TokenBucketRateLimiter"/> and <see cref="RateLimiterFactory"/>.
/// </summary>
public sealed class TokenBucketRateLimiterTests : IDisposable
{
    private readonly TokenBucketRateLimiter _limiter;

    public TokenBucketRateLimiterTests()
    {
        _limiter = new TokenBucketRateLimiter(operationsPerSecond: 50, burstCapacity: 10);
    }

    [Fact]
    public async Task Acquire_BurstWithinLimit_Succeeds()
    {
        int acquired = 0;
        for (int i = 0; i < 10; i++)
        {
            if (await _limiter.AcquireAsync())
                acquired++;
        }

        acquired.ShouldBe(10);
    }

    [Fact]
    public async Task Acquire_BurstExceedingLimit_BlocksUntilRefill()
    {
        // Drain the bucket
        for (int i = 0; i < 10; i++)
            await _limiter.AcquireAsync();

        // Next acquire should block until token refill
        var sw = Stopwatch.StartNew();
        bool result = await _limiter.AcquireAsync();
        sw.Stop();

        result.ShouldBeTrue();
        sw.ElapsedMilliseconds.ShouldBeGreaterThan(5); // At least some wait
    }

    [Fact]
    public async Task Acquire_RateRespectedOver1Second()
    {
        using var limiter = new TokenBucketRateLimiter(operationsPerSecond: 20, burstCapacity: 5);

        var sw = Stopwatch.StartNew();
        int count = 0;
        while (sw.ElapsedMilliseconds < 1000 && count < 30)
        {
            await limiter.AcquireAsync();
            count++;
        }
        sw.Stop();

        // Should be rate-limited: 5 burst + ~20/sec * ~1s = roughly 25 max
        count.ShouldBeLessThanOrEqualTo(30);
    }

    [Fact]
    public async Task Acquire_CancellationHonored()
    {
        // Drain the bucket
        for (int i = 0; i < 10; i++)
            await _limiter.AcquireAsync();

        using var cts = new CancellationTokenSource(50); // Cancel after 50ms

        await Should.ThrowAsync<OperationCanceledException>(async () =>
        {
            // Try to acquire many more than available — should eventually cancel
            for (int i = 0; i < 1000; i++)
                await _limiter.AcquireAsync(cts.Token);
        });
    }

    [Fact]
    public void TryAcquire_ReturnsFalseWhenEmpty()
    {
        // Drain all tokens
        while (_limiter.TryAcquire()) { }

        _limiter.TryAcquire().ShouldBeFalse();
    }

    [Fact]
    public void TryAcquire_ReturnsTrueWhenAvailable()
    {
        // Fresh limiter should have tokens
        _limiter.TryAcquire().ShouldBeTrue();
    }

    [Fact]
    public void AvailableTokens_ReflectsCurrentState()
    {
        int initial = _limiter.AvailableTokens;
        initial.ShouldBeGreaterThan(0);

        _limiter.TryAcquire();

        _limiter.AvailableTokens.ShouldBeLessThan(initial);
    }

    [Fact]
    public void MultipleNamedLimiters_IsolatedCorrectly()
    {
        using var factory = new RateLimiterFactory();

        var baseline = factory.GetLimiter(RateLimiterFactory.EntropyBaselineBuild);
        var computation = factory.GetLimiter(RateLimiterFactory.EntropyComputation);

        baseline.OperationsPerSecond.ShouldBe(50);
        computation.OperationsPerSecond.ShouldBe(100);

        // Draining one should not affect the other
        while (baseline.TryAcquire()) { }

        computation.TryAcquire().ShouldBeTrue();
    }

    public void Dispose() => _limiter.Dispose();
}
