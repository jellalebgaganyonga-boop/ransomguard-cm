using System.Diagnostics;
using System.Reflection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using RansomGuard.Agent.Core.Communication;
using RansomGuard.Agent.Core.Communication.Models;
using RansomGuard.Agent.Core.Configuration;
using RansomGuard.Agent.Core.Diagnostics;
using RansomGuard.Agent.Core.Persistence;
using RansomGuard.Agent.Core.Security.Cryptography;
using RansomGuard.Agent.Service;
using RansomGuard.Agent.Tests.Diagnostics;
using Shouldly;

namespace RansomGuard.Agent.Tests.Communication;

/// <summary>
/// The heartbeat backoff is capped at 2 minutes, ±20% (LOT 2 contract). In the LOT 2 demo the cap
/// was 15 minutes: the link came back at 15:06:24 and the first heartbeat was accepted at 15:13:27,
/// so the console stayed orange 7 minutes after the cut ended. Driven by a manual clock: a one-hour
/// outage runs in milliseconds, and every wait is measured exactly.
/// </summary>
public sealed class HeartbeatBackoffTests : IDisposable
{
    // The cap plus the jitter: 120 s × 1.2.
    private static readonly TimeSpan LongestWait = TimeSpan.FromSeconds(144);

    private readonly SqliteConnection _keepAlive;
    private readonly ServiceProvider _provider;
    private readonly ManualTimeProvider _time = new();
    private readonly object _gate = new();
    private readonly List<(long At, bool Accepted)> _attempts = [];
    private volatile bool _linkUp = true;

    public HeartbeatBackoffTests()
    {
        string db = $"file:heartbeat_backoff_{Guid.NewGuid():N}?mode=memory&cache=shared";
        _keepAlive = new SqliteConnection($"Data Source={db}");
        _keepAlive.Open();
        var services = new ServiceCollection();
        services.AddDbContext<AgentDbContext>(o => o.UseSqlite($"Data Source={db}"));
        _provider = services.BuildServiceProvider();
        using IServiceScope scope = _provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<AgentDbContext>().Database.EnsureCreated();
    }

    public void Dispose()
    {
        _provider.Dispose();
        _keepAlive.Dispose();
    }

    [Theory]
    [InlineData(60)]   // what the GRID asks for today
    [InlineData(30)]   // the agent's default
    [InlineData(600)]  // a GRID asking for a long interval: a failure still retries within the cap
    public async Task After_a_one_hour_outage_the_first_heartbeat_leaves_within_the_cap_plus_jitter(int serverInterval)
    {
        using var cts = new CancellationTokenSource();
        GridHeartbeatService heartbeat = Build(serverInterval);
        await heartbeat.StartAsync(cts.Token);
        await WaitUntilParked();

        // The link drops right after a successful heartbeat, for an hour: long enough for any
        // exponential backoff to reach its cap (15 min was reached after 4 failures).
        _linkUp = false;
        await RunFor(TimeSpan.FromHours(1));
        Attempts().ShouldContain(a => !a.Accepted);

        // Worst case: the link comes back just after a failed attempt (the demo's case: the third
        // failure fell at about 15:06, the link came back at 15:06:24).
        int before = Attempts().Count;
        await RunUntil(() => Attempts().Count > before);
        Attempts()[^1].Accepted.ShouldBeFalse();
        _linkUp = true;
        long restoredAt = _time.GetTimestamp();
        await RunUntil(() => Attempts()[^1].Accepted && Attempts()[^1].At > restoredAt);

        TimeSpan toFirstHeartbeat = Elapsed(restoredAt, Attempts()[^1].At);
        toFirstHeartbeat.ShouldBeLessThanOrEqualTo(LongestWait);

        // And no wait after a failure was ever longer than the cap plus jitter. (After a success the
        // wait is the server's interval, 600 s in one case: that is not a backoff.)
        var attempts = Attempts();
        for (int i = 1; i < attempts.Count; i++)
        {
            if (!attempts[i - 1].Accepted)
            {
                Elapsed(attempts[i - 1].At, attempts[i].At).ShouldBeLessThanOrEqualTo(LongestWait, $"wait after failure #{i}");
            }
        }

        await cts.CancelAsync();
        await heartbeat.StopAsync(CancellationToken.None);
    }

