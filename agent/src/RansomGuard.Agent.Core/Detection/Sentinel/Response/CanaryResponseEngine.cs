using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RansomGuard.Agent.Core.Communication;
using RansomGuard.Agent.Core.Configuration;
using RansomGuard.Agent.Core.Detection.Sentinel.Etw;
using RansomGuard.Agent.Core.Persistence;
using RansomGuard.Agent.Core.Persistence.Entities;
using RansomGuard.Agent.Core.Persistence.Repositories;

namespace RansomGuard.Agent.Core.Detection.Sentinel.Response;

/// <summary>Closed reasons for which a canary writer was not suspended (ADR-026, 2a).</summary>
public static class ResponseReason
{
    /// <summary>Sentinel:Response absent, or SuspendProcess false.</summary>
    public const string DisabledByConfig = "disabled_by_config";

    /// <summary>The attribution names nobody (unavailable or ambiguous): nobody is suspended.</summary>
    public const string NotResolved = "not_resolved";

    /// <summary>PID 0 or 4: the system.</summary>
    public const string SystemPid = "system_pid";

    /// <summary>The agent's own process.</summary>
    public const string AgentPid = "agent_pid";

    /// <summary>The process holding the PID now was created at another time: not the writer.</summary>
    public const string PidReused = "pid_reused";

    /// <summary>The writer has exited before the action.</summary>
    public const string ProcessExited = "process_exited";

    /// <summary>The process cannot be opened for suspension (protected).</summary>
    public const string AccessDenied = "access_denied";

    /// <summary>The kernel call failed; the error is in the audit entry.</summary>
    public const string SuspendFailed = "suspend_failed";

    /// <summary>A console request names an alert this agent does not know.</summary>
    public const string UnknownAlert = "unknown_alert";
}

/// <summary>What the response engine did for one attribution.</summary>
/// <param name="Suspended">True when the writer is now suspended.</param>
/// <param name="Reason">Why not, from <see cref="ResponseReason"/>; null when suspended.</param>
/// <param name="SuspensionId">The persisted suspension when suspended.</param>
public sealed record ResponseOutcome(bool Suspended, string? Reason, Guid? SuspensionId = null);

/// <summary>
/// SENTINEL's response (ADR-026): suspends the process that wrote a canary, on a resolved
/// attribution only, after re-reading its (PID, creation time) pair; never PID 0/4, never the
/// agent. Without attribution nobody is suspended; a burst of unattributed canaries recommends
/// isolation to the operator (the isolation itself does not exist: AGT-ISO-001).
/// </summary>
public sealed class CanaryResponseEngine
{
    /// <summary>The creation time read before acting may differ from the attribution's by this much (ADR-025, 4b).</summary>
    public static readonly TimeSpan PairTolerance = TimeSpan.FromMilliseconds(50);

    /// <summary>The burst that recommends isolation: this many distinct canaries, unattributed, within <see cref="BurstWindow"/>.</summary>
    public const int BurstCanaries = 3;

    /// <summary>The burst window; also the minimum interval between two recommendations.</summary>
    public static readonly TimeSpan BurstWindow = TimeSpan.FromSeconds(60);

    /// <summary>Resume attempts before giving up.</summary>
    public const int ResumeAttempts = 3;

    private readonly IProcessSuspender _suspender;
    private readonly IServiceScopeFactory _scopes;
    private readonly IAlertForwardingQueue _alerts;
    private readonly SentinelResponseOptions? _options;
    private readonly ILogger<CanaryResponseEngine> _logger;
    private readonly TimeProvider _time;
    private readonly Func<int, DateTime?> _readStartTime;
    private readonly int _agentPid;
    private readonly TimeSpan _resumeRetryDelay;
    private readonly object _burstGate = new();
    private readonly List<(string Path, DateTime At)> _unattributed = [];
    private DateTime? _lastRecommendation;

    /// <summary>Creates the engine.</summary>
    /// <param name="suspender">The kernel calls.</param>
    /// <param name="scopes">For the database and the audit.</param>
    /// <param name="alerts">The queue to the GRID: the response is an alert the console shows.</param>
    /// <param name="options">Sentinel:Response; null or SuspendProcess false: the engine never suspends.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="time">The clock.</param>
    /// <param name="readStartTime">The creation time of a running process now; null when it is gone. The OS by default.</param>
    /// <param name="agentPid">The agent's own PID; this process by default.</param>
    /// <param name="resumeRetryDelay">Between two resume attempts; 10 s by default.</param>
    public CanaryResponseEngine(
        IProcessSuspender suspender,
        IServiceScopeFactory scopes,
        IAlertForwardingQueue alerts,
        SentinelResponseOptions? options,
        ILogger<CanaryResponseEngine> logger,
        TimeProvider? time = null,
        Func<int, DateTime?>? readStartTime = null,
        int? agentPid = null,
        TimeSpan? resumeRetryDelay = null)
    {
        _suspender = suspender;
        _scopes = scopes;
        _alerts = alerts;
        _options = options;
        _logger = logger;
        _time = time ?? TimeProvider.System;
        _readStartTime = readStartTime ?? ReadStartTime;
        _agentPid = agentPid ?? Environment.ProcessId;
        _resumeRetryDelay = resumeRetryDelay ?? TimeSpan.FromSeconds(10);
    }

