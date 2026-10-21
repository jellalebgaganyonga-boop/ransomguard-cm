using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Diagnostics.Tracing.Session;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RansomGuard.Agent.Core.Persistence.Repositories;

namespace RansomGuard.Agent.Core.Detection.Sentinel.Etw;

/// <summary>Where the session stands.</summary>
public enum EtwSessionState
{
    /// <summary>Not started yet.</summary>
    NotStarted,

    /// <summary>Open, delivering events.</summary>
    Running,

    /// <summary>Could not start, or ended without being asked to: see <see cref="EtwUnavailableReason"/>.</summary>
    Unavailable,

    /// <summary>Stopped on the agent's request.</summary>
    Stopped,
}

/// <summary>Why the session is unavailable. Each reason is logged distinctly (ADR-025, 4c).</summary>
public enum EtwUnavailableReason
{
    /// <summary>Available.</summary>
    None,

    /// <summary>The agent is not an administrator: a kernel session is refused.</summary>
    AdminRightsMissing,

    /// <summary>The machine's system-logger session slots are all taken (Sysmon, Defender, another EDR...).</summary>
    NoSessionSlot,

    /// <summary>Any other failure to open the session.</summary>
    StartFailed,

    /// <summary>The session opened, then its event processing failed.</summary>
    ProcessingFailed,

    /// <summary>
    /// The session ended while the agent had not asked it to: stopped from outside (<c>logman stop
    /// ... -ets</c>, an anti-EDR tool). A known technique to blind an EDR, and a tampering signal.
    /// </summary>
    StoppedExternally,
}

/// <summary>
/// SENTINEL's kernel FileIO session (ADR-025, commit 1 of 8): its own session, separate from
/// EXFIL WATCH's; 64 MB of buffers; flushed every 100 ms, which brings the real-time delivery
/// delay at rest from 577/1 067 ms to 97/203 ms (p50/p95) for a cost lost in the noise
/// (EtwFileIoProbe, CI run 38809979940).
///
/// It opens, delivers and stops, and it never claims to run when it does not:
/// - when it cannot open, it says why (no administrator rights, no session slot left, anything else);
/// - when its processing ends without a stop from the agent -- the session was stopped from
///   outside, or failed -- it becomes unavailable, logs Critical (stopped from outside), writes an
///   <see cref="StoppedExternallyAction"/> audit entry, and tries again after 30 s, 2 min, then
///   every 10 min, each attempt logged;
/// - a session of the same name left by a previous agent (killed, crashed: an ETW session outlives
///   its process) is stopped and recreated, with a Warning.
///
/// The callbacks run on the session's own thread and must never block it (AGT-AUD-002): the filter
/// of commit 2 is the only subscriber.
/// </summary>
public sealed class EtwFileIoSession : IDisposable
{
    /// <summary>The session's name: one per machine, distinct from EXFIL WATCH's.</summary>
    public const string SessionName = "RansomGuard-Sentinel-FileIO";

    /// <summary>The real-time buffers, in MB (doubled before giving up under load, ADR-025, 4a).</summary>
    public const int BufferSizeMb = 64;

    /// <summary>Audit action written when the session ends without the agent asking.</summary>
    public const string StoppedExternallyAction = "EtwSessionStoppedExternally";

    /// <summary>The flush cadence retained by ADR-025.</summary>
    public static readonly TimeSpan FlushInterval = TimeSpan.FromMilliseconds(100);

    /// <summary>Delays before each restart attempt; the last one repeats.</summary>
    public static readonly IReadOnlyList<TimeSpan> RestartDelays =
        [TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(10)];

    // ERROR_NO_SYSTEM_RESOURCES (1450) as an HRESULT: what StartTrace returns when the machine's
    // system-logger sessions are all taken.
    internal const int NoSystemResourcesHResult = unchecked((int)0x800705AA);

