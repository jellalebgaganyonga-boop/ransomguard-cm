using Microsoft.Extensions.Logging;
using Moq;
using RansomGuard.Agent.Core.Detection.CrossModule;
using Shouldly;

namespace RansomGuard.Agent.Tests.Detection.CrossModule;

public sealed class DetectionEventBusTests
{
    private readonly InMemoryDetectionEventBus _bus;

    public DetectionEventBusTests()
    {
        _bus = new InMemoryDetectionEventBus(new Mock<ILogger<InMemoryDetectionEventBus>>().Object);
    }

    [Fact]
    public async Task Publish_With_No_Subscribers_Does_Not_Block()
    {
        var signal = CreateSignal();

        // Should complete without error
        await _bus.PublishAsync(signal);
    }

    [Fact]
    public async Task Subscribe_Receives_Published_Signals()
    {
        EntropySignal? received = null;
        _bus.Subscribe<EntropySignal>(async (signal, _) =>
        {
            received = signal;
            await Task.CompletedTask;
        });

        var published = CreateSignal();
        await _bus.PublishAsync(published);

        received.ShouldNotBeNull();
        received.SignalId.ShouldBe(published.SignalId);
        received.FilePath.ShouldBe(published.FilePath);
        received.EntropyAlertId.ShouldBe(published.EntropyAlertId);
    }

    [Fact]
    public async Task Multiple_Subscribers_All_Receive_Signal()
    {
        int count = 0;

        _bus.Subscribe<EntropySignal>(async (_, _) => { Interlocked.Increment(ref count); await Task.CompletedTask; });
        _bus.Subscribe<EntropySignal>(async (_, _) => { Interlocked.Increment(ref count); await Task.CompletedTask; });
        _bus.Subscribe<EntropySignal>(async (_, _) => { Interlocked.Increment(ref count); await Task.CompletedTask; });

        await _bus.PublishAsync(CreateSignal());

        count.ShouldBe(3);
    }

    [Fact]
    public async Task Dispose_Subscription_Stops_Receiving()
    {
        int count = 0;
        var sub = _bus.Subscribe<EntropySignal>(async (_, _) =>
        {
            Interlocked.Increment(ref count);
            await Task.CompletedTask;
        });

        await _bus.PublishAsync(CreateSignal());
        count.ShouldBe(1);

        sub.Dispose();

        await _bus.PublishAsync(CreateSignal());
        count.ShouldBe(1); // Should not increment after dispose
    }

    [Fact]
    public async Task Subscriber_Exception_Does_Not_Block_Other_Subscribers()
    {
        int successCount = 0;

        // First subscriber throws
        _bus.Subscribe<EntropySignal>((_, _) => throw new InvalidOperationException("Boom"));

        // Second subscriber should still receive
        _bus.Subscribe<EntropySignal>(async (_, _) =>
        {
            Interlocked.Increment(ref successCount);
            await Task.CompletedTask;
        });

        await _bus.PublishAsync(CreateSignal());

        successCount.ShouldBe(1);
    }

    [Fact]
    public async Task Channel_Saturation_Does_Not_Crash()
    {
        int received = 0;
        _bus.Subscribe<EntropySignal>(async (_, _) =>
        {
            Interlocked.Increment(ref received);
            await Task.CompletedTask;
        });

        // Publish 6000 signals (exceeding 5000 channel capacity)
        var tasks = new List<Task>();
        for (int i = 0; i < 6000; i++)
        {
            tasks.Add(_bus.PublishAsync(CreateSignal()));
        }

        await Task.WhenAll(tasks);

        // All 6000 should be delivered since InMemoryDetectionEventBus delivers inline
        received.ShouldBe(6000);
    }

    private static EntropySignal CreateSignal() => new()
    {
        SignalId = Guid.NewGuid(),
        SourceModule = "ENTROPY",
        EmittedAt = DateTime.UtcNow,
        FilePath = @"C:\test\document.docx",
        ProcessId = 1234,
        ProcessName = "ransomware.exe",
        EntropyValue = 7.95,
        FileSize = 1024 * 1024,
        EntropyAlertId = Guid.NewGuid()
    };
}
