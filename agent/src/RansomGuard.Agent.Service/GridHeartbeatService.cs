using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RansomGuard.Agent.Core.Communication;
using RansomGuard.Agent.Core.Communication.Models;
using RansomGuard.Agent.Core.Configuration;
using RansomGuard.Agent.Core.Diagnostics;
using RansomGuard.Agent.Core.Persistence;
using RansomGuard.Agent.Core.Persistence.Entities;
using RansomGuard.Agent.Core.Persistence.Repositories;

namespace RansomGuard.Agent.Service;

/// <summary>
/// Background service sending periodic heartbeats to the GRID server.
/// Heartbeat interval is driven by the server response (next_heartbeat_in_seconds)
/// with a fallback to the configured HeartbeatIntervalSeconds. The payload (v2)
/// reports real runtime module state from <see cref="IModuleStateRegistry"/>, disk
/// headroom, and alert-transmission health, so the console can answer "is this
/// endpoint protected, and did everything it detected reach us?".
/// </summary>
public sealed class GridHeartbeatService : BackgroundService
{
    // 15 minutes: the ceiling for the failure backoff, so a long outage never
    // stretches heartbeats beyond the window the console treats as "recent".
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(15);

    private readonly IGridApiClient _gridClient;
    private readonly EnrollmentService _enrollmentService;
    private readonly IModuleStateRegistry _moduleState;
    private readonly AlertForwardingService _forwarding;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly AgentConfiguration _config;
    private readonly ILogger<GridHeartbeatService> _logger;
    private readonly TimeProvider _time;
    private readonly AuditHealth? _auditHealth;
    // Monotonic: the uptime must not jump with a wall-clock change (NTP, DST, a user).
    private readonly long _startedAtTimestamp;

    /// <summary>Last list of pending command IDs returned by the server.</summary>
    public IReadOnlyList<string> LastPendingCommands { get; private set; } = [];

