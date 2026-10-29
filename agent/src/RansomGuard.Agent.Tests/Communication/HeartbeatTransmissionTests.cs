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
using RansomGuard.Agent.Core.Persistence.Entities;
using RansomGuard.Agent.Core.Persistence.Repositories;
using RansomGuard.Agent.Core.Security.Cryptography;
using RansomGuard.Agent.Service;
using RansomGuard.Agent.Tests.Diagnostics;
using Shouldly;

namespace RansomGuard.Agent.Tests.Communication;

/// <summary>
/// The heartbeat fields R9 adds or corrects: upload age and uptime on the monotonic clock,
/// rejected alerts over 24 h, and "starting" before the modules are up.
/// </summary>
public sealed class HeartbeatTransmissionTests : IDisposable
{
    private readonly SqliteConnection _keepAlive;
    private readonly ServiceProvider _provider;
    private readonly ManualTimeProvider _time = new();
    private readonly InMemoryModuleStateRegistry _registry = new();
    private readonly AlertForwardingService _forwarding;
    private readonly GridHeartbeatService _heartbeat;
    private readonly EnrollmentService _enrollment;
    private readonly IOptions<AgentConfiguration> _options;

    public HeartbeatTransmissionTests()
    {
        string db = $"file:heartbeat_{Guid.NewGuid():N}?mode=memory&cache=shared";
        _keepAlive = new SqliteConnection($"Data Source={db}");
        _keepAlive.Open();

        var services = new ServiceCollection();
        services.AddDbContext<AgentDbContext>(o => o.UseSqlite($"Data Source={db}"));
        _provider = services.BuildServiceProvider();
        using (IServiceScope scope = _provider.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<AgentDbContext>().Database.EnsureCreated();
        }

        var config = new AgentConfiguration
        {
            Identity = new AgentIdentityOptions { Id = "test-agent", Hostname = "TEST" },
            Server = new ServerOptions { BaseUrl = "https://localhost" },
            Detection = new DetectionOptions { WatchPaths = [] },
            Database = new DatabaseOptions { ConnectionString = $"Data Source={Path.GetTempPath()}rg_hb_{Guid.NewGuid():N}.db" },
        };
        _options = Options.Create(config);
        var grid = new Mock<IGridApiClient>();
        _enrollment = BuildEnrollmentService(_options);
        var scopes = _provider.GetRequiredService<IServiceScopeFactory>();

        _forwarding = new AlertForwardingService(grid.Object, _enrollment, scopes,
            NullLogger<AlertForwardingService>.Instance, _time);
        _heartbeat = new GridHeartbeatService(grid.Object, _enrollment, _registry, _forwarding, scopes, _options,
            NullLogger<GridHeartbeatService>.Instance, _time);
    }

    public void Dispose()
    {
        _provider.Dispose();
        _keepAlive.Dispose();
    }

    [Fact]
    public async Task The_upload_age_is_measured_on_the_monotonic_clock_and_ignores_wall_clock_changes()
    {
        (await _heartbeat.BuildHeartbeatAsync(CancellationToken.None)).Transmission!.LastUploadOkAgeSeconds.ShouldBeNull();

        _forwarding.MarkUploadOk();
        _time.Advance(TimeSpan.FromSeconds(42));
        // Someone sets the wall clock back an hour (or NTP corrects a drifted clock).
        _time.SetWallClock(_time.GetUtcNow().AddHours(-1));

        HeartbeatRequest beat = await _heartbeat.BuildHeartbeatAsync(CancellationToken.None);

        beat.Transmission!.LastUploadOkAgeSeconds.ShouldBe(42);
    }

    [Fact]
    public async Task The_uptime_is_real_and_monotonic_and_modules_not_yet_up_read_starting()
    {
        _time.Advance(TimeSpan.FromSeconds(125));
        _time.SetWallClock(_time.GetUtcNow().AddDays(-3));

        HeartbeatRequest beat = await _heartbeat.BuildHeartbeatAsync(CancellationToken.None);

        beat.AgentUptimeSeconds.ShouldBe(125);
        foreach (string code in new[] { ModuleCode.Sentinel, ModuleCode.Entropy, ModuleCode.UsbGuard, ModuleCode.ExfilWatch })
        {
            ModuleStatusInfo module = beat.Modules.Single(m => m.Code == code);
            module.State.ShouldBe("inactive");
            module.ReasonCode.ShouldBe(ModuleReasonCode.Starting); // the GRID judges it with the uptime
        }
    }

    [Fact]
    public async Task Rejected_alerts_count_the_refusals_of_the_last_24_hours_only()
    {
        DateTime now = DateTime.UtcNow;
        using (IServiceScope scope = _provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AgentDbContext>();
            db.PendingAlertUploads.AddRange(
                Upload(PendingUploadStatus.FailedValidation, now.AddMinutes(-5)),
                Upload(PendingUploadStatus.FailedValidation, now.AddHours(-23)),
                Upload(PendingUploadStatus.FailedValidation, now.AddHours(-25)), // outside the window
                Upload(PendingUploadStatus.Pending, now.AddMinutes(-1)),           // not refused
                Upload(PendingUploadStatus.Uploaded, now.AddMinutes(-2)));         // not refused
            await db.SaveChangesAsync();
        }

        HeartbeatRequest beat = await _heartbeat.BuildHeartbeatAsync(CancellationToken.None);

        beat.Transmission!.RejectedAlerts.ShouldBe(2);
        beat.Transmission.PendingAlerts.ShouldBe(1);
    }

