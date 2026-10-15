using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RansomGuard.Agent.Core.Detection.IronClad;
using RansomGuard.Agent.Core.Detection.IronClad.Communication;
using RansomGuard.Agent.Core.Detection.IronClad.Models;
using RansomGuard.Agent.Service;
using RansomGuard.Agent.Tests.Diagnostics;

namespace RansomGuard.Agent.Tests.Detection.IronClad;

/// <summary>
/// The IronClad liveness monitor, on a clock the test moves by hand: no fixed wait, so the result
/// never depends on how loaded the machine is (the previous version waited real seconds and
/// failed once in 21 runs of the suite under load).
/// </summary>
public sealed class IronCladHeartbeatServiceTests
{
    private readonly ILogger<IronCladHeartbeatService> _logger = NullLogger<IronCladHeartbeatService>.Instance;
    private readonly ManualTimeProvider _time = new();

    private IronCladHeartbeatService Service(TestCommunicator comm, int timeoutSeconds, ILogger<IronCladHeartbeatService>? logger = null) =>
        new(comm, Options.Create(new IronCladOptions
        {
            Enabled = true,
            HeartbeatIntervalSeconds = 1,
            HeartbeatTimeoutSeconds = timeoutSeconds
        }), logger ?? _logger, _time);

    /// <summary>One check of the monitor: wait until it is parked on its tick, then fire it.</summary>
    private async Task TickAsync(TimeSpan by)
    {
        await Eventually.HoldsAsync(() => _time.ActiveTimers > 0, "the monitor waiting on its next tick");
        _time.Advance(by);
    }

    [Fact]
    public async Task Heartbeat_Received_Resets_Miss_Counter()
    {
        var comm = new TestCommunicator();
        using var service = Service(comm, timeoutSeconds: 5);
        using var cts = new CancellationTokenSource();
        await service.StartAsync(cts.Token);

        await TickAsync(TimeSpan.FromSeconds(1.5));
        await Eventually.HoldsAsync(() => service.ConsecutiveMissed == 1, "one missed heartbeat");

        comm.RaiseHeartbeat(TimeSpan.FromMilliseconds(10));

        Assert.Equal(0, service.ConsecutiveMissed);
        await cts.CancelAsync();
    }

    [Fact]
    public async Task Missed_Heartbeats_Increment_Counter()
    {
        var comm = new TestCommunicator();
        using var service = Service(comm, timeoutSeconds: 10);
        using var cts = new CancellationTokenSource();
        await service.StartAsync(cts.Token);

        for (int missed = 1; missed <= 3; missed++)
        {
            await TickAsync(TimeSpan.FromSeconds(1.5));
            int expected = missed;
            await Eventually.HoldsAsync(() => service.ConsecutiveMissed == expected, $"{expected} missed heartbeats");
        }

        await cts.CancelAsync();
    }

    [Fact]
    public async Task A_heartbeat_within_the_interval_is_not_a_miss()
    {
        var comm = new TestCommunicator();
        using var service = Service(comm, timeoutSeconds: 10);
        using var cts = new CancellationTokenSource();
        await service.StartAsync(cts.Token);

        for (int i = 0; i < 3; i++)
        {
            await Eventually.HoldsAsync(() => _time.ActiveTimers > 0, "the monitor waiting on its next tick");
            _time.Advance(TimeSpan.FromSeconds(0.6));
            comm.RaiseHeartbeat(TimeSpan.FromMilliseconds(5)); // the device answers in time
            _time.Advance(TimeSpan.FromSeconds(0.6));          // the check: 0.6 s since the heartbeat
        }

        await Eventually.HoldsAsync(() => _time.ActiveTimers > 0, "the monitor waiting on its next tick");
        Assert.Equal(0, service.ConsecutiveMissed);
        await cts.CancelAsync();
    }

