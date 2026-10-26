using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RansomGuard.Agent.Core.Detection.IronClad;
using RansomGuard.Agent.Core.Detection.IronClad.Communication;
using RansomGuard.Agent.Core.Detection.IronClad.Models;
using RansomGuard.Agent.Core.Persistence;
using RansomGuard.Agent.Service;
using Shouldly;

namespace RansomGuard.Agent.Tests.Service;

/// <summary>
/// Windows is told the service is running before the agent does anything slow.
///
/// <para>
/// A Windows service has to report itself running inside the Service Control Manager's limit,
/// which is 30 s and is not the agent's to set. The host reports that only once every hosted
/// service has returned from <c>StartAsync</c> -- and for a <see cref="BackgroundService"/> that
/// means once <c>ExecuteAsync</c> has reached an await that does not finish straight away.
/// Anything done before that await is done while the SCM is counting.
/// </para>
///
/// <para>
/// On PC-LAB on 2026-10-26 it ran out: `sc start` returned 1053, "the service did not respond to
/// the start or control request in a timely fashion", and the installer stopped with the agent
/// unenrolled. The work that has to happen -- the kernel session, the decoys, enrolment with the
/// GRID -- is work the workstation can do a moment later; what it cannot do is make Windows wait
/// for it.
/// </para>
///
/// <para>
/// So every long-running service yields before it touches anything. These tests hold that line
/// where it can be measured: a service whose body is deliberately slow must still hand control
/// back at once.
/// </para>
/// </summary>
public sealed class AgentStartupTests
{
    /// <summary>
    /// Comfortably under the SCM's 30 s, and far under the 2 s this service waits for its device:
    /// if the wait were still on the startup path this could not pass.
    /// </summary>
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(1);

    [Fact(Timeout = 30_000)]
    public async Task Reconciling_the_backup_device_does_not_hold_the_service_start()
    {
        var service = new IronCladStateReconciliationService(
            new SlowCommunicator(),
            Options.Create(new IronCladOptions { Enabled = true }),
            new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            NullLogger<IronCladStateReconciliationService>.Instance);

        var clock = Stopwatch.StartNew();
        await service.StartAsync(CancellationToken.None);
        clock.Stop();

        // Before the fix this was an IHostedService whose StartAsync waited two seconds for the
        // device and then reconciled with it, all of it before Windows was told anything.
        clock.Elapsed.ShouldBeLessThan(Budget,
            "the host must be free to report the service running before this work is done");

        await service.StopAsync(CancellationToken.None);
    }

    [Fact(Timeout = 30_000)]
    public async Task A_service_whose_work_is_slow_still_reports_started_at_once()
    {
        var started = new TaskCompletionSource();
        var service = new SlowService(started);

        var clock = Stopwatch.StartNew();
        await service.StartAsync(CancellationToken.None);
        clock.Stop();

        clock.Elapsed.ShouldBeLessThan(Budget, "StartAsync must not wait for the body");

        // And the work does run, just afterwards: deferring it is not the same as dropping it.
        (await Task.WhenAny(started.Task, Task.Delay(TimeSpan.FromSeconds(10))))
            .ShouldBe(started.Task, "the deferred work never ran");

        await service.StopAsync(CancellationToken.None);
    }

    [Fact(Timeout = 30_000)]
    public async Task Nothing_reads_the_database_before_it_exists()
    {
        var ready = new DatabaseReady();
        ready.IsReady.ShouldBeFalse("the schema has not been created yet");

        var waiting = Task.Run(() => ready.WaitAsync(CancellationToken.None));
        await Task.Delay(200);
        waiting.IsCompleted.ShouldBeFalse("a monitor must not proceed before the schema exists");

        ready.Signal();

        await waiting.WaitAsync(TimeSpan.FromSeconds(5));
        ready.IsReady.ShouldBeTrue();

        // And afterwards it costs nothing: a monitor started later does not wait at all.
        await ready.WaitAsync(CancellationToken.None);
    }

    [Fact(Timeout = 30_000)]
    public async Task A_database_that_cannot_be_prepared_stops_the_monitors_rather_than_hanging_them()
    {
        var ready = new DatabaseReady();
        var waiting = Task.Run(() => ready.WaitAsync(CancellationToken.None));

        ready.Fail(new InvalidOperationException("the schema is broken"));

        // The alternative is a detector that waits for ever, which from the outside looks exactly
        // like one that is watching. It has to fail loudly instead.
        InvalidOperationException thrown =
            await Should.ThrowAsync<InvalidOperationException>(async () => await waiting);
        thrown.Message.ShouldBe("the schema is broken");
        ready.IsReady.ShouldBeFalse();
    }

    /// <summary>A BackgroundService written the way the agent's are: yield, then the slow part.</summary>
    private sealed class SlowService(TaskCompletionSource started) : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await Task.Yield();
            Thread.Sleep(TimeSpan.FromSeconds(2));   // stands in for the kernel session
            started.TrySetResult();
        }
    }

    /// <summary>A device that is there but slow to answer, which is the awkward case.</summary>
    private sealed class SlowCommunicator : IIronCladCommunicator
    {
        public bool IsConnected => true;
        public string? DeviceVersion => "1.0.0";
        public int PortCount => 4;
        public event EventHandler<HeartbeatEventArgs>? HeartbeatReceived;
        public event EventHandler<ConnectionLostEventArgs>? ConnectionLost;

        public Task<bool> ConnectAsync(CancellationToken ct = default) => Task.FromResult(true);

        public Task DisconnectAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task<IronCladResponse> SendCommandAsync(IronCladCommand command, CancellationToken ct = default)
            => Task.FromResult(new IronCladResponse
            {
                CommandId = command.Id,
                Type = IronCladResponseType.Pong,
                RawPayload = "PONG",
                ReceivedAt = DateTime.UtcNow,
                IsAcknowledgement = true,
            });

        public async Task<HealthStatus> CheckHealthAsync(CancellationToken ct = default)
        {
            await Task.Delay(TimeSpan.FromSeconds(5), ct);
            return new HealthStatus
            {
                IsConnected = true, DeviceVersion = "1.0.0", PortCount = 4,
                PortStates = new Dictionary<int, PortState>(), LastHeartbeatAt = DateTime.UtcNow,
                AverageLatency = TimeSpan.Zero, MissedHeartbeats = 0,
            };
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        private void Unused()
        {
            HeartbeatReceived?.Invoke(this, null!);
            ConnectionLost?.Invoke(this, null!);
        }
    }
}