    private readonly Func<IKernelFileIoTrace> _traceFactory;
    private readonly Func<IReadOnlyCollection<string>> _activeSessions;
    private readonly ILogger<EtwFileIoSession> _logger;
    private readonly TimeProvider _time;
    private readonly IServiceScopeFactory? _scopeFactory;
    private readonly object _gate = new();
    private Action<FileIoRecord>? _onFileIo;
    private Action<ProcessStartRecord>? _onProcessStart;
    private IKernelFileIoTrace? _trace;
    private ITimer? _flushTimer;
    private ITimer? _restartTimer;
    private Thread? _processing;
    private int _restartAttempt;
    private bool _stopRequested;
    private long _flushes;
    private volatile EtwSessionState _state = EtwSessionState.NotStarted;
    private volatile EtwUnavailableReason _reason = EtwUnavailableReason.None;

    /// <summary>Creates the session; nothing is opened before <see cref="Start"/>.</summary>
    /// <param name="traceFactory">The kernel trace (TraceEvent in production).</param>
    /// <param name="logger">Logger.</param>
    /// <param name="time">The clock of the flush and restart timers.</param>
    /// <param name="activeSessions">The machine's active trace sessions (orphan check, and logged when no slot is left).</param>
    /// <param name="scopeFactory">Scope factory for the audit repository; null writes no audit entry (tests of the session alone).</param>
    public EtwFileIoSession(
        Func<IKernelFileIoTrace> traceFactory,
        ILogger<EtwFileIoSession> logger,
        TimeProvider? time = null,
        Func<IReadOnlyCollection<string>>? activeSessions = null,
        IServiceScopeFactory? scopeFactory = null)
    {
        _traceFactory = traceFactory;
        _logger = logger;
        _time = time ?? TimeProvider.System;
        _activeSessions = activeSessions ?? ActiveSessionNames;
        _scopeFactory = scopeFactory;
    }

    /// <summary>Where the session stands.</summary>
    public EtwSessionState State => _state;

    /// <summary>Why it is unavailable; <see cref="EtwUnavailableReason.None"/> otherwise.</summary>
    public EtwUnavailableReason UnavailableReason => _reason;

    /// <summary>Events the session dropped, cumulative; null when unknown -- never 0 for "unknown".</summary>
    public int? EventsLost => _trace?.EventsLost;

    /// <summary>Flushes done since the start.</summary>
    public long Flushes => Interlocked.Read(ref _flushes);

    /// <summary>Restart attempts since the session last ended without the agent asking.</summary>
    public int RestartAttempts => Volatile.Read(ref _restartAttempt);

    /// <summary>
    /// Opens the session and starts delivering. Returns false, with <see cref="UnavailableReason"/>
    /// set and the reason logged, when the session cannot open.
    /// </summary>
    public bool Start(Action<FileIoRecord> onFileIo, Action<ProcessStartRecord> onProcessStart)
    {
        lock (_gate)
        {
            if (_state == EtwSessionState.Running)
            {
                return true;
            }

            _onFileIo = onFileIo;
            _onProcessStart = onProcessStart;
            _stopRequested = false;
            return TryOpen();
        }
    }

    /// <summary>Stops the session, its timers and its thread; idempotent.</summary>
    public void Stop()
    {
        IKernelFileIoTrace? trace;
        Thread? processing;
        lock (_gate)
        {
            _stopRequested = true;
            _flushTimer?.Dispose();
            _flushTimer = null;
            _restartTimer?.Dispose();
            _restartTimer = null;
            trace = _trace;
            processing = _processing;
            _trace = null;
            _processing = null;
            if (_state == EtwSessionState.Running)
            {
                _state = EtwSessionState.Stopped;
            }
        }

        if (trace is null)
        {
            return;
        }

        try
        {
            trace.Stop();
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            _logger.LogDebug(ex, "SENTINEL ETW FileIO session already stopped");
        }

        if (processing is not null && processing != Thread.CurrentThread && !processing.Join(TimeSpan.FromSeconds(5)))
        {
            _logger.LogWarning("SENTINEL ETW FileIO processing thread did not end within 5 s of the stop");
        }

        trace.Dispose();
        _logger.LogInformation("SENTINEL ETW FileIO session {Session} stopped", SessionName);
    }