    [Fact]
    public async Task Reconnection_Resets_Counter()
    {
        var comm = new TestCommunicator();
        using var service = Service(comm, timeoutSeconds: 30);
        using var cts = new CancellationTokenSource();
        await service.StartAsync(cts.Token);

        await TickAsync(TimeSpan.FromSeconds(1.5));
        await TickAsync(TimeSpan.FromSeconds(1.5));
        await Eventually.HoldsAsync(() => service.ConsecutiveMissed == 2, "two missed heartbeats");

        comm.RaiseHeartbeat(TimeSpan.FromMilliseconds(5)); // reconnection

        Assert.Equal(0, service.ConsecutiveMissed);
        await cts.CancelAsync();
    }

    [Fact]
    public async Task No_heartbeat_past_the_timeout_raises_one_critical_alert()
    {
        var comm = new TestCommunicator();
        var log = new RecordingLogger<IronCladHeartbeatService>();
        using var service = Service(comm, timeoutSeconds: 5, log);
        using var cts = new CancellationTokenSource();
        await service.StartAsync(cts.Token);

        for (int i = 0; i < 4; i++)
        {
            await TickAsync(TimeSpan.FromSeconds(1.5)); // 6 s without a heartbeat
        }

        await Eventually.HoldsAsync(() => log.Entries.Any(e => e.Level == LogLevel.Critical), "the Critical alert");
        await TickAsync(TimeSpan.FromSeconds(1.5));
        await Eventually.HoldsAsync(() => service.ConsecutiveMissed == 5, "the fifth check done");
        Assert.Equal(1, log.Entries.Count(e => e.Level == LogLevel.Critical)); // raised once, not every tick
        await cts.CancelAsync();
    }

    [Fact]
    public async Task Disabled_IronClad_Does_Not_Start_Monitor()
    {
        var comm = new TestCommunicator();
        var opts = Options.Create(new IronCladOptions
        {
            Enabled = false,
            HeartbeatIntervalSeconds = 1,
            HeartbeatTimeoutSeconds = 5
        });

        using var service = new IronCladHeartbeatService(comm, opts, _logger);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        // Should complete quickly since disabled
        await service.StartAsync(cts.Token);
        Assert.Equal(0, service.ConsecutiveMissed);
    }

    private sealed class TestCommunicator : IIronCladCommunicator
    {
        public bool IsConnected => true;
        public string? DeviceVersion => "1.0.0";
        public int PortCount => 4;
        public event EventHandler<HeartbeatEventArgs>? HeartbeatReceived;
        public event EventHandler<ConnectionLostEventArgs>? ConnectionLost;

        public void RaiseHeartbeat(TimeSpan latency)
        {
            HeartbeatReceived?.Invoke(this, new HeartbeatEventArgs
            {
                Timestamp = DateTime.UtcNow,
                Latency = latency
            });
        }

        public void RaiseConnectionLost(string reason)
        {
            ConnectionLost?.Invoke(this, new ConnectionLostEventArgs
            {
                DisconnectedAt = DateTime.UtcNow,
                Reason = reason
            });
        }

        public Task<bool> ConnectAsync(CancellationToken ct = default) => Task.FromResult(true);
        public Task DisconnectAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<IronCladResponse> SendCommandAsync(IronCladCommand command, CancellationToken ct = default)
            => Task.FromResult(new IronCladResponse
            {
                CommandId = command.Id, Type = IronCladResponseType.Pong,
                RawPayload = "PONG", ReceivedAt = DateTime.UtcNow, IsAcknowledgement = true
            });
        public Task<HealthStatus> CheckHealthAsync(CancellationToken ct = default)
            => Task.FromResult(new HealthStatus
            {
                IsConnected = true, DeviceVersion = "1.0.0", PortCount = 4,
                PortStates = new Dictionary<int, PortState>(), LastHeartbeatAt = DateTime.UtcNow,
                AverageLatency = TimeSpan.Zero, MissedHeartbeats = 0
            });
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
