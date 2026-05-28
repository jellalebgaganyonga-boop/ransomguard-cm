using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RansomGuard.Agent.Core.Detection.IronClad;
using RansomGuard.Agent.Core.Detection.IronClad.Communication;
using RansomGuard.Agent.Core.Detection.IronClad.Models;
using RansomGuard.Agent.Service;

namespace RansomGuard.Agent.Tests.Detection.IronClad;

public sealed class IronCladHeartbeatServiceTests
{
    private readonly ILogger<IronCladHeartbeatService> _logger = NullLogger<IronCladHeartbeatService>.Instance;

    [Fact]
    public async Task Heartbeat_Received_Resets_Miss_Counter()
    {
        var comm = new TestCommunicator();
        var opts = Options.Create(new IronCladOptions
        {
            Enabled = true,
            HeartbeatIntervalSeconds = 1,
            HeartbeatTimeoutSeconds = 5
        });

        using var service = new IronCladHeartbeatService(comm, opts, _logger);
        using var cts = new CancellationTokenSource();

        var execTask = service.StartAsync(cts.Token);
        await Task.Delay(100); // Let service start

        // Simulate heartbeat
        comm.RaiseHeartbeat(TimeSpan.FromMilliseconds(10));
        await Task.Delay(100);

        Assert.Equal(0, service.ConsecutiveMissed);

        await cts.CancelAsync();
    }

    [Fact]
    public async Task Missed_Heartbeats_Increment_Counter()
    {
        var comm = new TestCommunicator();
        var opts = Options.Create(new IronCladOptions
        {
            Enabled = true,
            HeartbeatIntervalSeconds = 1,
            HeartbeatTimeoutSeconds = 10
        });

        using var service = new IronCladHeartbeatService(comm, opts, _logger);
        using var cts = new CancellationTokenSource();

        _ = Task.Run(() => service.StartAsync(cts.Token));

        // Wait for 3 heartbeat intervals without sending heartbeats
        await Task.Delay(3500);

        Assert.True(service.ConsecutiveMissed >= 2, $"Expected >= 2 missed, got {service.ConsecutiveMissed}");

        await cts.CancelAsync();
    }

    [Fact]
    public async Task Reconnection_Resets_Counter()
    {
        var comm = new TestCommunicator();
        var opts = Options.Create(new IronCladOptions
        {
            Enabled = true,
            HeartbeatIntervalSeconds = 1,
            HeartbeatTimeoutSeconds = 30
        });

        using var service = new IronCladHeartbeatService(comm, opts, _logger);
        using var cts = new CancellationTokenSource();

        _ = Task.Run(() => service.StartAsync(cts.Token));
        await Task.Delay(2500); // Let some heartbeats be missed

        // Now simulate heartbeat (reconnection)
        comm.RaiseHeartbeat(TimeSpan.FromMilliseconds(5));
        await Task.Delay(100);

        Assert.Equal(0, service.ConsecutiveMissed);

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
