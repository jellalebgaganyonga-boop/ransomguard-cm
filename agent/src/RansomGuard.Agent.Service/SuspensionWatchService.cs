using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RansomGuard.Agent.Core.Communication;
using RansomGuard.Agent.Core.Configuration;
using RansomGuard.Agent.Core.Detection.Sentinel.Response;
using RansomGuard.Agent.Core.Persistence;
using RansomGuard.Agent.Core.Persistence.Entities;
using RansomGuard.Agent.Core.Persistence.Repositories;

namespace RansomGuard.Agent.Service;

/// <summary>
/// Watches the processes SENTINEL suspended (ADR-026, 2c). Every minute: a suspended process that
/// is gone (exited, machine restarted, PID now someone else's) is closed as Ended; one still
/// waiting past Sentinel:Response:EscalationMinutes without a decision raises the "suspended, no
/// decision" alert again. Nothing is ever resumed here: the decision belongs to a person.
/// </summary>
public sealed class SuspensionWatchService : BackgroundService
{
    /// <summary>Between two sweeps.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory _scopes;
    private readonly IAlertForwardingQueue _alerts;
    private readonly SentinelResponseOptions? _options;
    private readonly ILogger<SuspensionWatchService> _logger;
    private readonly TimeProvider _time;
    private readonly Func<int, DateTime?> _readStartTime;

    /// <summary>Creates the service.</summary>
    /// <param name="scopes">For the database and the audit.</param>
    /// <param name="alerts">The queue to the GRID.</param>
    /// <param name="config">Agent configuration (Sentinel:Response).</param>
    /// <param name="logger">Logger.</param>
    /// <param name="time">The clock; the sweep timer runs on it.</param>
    /// <param name="readStartTime">The creation time of a running process now; null when gone. The OS by default.</param>
    public SuspensionWatchService(
        IServiceScopeFactory scopes,
        IAlertForwardingQueue alerts,
        IOptionsMonitor<AgentConfiguration> config,
        ILogger<SuspensionWatchService> logger,
        TimeProvider? time = null,
        Func<int, DateTime?>? readStartTime = null)
    {
        _scopes = scopes;
        _alerts = alerts;
        _options = config.CurrentValue.Sentinel?.Response;
        _logger = logger;
        _time = time ?? TimeProvider.System;
        _readStartTime = readStartTime ?? ReadStartTime;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_options is null)
        {
            _logger.LogInformation("Suspension watch idle: no Sentinel:Response section");
            return;
        }

        try
        {
            // At start too: the suspensions of a previous run are re-read and reported.
            await SweepAsync(stoppingToken);
            using var timer = new PeriodicTimer(Interval, _time);
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await SweepAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
    }

    /// <summary>One sweep; public for tests.</summary>
    public async Task SweepAsync(CancellationToken ct)
    {
        if (_options is null)
        {
            return;
        }

        try
        {
            await using AsyncServiceScope scope = _scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AgentDbContext>();
            var audit = scope.ServiceProvider.GetRequiredService<IAuditLogRepository>();
            DateTime now = _time.GetUtcNow().UtcDateTime;
            TimeSpan escalation = TimeSpan.FromMinutes(_options.EscalationMinutes);

            List<SuspendedProcess> waiting = await db.SuspendedProcesses
                .Where(s => s.State == SuspensionState.Suspended)
                .ToListAsync(ct);
            foreach (SuspendedProcess record in waiting)
            {
                string who = $"PID {record.ProcessId} created {record.ProcessCreatedAt:o} {record.ProcessName}";
                DateTime? start = _readStartTime(record.ProcessId);
                if (start is null || (start.Value - record.ProcessCreatedAt).Duration() > CanaryResponseEngine.PairTolerance)
                {
                    record.State = SuspensionState.Ended;
                    record.ResumedAt = now;
                    record.ResumedBy = "ended";
                    _logger.LogWarning("Suspended process gone: {Who} (suspension {Id})", who, record.Id);
                    await audit.AppendAsync("SuspendedProcessEnded", $"{who}: no longer running", "SuspendedProcess", record.Id, CancellationToken.None);
                    continue;
                }

                DateTime since = record.LastEscalationAt ?? record.SuspendedAt;
                if (now - since < escalation)
                {
                    continue;
                }

                record.EscalationCount++;
                record.LastEscalationAt = now;
                TimeSpan age = now - record.SuspendedAt;
                _logger.LogCritical("Suspended {Age} min with no decision: {Who} (suspension {Id}, escalation {N})",
                    (int)age.TotalMinutes, who, record.Id, record.EscalationCount);
                _alerts.TryEnqueue(AlertMapper.FromSuspensionEscalation(record, age, record.EscalationCount, now));
                await audit.AppendAsync("ProcessSuspendedNoDecision", $"{who}: suspended {(int)age.TotalMinutes} min, escalation {record.EscalationCount}",
                    "SuspendedProcess", record.Id, CancellationToken.None);
            }

            if (waiting.Count > 0)
            {
                await db.SaveChangesAsync(CancellationToken.None);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Suspension watch sweep failed");
        }
    }

    private static DateTime? ReadStartTime(int pid)
    {
        try
        {
            using Process process = Process.GetProcessById(pid);
            return process.StartTime.ToUniversalTime();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }
}