    /// <inheritdoc />
    public void Dispose() => Stop();

    internal static EtwUnavailableReason Classify(Exception ex) => ex switch
    {
        UnauthorizedAccessException => EtwUnavailableReason.AdminRightsMissing,
        COMException { HResult: NoSystemResourcesHResult } => EtwUnavailableReason.NoSessionSlot,
        _ => EtwUnavailableReason.StartFailed,
    };

    private static IReadOnlyCollection<string> ActiveSessionNames()
    {
        try
        {
            return TraceEventSession.GetActiveSessionNames().ToList();
        }
        catch (Exception ex) when (ex is COMException or UnauthorizedAccessException)
        {
            return []; // a diagnostic: its absence never blocks the session
        }
    }

    // Under _gate. Opens a new trace and starts its thread and flush timer.
    private bool TryOpen()
    {
        // An ETW session outlives the process that created it: after a kill or a crash of the agent
        // the session of this name can still exist, holding one of the machine's 8 slots.
        // TraceEvent 3.2.2, new TraceEventSession(name) with the default TraceEventSessionOptions.Create:
        // "stop and recreate it if it already exists" (NoRestartOnCreate would fail instead). The
        // Etw category test EtwFileIoSessionLiveTests.An_orphan_session_left_by_a_killed_agent_is_stopped_and_recreated
        // proves it on a real session: the recreated one has this session's 64 MB, not the orphan's.
        if (_activeSessions().Contains(SessionName, StringComparer.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "SENTINEL ETW FileIO (etw_orphan_session_replaced): a session {Session} left by a previous agent (killed or crashed) was found; it is stopped and recreated",
                SessionName);
        }

        IKernelFileIoTrace trace = _traceFactory();
        try
        {
            trace.Open(SessionName, BufferSizeMb);
        }
        catch (Exception ex)
        {
            trace.Dispose();
            Unavailable(Classify(ex), ex);
            return false;
        }

        _trace = trace;
        _reason = EtwUnavailableReason.None;
        _state = EtwSessionState.Running;
        Action<FileIoRecord> onFileIo = _onFileIo!;
        Action<ProcessStartRecord> onProcessStart = _onProcessStart!;
        _processing = new Thread(() => Process(trace, onFileIo, onProcessStart))
        {
            IsBackground = true,
            Name = "SENTINEL ETW FileIO",
        };
        _processing.Start();
        _flushTimer = _time.CreateTimer(_ => Flush(trace), null, FlushInterval, FlushInterval);
        _logger.LogInformation(
            "SENTINEL ETW FileIO session {Session} started ({BufferMb} MB, flush every {FlushMs} ms)",
            SessionName, BufferSizeMb, FlushInterval.TotalMilliseconds);
        return true;
    }

    private void Process(IKernelFileIoTrace trace, Action<FileIoRecord> onFileIo, Action<ProcessStartRecord> onProcessStart)
    {
        Exception? failure = null;
        try
        {
            trace.Process(onFileIo, onProcessStart);
        }
        catch (Exception ex)
        {
            failure = ex;
        }

        lock (_gate)
        {
            if (_stopRequested || !ReferenceEquals(_trace, trace))
            {
                return; // the agent stopped it: the expected way out
            }

            // Process() ended while nobody asked: returning normally means the session was stopped
            // from outside; throwing means it failed. Either way, no event arrives any more.
            _flushTimer?.Dispose();
            _flushTimer = null;
            _trace = null;
            _processing = null;
            trace.Dispose();
            Unavailable(failure is null ? EtwUnavailableReason.StoppedExternally : EtwUnavailableReason.ProcessingFailed, failure);
            if (failure is null)
            {
                _ = Task.Run(() => AuditStoppedExternallyAsync(RestartDelays[0]));
            }

            ScheduleRestart();
        }
    }

