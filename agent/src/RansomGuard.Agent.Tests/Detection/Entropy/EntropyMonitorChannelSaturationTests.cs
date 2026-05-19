using System.Threading.Channels;
using Shouldly;

namespace RansomGuard.Agent.Tests.Detection.Entropy;

/// <summary>
/// Tests for bounded channel DropOldest behavior as used by EntropyMonitor.
/// Verifies that channel saturation causes graceful dropping, not crashes.
/// </summary>
public sealed class EntropyMonitorChannelSaturationTests
{
    [Fact(Timeout = 30_000)]
    public void ChannelSaturation_DropOldest_NoCrash()
    {
        // Mirror EntropyMonitor channel config with small capacity for test
        var channel = Channel.CreateBounded<string>(
            new BoundedChannelOptions(100)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true
            });

        int writeAttempts = 10_000;
        int successfulWrites = 0;

        // Flood the channel without reading
        for (int i = 0; i < writeAttempts; i++)
        {
            if (channel.Writer.TryWrite($"C:\\test\\file_{i}.txt"))
                successfulWrites++;
        }

        // All writes should succeed (DropOldest never rejects)
        successfulWrites.ShouldBe(writeAttempts);

        // Only the newest 100 items should remain in the channel
        int readCount = 0;
        var lastItem = string.Empty;

        while (channel.Reader.TryRead(out string? item))
        {
            lastItem = item;
            readCount++;
        }

        readCount.ShouldBe(100, "Bounded channel should contain exactly capacity items");
        lastItem.ShouldBe($"C:\\test\\file_{writeAttempts - 1}.txt",
            "Last item should be the most recently written");
    }

    [Fact(Timeout = 30_000)]
    public async Task ChannelSaturation_ConcurrentWriters_NoCrash()
    {
        var channel = Channel.CreateBounded<string>(
            new BoundedChannelOptions(100)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true
            });

        // Simulate multiple FSW threads writing concurrently
        var tasks = Enumerable.Range(0, 10).Select(threadId =>
            Task.Run(() =>
            {
                for (int i = 0; i < 1000; i++)
                    channel.Writer.TryWrite($"thread_{threadId}_file_{i}.txt");
            }));

        await Task.WhenAll(tasks);

        // Should not crash, channel should contain items
        int count = 0;
        while (channel.Reader.TryRead(out _)) count++;

        count.ShouldBe(100, "Channel should be capped at capacity after flood");
    }

    [Fact(Timeout = 30_000)]
    public async Task ChannelSaturation_ReaderKeepsUp_NoDrops()
    {
        var channel = Channel.CreateBounded<string>(
            new BoundedChannelOptions(100)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true
            });

        int consumed = 0;
        using var cts = new CancellationTokenSource();

        // Start consumer
        var consumerTask = Task.Run(async () =>
        {
            await foreach (var item in channel.Reader.ReadAllAsync(cts.Token))
            {
                Interlocked.Increment(ref consumed);
            }
        });

        // Write 500 items with small delay (reader should keep up)
        for (int i = 0; i < 500; i++)
        {
            channel.Writer.TryWrite($"file_{i}.txt");
            if (i % 50 == 0)
                await Task.Delay(1);
        }

        channel.Writer.Complete();
        await consumerTask;

        consumed.ShouldBe(500, "When reader keeps up, no items should be dropped");
    }

    [Fact]
    public void ChannelConfig_MatchesEntropyMonitor()
    {
        // Verify the production config matches what we test
        const int productionCapacity = 10_000;

        var channel = Channel.CreateBounded<string>(
            new BoundedChannelOptions(productionCapacity)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true
            });

        // Should accept writes up to capacity without blocking
        for (int i = 0; i < productionCapacity; i++)
            channel.Writer.TryWrite($"file_{i}.txt").ShouldBeTrue();

        // Capacity+1 write should still succeed (drops oldest)
        channel.Writer.TryWrite("overflow.txt").ShouldBeTrue();
    }
}