    [Fact]
    public void The_cap_is_two_minutes()
    {
        GridHeartbeatService.MaxBackoff.ShouldBe(TimeSpan.FromMinutes(2));
    }

    private GridHeartbeatService Build(int serverInterval)
    {
        var config = new AgentConfiguration
        {
            Identity = new AgentIdentityOptions { Id = "test-agent", Hostname = "TEST" },
            Server = new ServerOptions { BaseUrl = "https://localhost", HeartbeatIntervalSeconds = 60 },
            Detection = new DetectionOptions { WatchPaths = [] },
            Database = new DatabaseOptions { ConnectionString = $"Data Source={Path.GetTempPath()}rg_hbb_{Guid.NewGuid():N}.db" },
        };
        IOptions<AgentConfiguration> options = Options.Create(config);

        var grid = new Mock<IGridApiClient>();
        grid.SetupGet(g => g.IsEnrolled).Returns(true);
        grid.Setup(g => g.SendHeartbeatAsync(It.IsAny<HeartbeatRequest>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                bool up = _linkUp;
                lock (_gate) _attempts.Add((_time.GetTimestamp(), up));
                return up
                    ? Task.FromResult(new HeartbeatResponse { NextHeartbeatInSeconds = serverInterval })
                    : Task.FromException<HeartbeatResponse>(new HttpRequestException("link down"));
            });

        EnrollmentService enrollment = BuildEnrollmentService(options);
        var scopes = _provider.GetRequiredService<IServiceScopeFactory>();
        var forwarding = new AlertForwardingService(grid.Object, enrollment, scopes,
            NullLogger<AlertForwardingService>.Instance, _time);
        return new GridHeartbeatService(grid.Object, enrollment, new InMemoryModuleStateRegistry(), forwarding,
            scopes, options, NullLogger<GridHeartbeatService>.Instance, _time);
    }

    private List<(long At, bool Accepted)> Attempts()
    {
        lock (_gate) return [.. _attempts];
    }

    private static TimeSpan Elapsed(long from, long to) => TimeSpan.FromTicks(to - from); // 1 tick = 1 timestamp unit

    /// <summary>Moves the clock one second at a time; each time the loop wakes, waits until it parks again.</summary>
    private async Task RunFor(TimeSpan duration)
    {
        for (int s = 0; s < duration.TotalSeconds; s++)
        {
            await Step();
        }
    }

    private async Task RunUntil(Func<bool> condition)
    {
        for (int s = 0; s < 3600; s++)
        {
            if (Attempts().Count > 0 && condition())
            {
                return;
            }

            await Step();
        }

        throw new TimeoutException("condition not reached within an hour of simulated time");
    }

    private async Task Step()
    {
        _time.Advance(TimeSpan.FromSeconds(1));
        if (_time.ActiveTimers == 0)
        {
            await WaitUntilParked();
        }
    }

    private async Task WaitUntilParked()
    {
        var watch = Stopwatch.StartNew();
        while (_time.ActiveTimers == 0)
        {
            watch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(10), "the heartbeat loop never parked on its next delay");
            await Task.Delay(1);
        }
    }

    private static EnrollmentService BuildEnrollmentService(IOptions<AgentConfiguration> options)
    {
        var http = new Mock<IHttpClientFactory>();
        http.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(new HttpClient());
        var gridClient = new GridApiClient(http.Object, options, NullLogger<GridApiClient>.Instance);
        var state = new EnrollmentStateManager(options, NullLogger<EnrollmentStateManager>.Instance);
        var signer = new AuditLogSigner(Path.Combine(Path.GetTempPath(), "rg-signer-" + Guid.NewGuid().ToString("N")),
            NullLogger<AuditLogSigner>.Instance);
        var enrollment = new EnrollmentService(gridClient, state, signer, options, NullLogger<EnrollmentService>.Instance);
        var signal = typeof(EnrollmentService).GetField("_enrolledSignal", BindingFlags.NonPublic | BindingFlags.Instance);
        ((TaskCompletionSource?)signal?.GetValue(enrollment))?.TrySetResult();
        return enrollment;
    }
}