    /// <summary>True when a resolved attribution leads to a suspension.</summary>
    public bool Enabled => _options?.SuspendProcess == true;

    /// <summary>Decides and acts on one attribution.</summary>
    /// <param name="attribution">Who wrote the canary, from the attributor.</param>
    /// <param name="canaryPath">The canary.</param>
    /// <param name="alertId">The local canary alert.</param>
    /// <param name="clientMessageId">The GRID id of that alert.</param>
    /// <param name="ct">Cancellation.</param>
    public async Task<ResponseOutcome> RespondAsync(
        AttributionOutcome attribution, string canaryPath, Guid alertId, string clientMessageId, CancellationToken ct = default)
    {
        if (!Enabled)
        {
            return new ResponseOutcome(false, ResponseReason.DisabledByConfig);
        }

        if (attribution.State != AttributionState.Resolved
            || attribution.ProcessId is not int pid
            || attribution.ProcessCreatedAtUtc is not DateTime created)
        {
            _logger.LogWarning("SENTINEL RESPONSE: nobody suspended for {Canary}, attribution {State} ({Reason})",
                canaryPath, attribution.StateCode, attribution.Reason);
            await RecordUnattributedAsync(canaryPath, ct).ConfigureAwait(false);
            return new ResponseOutcome(false, ResponseReason.NotResolved);
        }

        // The attributor already leaves these out; the engine does not trust a caller for it.
        if (pid is 0 or 4)
        {
            return new ResponseOutcome(false, ResponseReason.SystemPid);
        }

        if (pid == _agentPid)
        {
            _logger.LogError("SENTINEL RESPONSE: the agent itself (PID {Pid}) is named as the writer of {Canary}; never suspended", pid, canaryPath);
            return new ResponseOutcome(false, ResponseReason.AgentPid);
        }

        // The pair is re-read just before acting (ADR-025, 4b): a reused PID is never suspended.
        DateTime? now = _readStartTime(pid);
        if (now is null)
        {
            _logger.LogWarning("SENTINEL RESPONSE: PID {Pid} exited before the response for {Canary}", pid, canaryPath);
            return new ResponseOutcome(false, ResponseReason.ProcessExited);
        }

        if ((now.Value - created).Duration() > PairTolerance)
        {
            _logger.LogWarning(
                "SENTINEL RESPONSE: PID {Pid} now belongs to a process created {Now:o}, the writer was created {Created:o}: reused, not suspended",
                pid, now.Value, created);
            return new ResponseOutcome(false, ResponseReason.PidReused);
        }

        string who = $"PID {pid} created {created:o} {attribution.ProcessName ?? "(unnamed)"} {attribution.ImagePath ?? ""}".TrimEnd();
        await using AsyncServiceScope scope = _scopes.CreateAsyncScope();
        var audit = scope.ServiceProvider.GetRequiredService<IAuditLogRepository>();

        // The intent before the action (AGT-AUD-002): the trace exists even if the agent dies mid-call.
        await audit.AppendAsync("ProcessSuspendIntent", $"{who} for {canaryPath}", "CanaryAlert", alertId, CancellationToken.None).ConfigureAwait(false);

        SuspendOutcome outcome = _suspender.Suspend(pid, out string? error);
        DateTime at = _time.GetUtcNow().UtcDateTime;
        if (outcome != SuspendOutcome.Done)
        {
            string reason = outcome switch
            {
                SuspendOutcome.NotFound => ResponseReason.ProcessExited,
                SuspendOutcome.AccessDenied => ResponseReason.AccessDenied,
                _ => ResponseReason.SuspendFailed,
            };
            _logger.LogError("SENTINEL RESPONSE: suspension of {Who} failed: {Reason} {Error}", who, reason, error);
            await audit.AppendAsync("ProcessSuspendFailed", $"{who}: {reason} {error}", "CanaryAlert", alertId, CancellationToken.None).ConfigureAwait(false);
            _alerts.TryEnqueue(AlertMapper.FromSuspensionFailed(
                Guid.NewGuid(), pid, created, attribution.ProcessName, attribution.ImagePath, canaryPath, reason, error, clientMessageId, at));
            return new ResponseOutcome(false, reason);
        }

        var record = new SuspendedProcess
        {
            ProcessId = pid,
            ProcessCreatedAt = created,
            ProcessName = attribution.ProcessName,
            ImagePath = attribution.ImagePath,
            CanaryPath = canaryPath,
            AlertId = alertId,
            ClientMessageId = clientMessageId,
            SuspendedAt = at,
        };
        _logger.LogCritical("SENTINEL RESPONSE: {Who} SUSPENDED after writing canary {Canary} (suspension {Id})", who, canaryPath, record.Id);

        // The console first: the response is an alert, by the queue that persists it itself.
        _alerts.TryEnqueue(AlertMapper.FromProcessSuspension(record));
        try
        {
            var db = scope.ServiceProvider.GetRequiredService<AgentDbContext>();
            db.SuspendedProcesses.Add(record);
            await db.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // The process is suspended and the console told; the local record is what failed.
            _logger.LogError(ex, "SENTINEL RESPONSE: suspension {Id} done but its record failed", record.Id);
        }

        await audit.AppendAsync("ProcessSuspended", $"{who} for {canaryPath}; suspension {record.Id}", "CanaryAlert", alertId, CancellationToken.None).ConfigureAwait(false);
        return new ResponseOutcome(true, null, record.Id);
    }