    // Under _gate.
    private void ScheduleRestart()
    {
        TimeSpan delay = RestartDelays[Math.Min(_restartAttempt, RestartDelays.Count - 1)];
        _restartTimer?.Dispose();
        _restartTimer = _time.CreateTimer(_ => Restart(), null, delay, Timeout.InfiniteTimeSpan);
        _logger.LogWarning(
            "SENTINEL ETW FileIO session: restart attempt {Attempt} in {Delay}",
            _restartAttempt + 1, delay);
    }

    private void Restart()
    {
        lock (_gate)
        {
            if (_stopRequested || _state == EtwSessionState.Running)
            {
                return;
            }

            _restartTimer?.Dispose();
            _restartTimer = null;
            _restartAttempt++;
            if (TryOpen())
            {
                _logger.LogWarning(
                    "SENTINEL ETW FileIO session restarted (attempt {Attempt})", _restartAttempt);
                _restartAttempt = 0;
                return;
            }

            _logger.LogWarning(
                "SENTINEL ETW FileIO session restart attempt {Attempt} failed ({Reason})", _restartAttempt, _reason);
            ScheduleRestart();
        }
    }

    private void Flush(IKernelFileIoTrace trace)
    {
        try
        {
            trace.Flush();
            Interlocked.Increment(ref _flushes);
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or ObjectDisposedException)
        {
            // The session is stopping between two ticks; the next tick does not come.
            _logger.LogDebug(ex, "SENTINEL ETW FileIO flush skipped");
        }
    }

    private void Unavailable(EtwUnavailableReason reason, Exception? ex)
    {
        _reason = reason;
        _state = EtwSessionState.Unavailable;
        switch (reason)
        {
            case EtwUnavailableReason.AdminRightsMissing:
                _logger.LogWarning(
                    "SENTINEL ETW FileIO unavailable (etw_admin_rights_missing): the agent is not an administrator; detection by FileSystemWatcher only, without attribution");
                break;
            case EtwUnavailableReason.NoSessionSlot:
                _logger.LogWarning(
                    "SENTINEL ETW FileIO unavailable (etw_no_session_slot): no system-logger session slot left; active sessions: {Sessions}",
                    string.Join(", ", _activeSessions()));
                break;
            case EtwUnavailableReason.StoppedExternally:
                _logger.LogCritical(
                    "SENTINEL ETW FileIO (etw_session_stopped_externally): session {Session} ended while the agent had not stopped it -- stopped from outside (logman, an anti-EDR tool?); file attribution and ETW detection are down until it restarts",
                    SessionName);
                break;
            case EtwUnavailableReason.ProcessingFailed:
                _logger.LogError(ex, "SENTINEL ETW FileIO unavailable (etw_processing_failed): event processing stopped");
                break;
            default:
                _logger.LogError(ex, "SENTINEL ETW FileIO unavailable (etw_start_failed)");
                break;
        }
    }

    private async Task AuditStoppedExternallyAsync(TimeSpan restartIn)
    {
        if (_scopeFactory is null)
        {
            return;
        }

        try
        {
            string details = JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["session"] = SessionName,
                ["reason"] = "etw_session_stopped_externally",
                ["restart_in_seconds"] = (int)restartIn.TotalSeconds,
            });
            await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
            var audit = scope.ServiceProvider.GetRequiredService<IAuditLogRepository>();
            await audit.AppendAsync(StoppedExternallyAction, details, entityType: "Module", cancellationToken: CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception auditException)
        {
            // The stop itself is already in the state and the Critical log.
            _logger.LogError(auditException, "Could not write the {Action} audit entry", StoppedExternallyAction);
        }
    }
}