    [Fact]
    public async Task Attribution_complements_are_not_counted_as_pending_or_rejected_alerts()
    {
        // ADR-025 commit 4: complements share the retry table with the alerts. Until the GRID route
        // exists (commit 5) every complement waits there; counted as alerts, they would show the
        // console alerts "not yet transmitted" that do not exist.
        DateTime now = DateTime.UtcNow;
        using (IServiceScope scope = _provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AgentDbContext>();
            db.PendingAlertUploads.AddRange(
                Upload(PendingUploadStatus.Pending, now.AddMinutes(-1)),
                Upload(PendingUploadStatus.FailedValidation, now.AddMinutes(-5)),
                Upload(PendingUploadStatus.Pending, now.AddMinutes(-1), PendingUploadKind.Attribution),
                Upload(PendingUploadStatus.Uploading, now.AddMinutes(-1), PendingUploadKind.Attribution),
                Upload(PendingUploadStatus.FailedValidation, now.AddMinutes(-5), PendingUploadKind.Attribution));
            await db.SaveChangesAsync();
        }

        HeartbeatRequest beat = await _heartbeat.BuildHeartbeatAsync(CancellationToken.None);

        beat.Transmission!.PendingAlerts.ShouldBe(1);
        beat.Transmission.RejectedAlerts.ShouldBe(1);
    }

    [Fact]
    public async Task An_unreadable_database_reports_pending_rejected_and_db_free_space_as_unknown_never_zero()
    {
        // A file that is not a SQLite database: every read fails, as with a corrupt agent.db.
        string corrupt = Path.Combine(Path.GetTempPath(), $"rg_corrupt_{Guid.NewGuid():N}.db");
        await File.WriteAllTextAsync(corrupt, new string('x', 4096));
        var services = new ServiceCollection();
        services.AddDbContext<AgentDbContext>(o => o.UseSqlite($"Data Source={corrupt};Pooling=False"));
        using ServiceProvider broken = services.BuildServiceProvider();
        var heartbeat = new GridHeartbeatService(new Mock<IGridApiClient>().Object, _enrollment, _registry, _forwarding,
            broken.GetRequiredService<IServiceScopeFactory>(), _options, NullLogger<GridHeartbeatService>.Instance, _time);

        HeartbeatRequest beat = await heartbeat.BuildHeartbeatAsync(CancellationToken.None);

        // 0 would say "queue empty" / "0 bytes free" and could turn the verdict green.
        beat.Transmission!.PendingAlerts.ShouldBeNull();
        beat.Transmission.RejectedAlerts.ShouldBeNull();
        beat.Disk!.DbVolumeFreeBytes.ShouldBeNull();
        // What can still be measured is still reported.
        beat.Disk.SystemFreeBytes.ShouldNotBeNull();
        beat.Disk.SystemFreeBytes.Value.ShouldBeGreaterThan(0);
        File.Delete(corrupt);
    }

    [Fact]
    public async Task Audit_entries_not_recorded_are_reported_and_unknown_is_null_never_zero()
    {
        // No counter wired (an agent built without it): not reported, never a reassuring 0.
        (await _heartbeat.BuildHeartbeatAsync(CancellationToken.None)).Transmission!.AuditWriteFailures.ShouldBeNull();

        var health = new AuditHealth();
        var heartbeat = new GridHeartbeatService(new Mock<IGridApiClient>().Object, _enrollment, _registry, _forwarding,
            _provider.GetRequiredService<IServiceScopeFactory>(), _options, NullLogger<GridHeartbeatService>.Instance,
            _time, health);
        (await heartbeat.BuildHeartbeatAsync(CancellationToken.None)).Transmission!.AuditWriteFailures.ShouldBe(0);

        health.RecordFailure();
        health.RecordFailure();
        (await heartbeat.BuildHeartbeatAsync(CancellationToken.None)).Transmission!.AuditWriteFailures.ShouldBe(2);
    }

    [Fact]
    public async Task The_heartbeat_reports_the_version_of_the_running_build()
    {
        HeartbeatRequest beat = await _heartbeat.BuildHeartbeatAsync(CancellationToken.None);

        beat.AgentVersion.ShouldBe(AgentVersion.Current); // never a setting (the demo showed 0.3.0)
    }

    private static PendingAlertUpload Upload(
        PendingUploadStatus status, DateTime firstAttemptAt, PendingUploadKind kind = PendingUploadKind.Alert) => new()
    {
        ClientMessageId = Guid.NewGuid().ToString(),
        Kind = kind,
        SerializedPayload = "{}",
        FirstAttemptAt = firstAttemptAt,
        NextRetryAt = firstAttemptAt,
        Status = status,
    };

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