    public GridHeartbeatService(
        IGridApiClient gridClient,
        EnrollmentService enrollmentService,
        IModuleStateRegistry moduleState,
        AlertForwardingService forwarding,
        IServiceScopeFactory scopeFactory,
        IOptions<AgentConfiguration> config,
        ILogger<GridHeartbeatService> logger,
        TimeProvider? time = null,
        AuditHealth? auditHealth = null)
    {
        _auditHealth = auditHealth;
        _time = time ?? TimeProvider.System;
        _startedAtTimestamp = _time.GetTimestamp();
        _gridClient = gridClient;
        _enrollmentService = enrollmentService;
        _moduleState = moduleState;
        _forwarding = forwarding;
        _scopeFactory = scopeFactory;
        _config = config.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Wait for enrollment to complete before starting heartbeat
        if (!_gridClient.IsEnrolled)
        {
            _logger.LogInformation("GridHeartbeatService: waiting for enrollment...");
            try
            {
                await Task.WhenAny(_enrollmentService.EnrolledTask, Task.Delay(Timeout.Infinite, stoppingToken))
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (!_gridClient.IsEnrolled)
            {
                _logger.LogWarning("GridHeartbeatService: enrollment did not complete, heartbeat disabled");
                return;
            }
        }

        var intervalSeconds = _config.Server.HeartbeatIntervalSeconds;
        int consecutiveFailures = 0;
        _logger.LogInformation("GRID heartbeat started (interval={Interval}s)", intervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            bool ok = false;
            try
            {
                var request = await BuildHeartbeatAsync(stoppingToken).ConfigureAwait(false);

                var response = await _gridClient.SendHeartbeatAsync(request, stoppingToken)
                    .ConfigureAwait(false);

                LastPendingCommands = response.PendingCommands;
                intervalSeconds = response.NextHeartbeatInSeconds > 0
                    ? response.NextHeartbeatInSeconds
                    : _config.Server.HeartbeatIntervalSeconds;
                ok = true;

                if (response.PendingCommands.Count > 0)
                {
                    _logger.LogInformation("Heartbeat OK — {Count} pending commands",
                        response.PendingCommands.Count);
                }
            }
            catch (TaskCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(ex, "Heartbeat failed — will retry with backoff");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected heartbeat error");
            }

            int delaySeconds;
            if (ok)
            {
                consecutiveFailures = 0;
                delaySeconds = WithJitter(intervalSeconds);
            }
            else
            {
                consecutiveFailures++;
                delaySeconds = WithJitter(Backoff(intervalSeconds, consecutiveFailures));
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds), stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    internal async Task<HeartbeatRequest> BuildHeartbeatAsync(CancellationToken ct)
    {
        // Real uptime on the monotonic clock. A module still "starting" is judged on it by
        // the GRID: under 300 s it is "starting", beyond it counts as a start failure.
        var uptimeSeconds = (int)_time.GetElapsedTime(_startedAtTimestamp).TotalSeconds;

        var modules = _moduleState.Snapshot()
            .Select(e => new ModuleStatusInfo
            {
                Code = e.Code,
                State = ToWireState(e.State),
                ReasonCode = e.ReasonCode,
                ChangedAt = e.ChangedAt,
            })
            .ToList();

        (int? pendingAlerts, int? rejectedAlerts, long? dbVolumeFreeBytes) = await QueryDbStateAsync(ct).ConfigureAwait(false);

        return new HeartbeatRequest
        {
            AgentUptimeSeconds = uptimeSeconds,
            AgentVersion = AgentVersion.Current,
            OsVersion = Environment.OSVersion.ToString(),
            ThreatIntelVersion = null,
            LastDetectionAt = _forwarding.LastDetectionAtUtc,
            Disk = new DiskInfo
            {
                SystemFreeBytes = FreeBytes(Environment.SystemDirectory),
                DbVolumeFreeBytes = dbVolumeFreeBytes,
            },
            Transmission = new TransmissionInfo
            {
                PendingAlerts = pendingAlerts,
                LastUploadOkAt = _forwarding.LastUploadOkAtUtc,
                LastUploadOkAgeSeconds = _forwarding.LastUploadOkAgeSeconds,
                LastUploadErrorCode = _forwarding.LastUploadErrorCode,
                RejectedAlerts = rejectedAlerts,
                AuditWriteFailures = _auditHealth?.Failures,
            },
            Modules = modules,
        };
    }

    /// <summary>
    /// Reads the DB-backed facts in a single scope: the backlog of alerts still waiting
    /// to upload, the alerts the GRID refused over the last 24 h, and the free space on
    /// the volume that holds the database.
    /// </summary>
    private async Task<(int? PendingAlerts, int? RejectedAlerts, long? DbVolumeFreeBytes)> QueryDbStateAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AgentDbContext>();

            int pending = await db.PendingAlertUploads
                .CountAsync(
                    p => p.Status == PendingUploadStatus.Pending || p.Status == PendingUploadStatus.Uploading,
                    ct)
                .ConfigureAwait(false);

            // Rolling 24 h, approximated on FirstAttemptAt (when the alert was first sent), not
            // on the moment the GRID refused it: a 400/422 is not retried, so the refusal comes
            // on the first attempt or within its retries, minutes later at most. The window is
            // on the wall clock (UTC) because it must survive a restart, which the monotonic
            // clock does not.
            DateTime since = DateTime.UtcNow.AddHours(-24);
            int rejected = await db.PendingAlertUploads
                .CountAsync(p => p.Status == PendingUploadStatus.FailedValidation && p.FirstAttemptAt >= since, ct)
                .ConfigureAwait(false);

            long? dbFree = FreeBytes(ExtractDataSource(db.Database.GetConnectionString()));
            return (pending, rejected, dbFree);
        }
        catch (Exception ex)
        {
            // Never let a diagnostics read break the heartbeat itself. What could not be read
            // is unknown (null), never a reassuring 0: pending_alerts = 0 would read "queue
            // empty" and could turn the verdict green; the GRID shows null as unavailable and,
            // past 300 s, as partial / transmission_unknown.
            _logger.LogWarning(ex, "Could not read local DB state for heartbeat");
            return (null, null, null);
        }
    }

    /// <summary>Free bytes on the volume holding <paramref name="path"/>, or 0 if it cannot be read.</summary>
    /// <summary>Free bytes on the volume holding <paramref name="path"/>; null if it cannot be measured, never 0.</summary>
    private static long? FreeBytes(string? path)
    {
        try
        {
            string? root = string.IsNullOrWhiteSpace(path) ? null : Path.GetPathRoot(Path.GetFullPath(path));
            if (string.IsNullOrEmpty(root))
            {
                return null;
            }

            return new DriveInfo(root).AvailableFreeSpace;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Pulls the file path out of a "Data Source=..." SQLite connection string.</summary>
    private static string? ExtractDataSource(string? connectionString)
    {
        if (string.IsNullOrEmpty(connectionString))
        {
            return null;
        }

        const string prefix = "Data Source=";
        int idx = connectionString.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
        {
            return null;
        }

        string rest = connectionString[(idx + prefix.Length)..];
        int semicolon = rest.IndexOf(';');
        return (semicolon >= 0 ? rest[..semicolon] : rest).Trim();
    }

    private static string ToWireState(ModuleState state) => state switch
    {
        ModuleState.Active => "active",
        ModuleState.Inactive => "inactive",
        ModuleState.DisabledByConfig => "disabled_by_config",
        ModuleState.Degraded => "degraded",
        _ => "inactive",
    };

    /// <summary>Spreads heartbeats by ±20% so fleets never arrive in lockstep.</summary>
    private static int WithJitter(int baseSeconds)
    {
        double factor = 1.0 + ((Random.Shared.NextDouble() * 0.4) - 0.2); // [0.8, 1.2]
        return Math.Max(1, (int)Math.Round(baseSeconds * factor));
    }

    /// <summary>Exponential backoff on repeated failures, capped at <see cref="MaxBackoff"/>.</summary>
    private static int Backoff(int baseSeconds, int consecutiveFailures)
    {
        double multiplier = Math.Pow(2, Math.Min(consecutiveFailures, 10));
        double seconds = baseSeconds * multiplier;
        return (int)Math.Min(seconds, MaxBackoff.TotalSeconds);
    }
}