    /// <summary>
    /// A suspension asked by the console (J1 § 1.3) on an alert of this agent, with the PID and
    /// creation time the GRID took from the alert's resolved attribution. Same rules as the
    /// automatic one; a process already suspended is not suspended twice.
    /// </summary>
    /// <param name="pid">The attributed writer.</param>
    /// <param name="processCreatedAt">Its creation time (UTC), the other half of its identity.</param>
    /// <param name="alertClientMessageId">The GRID id of the canary alert.</param>
    /// <param name="by">Who asks (a command id).</param>
    /// <param name="ct">Cancellation.</param>
    public async Task<ResponseOutcome> SuspendOnRequestAsync(
        int pid, DateTime processCreatedAt, string alertClientMessageId, string by, CancellationToken ct = default)
    {
        Guid alertId;
        string canaryPath;
        await using (AsyncServiceScope scope = _scopes.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AgentDbContext>();
            SuspendedProcess? already = await db.SuspendedProcesses
                .FirstOrDefaultAsync(s => s.ProcessId == pid && s.ProcessCreatedAt == processCreatedAt && s.State == SuspensionState.Suspended, ct)
                .ConfigureAwait(false);
            if (already is not null)
            {
                _logger.LogInformation("SENTINEL RESPONSE: PID {Pid} already suspended (suspension {Id}); request by {By} changes nothing", pid, already.Id, by);
                return new ResponseOutcome(true, null, already.Id);
            }

            // The local alert: its GRID id is derived from its id, so the recent ones are scanned.
            DateTime since = _time.GetUtcNow().UtcDateTime.AddDays(-7);
            var candidates = await db.CanaryAlerts
                .Where(a => a.DetectedAt >= since)
                .Select(a => new { a.Id, a.CanaryPath })
                .ToListAsync(ct)
                .ConfigureAwait(false);
            var match = candidates.FirstOrDefault(c => AlertMapper.DeterministicId(c.Id) == alertClientMessageId);
            if (match is null)
            {
                _logger.LogWarning("SENTINEL RESPONSE: suspension asked by {By} for an alert this agent does not know ({ClientMessageId})", by, alertClientMessageId);
                return new ResponseOutcome(false, ResponseReason.UnknownAlert);
            }

            (alertId, canaryPath) = (match.Id, match.CanaryPath);
        }

        string? name = null;
        string? image = null;
        try
        {
            using Process process = Process.GetProcessById(pid);
            name = process.ProcessName;
            try
            {
                image = process.MainModule?.FileName;
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                // protected or exiting: the image stays unknown
            }
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // gone: RespondAsync says process_exited after the pair re-read
        }

        _logger.LogWarning("SENTINEL RESPONSE: suspension of PID {Pid} asked by {By} for {Canary}", pid, by, canaryPath);
        var attribution = new AttributionOutcome(AttributionState.Resolved, pid, processCreatedAt, name, image);
        return await RespondAsync(attribution, canaryPath, alertId, alertClientMessageId, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Resumes a suspended process, on request. The pair is checked again: the PID may belong to
    /// someone else by now. Three attempts; after the third failure the record reads ResumeFailed
    /// and the process stays suspended until the machine restarts (AGT-RESP-001).
    /// </summary>
    /// <param name="suspensionId">The suspension.</param>
    /// <param name="by">Who asks (a command id, an operator).</param>
    /// <param name="ct">Cancellation.</param>
    /// <returns>True when resumed, or already resumed or ended.</returns>
    public async Task<bool> ResumeAsync(Guid suspensionId, string by, CancellationToken ct = default)
    {
        await using AsyncServiceScope scope = _scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AgentDbContext>();
        var audit = scope.ServiceProvider.GetRequiredService<IAuditLogRepository>();
        SuspendedProcess? record = await db.SuspendedProcesses.FirstOrDefaultAsync(s => s.Id == suspensionId, ct).ConfigureAwait(false);
        if (record is null)
        {
            _logger.LogWarning("SENTINEL RESPONSE: resume of unknown suspension {Id} asked by {By}", suspensionId, by);
            return false;
        }

        if (record.State != SuspensionState.Suspended)
        {
            return record.State is SuspensionState.Resumed or SuspensionState.Ended;
        }

        string who = $"PID {record.ProcessId} created {record.ProcessCreatedAt:o} {record.ProcessName}";
        DateTime? now = _readStartTime(record.ProcessId);
        if (now is null || (now.Value - record.ProcessCreatedAt).Duration() > PairTolerance)
        {
            // Nothing to resume: the writer is gone (the PID may be someone else's: untouched).
            record.State = SuspensionState.Ended;
            record.ResumedAt = _time.GetUtcNow().UtcDateTime;
            record.ResumedBy = by;
            await db.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
            await audit.AppendAsync("SuspendedProcessEnded", $"{who}: gone before the resume asked by {by}", "SuspendedProcess", record.Id, CancellationToken.None).ConfigureAwait(false);
            return true;
        }

        await audit.AppendAsync("ProcessResumeIntent", $"{who} by {by}", "SuspendedProcess", record.Id, CancellationToken.None).ConfigureAwait(false);
        string? error = null;
        for (int attempt = 1; attempt <= ResumeAttempts; attempt++)
        {
            SuspendOutcome outcome = _suspender.Resume(record.ProcessId, out error);
            if (outcome == SuspendOutcome.Done)
            {
                record.State = SuspensionState.Resumed;
                record.ResumedAt = _time.GetUtcNow().UtcDateTime;
                record.ResumedBy = by;
                await db.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
                _logger.LogWarning("SENTINEL RESPONSE: {Who} resumed by {By}", who, by);
                await audit.AppendAsync("ProcessResumed", $"{who} by {by} (attempt {attempt})", "SuspendedProcess", record.Id, CancellationToken.None).ConfigureAwait(false);
                return true;
            }

            _logger.LogError("SENTINEL RESPONSE: resume of {Who} failed (attempt {Attempt}): {Outcome} {Error}", who, attempt, outcome, error);
            if (attempt < ResumeAttempts)
            {
                await Task.Delay(_resumeRetryDelay, _time, ct).ConfigureAwait(false);
            }
        }

        record.State = SuspensionState.ResumeFailed;
        record.LastError = error;
        await db.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
        _logger.LogCritical("SENTINEL RESPONSE: {Who} could not be resumed after {Attempts} attempts; it stays suspended until the machine restarts", who, ResumeAttempts);
        await audit.AppendAsync("ProcessResumeFailed", $"{who} by {by}: {error}", "SuspendedProcess", record.Id, CancellationToken.None).ConfigureAwait(false);
        return false;
    }

    // Unattributed canaries in a row are an encryption in progress with nobody to suspend: the
    // operator is told to isolate (the agent cannot, AGT-ISO-001). Once per window.
    private async Task RecordUnattributedAsync(string canaryPath, CancellationToken ct)
    {
        DateTime now = _time.GetUtcNow().UtcDateTime;
        List<string> paths;
        lock (_burstGate)
        {
            _unattributed.RemoveAll(e => now - e.At > BurstWindow);
            _unattributed.Add((canaryPath, now));
            paths = _unattributed.Select(e => e.Path).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (paths.Count < BurstCanaries || (_lastRecommendation is DateTime last && now - last < BurstWindow))
            {
                return;
            }

            _lastRecommendation = now;
        }

        _logger.LogCritical("SENTINEL RESPONSE: {Count} canaries written in {Window}s with no attribution: isolation of this endpoint recommended (not automatic)",
            paths.Count, (int)BurstWindow.TotalSeconds);
        _alerts.TryEnqueue(AlertMapper.FromIsolationRecommended(Guid.NewGuid(), paths, now));
        await using AsyncServiceScope scope = _scopes.CreateAsyncScope();
        var audit = scope.ServiceProvider.GetRequiredService<IAuditLogRepository>();
        await audit.AppendAsync("IsolationRecommended", $"{paths.Count} unattributed canaries in {BurstWindow.TotalSeconds}s: {string.Join("; ", paths)}",
            cancellationToken: CancellationToken.None).ConfigureAwait(false);
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
