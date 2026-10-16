using System.Runtime.InteropServices;
using Microsoft.Diagnostics.Tracing.Session;
using Microsoft.Extensions.Logging;

namespace RansomGuard.Agent.Core.Detection.Sentinel.Etw;

/// <summary>Where the session stands.</summary>
public enum EtwSessionState
{
    /// <summary>Not started yet.</summary>
    NotStarted,

    /// <summary>Open, delivering events.</summary>
    Running,

    /// <summary>Could not start, or failed while running: see <see cref="EtwUnavailableReason"/>.</summary>
    Unavailable,

    /// <summary>Stopped on request.</summary>
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
}

/// <summary>
/// SENTINEL's kernel FileIO session (ADR-025, commit 1 of 8): its own session, separate from
/// EXFIL WATCH's; 64 MB of buffers; flushed every 100 ms, which brings the real-time delivery
/// delay at rest from 577/1 067 ms to 97/203 ms (p50/p95) for a cost lost in the noise
/// (EtwFileIoProbe, CI run 38809979940).
///
/// It only opens, delivers and stops. When it cannot open, it says why -- no administrator rights,
/// no session slot left, anything else -- and SENTINEL keeps its FileSystemWatcher (wired in a
/// later commit). The callbacks run on the session's own thread and must never block it
/// (AGT-AUD-002): the filter of commit 2 is the only subscriber.
/// </summary>
public sealed class EtwFileIoSession : IDisposable
{
    /// <summary>The session's name: one per machine, distinct from EXFIL WATCH's.</summary>
    public const string SessionName = "RansomGuard-Sentinel-FileIO";

    /// <summary>The real-time buffers, in MB (doubled before giving up under load, ADR-025, 4a).</summary>
    public const int BufferSizeMb = 64;

    /// <summary>The flush cadence retained by ADR-025.</summary>
    public static readonly TimeSpan FlushInterval = TimeSpan.FromMilliseconds(100);

    // ERROR_NO_SYSTEM_RESOURCES (1450) as an HRESULT: what StartTrace returns when the machine's
    // system-logger sessions are all taken.
    internal const int NoSystemResourcesHResult = unchecked((int)0x800705AA);

    private readonly Func<IKernelFileIoTrace> _traceFactory;
    private readonly Func<IReadOnlyCollection<string>> _activeSessions;
    private readonly ILogger<EtwFileIoSession> _logger;
    private readonly TimeProvider _time;
    private readonly object _gate = new();
    private IKernelFileIoTrace? _trace;
    private ITimer? _flushTimer;
    private Thread? _processing;
    private long _flushes;
    private volatile EtwSessionState _state = EtwSessionState.NotStarted;
    private volatile EtwUnavailableReason _reason = EtwUnavailableReason.None;

    /// <summary>Creates the session; nothing is opened before <see cref="Start"/>.</summary>
    /// <param name="traceFactory">The kernel trace (TraceEvent in production).</param>
    /// <param name="logger">Logger.</param>
    /// <param name="time">The clock of the flush timer.</param>
    /// <param name="activeSessions">The machine's active trace sessions, logged when no slot is left.</param>
    public EtwFileIoSession(
        Func<IKernelFileIoTrace> traceFactory,
        ILogger<EtwFileIoSession> logger,
        TimeProvider? time = null,
        Func<IReadOnlyCollection<string>>? activeSessions = null)
    {
        _traceFactory = traceFactory;
        _logger = logger;
        _time = time ?? TimeProvider.System;
        _activeSessions = activeSessions ?? ActiveSessionNames;
    }

    /// <summary>Where the session stands.</summary>
    public EtwSessionState State => _state;

    /// <summary>Why it is unavailable; <see cref="EtwUnavailableReason.None"/> otherwise.</summary>
    public EtwUnavailableReason UnavailableReason => _reason;

    /// <summary>Events the session dropped, cumulative; null when unknown -- never 0 for "unknown".</summary>
    public int? EventsLost => _trace?.EventsLost;

    /// <summary>Flushes done since the start.</summary>
    public long Flushes => Interlocked.Read(ref _flushes);

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
    }

    /// <summary>Stops the session and waits for its thread; idempotent.</summary>
    public void Stop()
    {
        IKernelFileIoTrace? trace;
        Thread? processing;
        lock (_gate)
        {
            _flushTimer?.Dispose();
            _flushTimer = null;
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

        if (processing is not null && !processing.Join(TimeSpan.FromSeconds(5)))
        {
            _logger.LogWarning("SENTINEL ETW FileIO processing thread did not end within 5 s of the stop");
        }

        trace.Dispose();
        _logger.LogInformation("SENTINEL ETW FileIO session {Session} stopped", SessionName);
    }

    /// <inheritdoc />
    public void Dispose() => Stop();

    private static IReadOnlyCollection<string> ActiveSessionNames()
    {
        try
        {
            return TraceEventSession.GetActiveSessionNames().ToList();
        }
        catch (Exception ex) when (ex is COMException or UnauthorizedAccessException)
        {
            return []; // the list is a diagnostic: its absence never blocks the fallback
        }
    }

    internal static EtwUnavailableReason Classify(Exception ex) => ex switch
    {
        UnauthorizedAccessException => EtwUnavailableReason.AdminRightsMissing,
        COMException { HResult: NoSystemResourcesHResult } => EtwUnavailableReason.NoSessionSlot,
        _ => EtwUnavailableReason.StartFailed,
    };

    private void Process(IKernelFileIoTrace trace, Action<FileIoRecord> onFileIo, Action<ProcessStartRecord> onProcessStart)
    {
        try
        {
            trace.Process(onFileIo, onProcessStart);
        }
        catch (Exception ex)
        {
            lock (_gate)
            {
                if (_state != EtwSessionState.Running)
                {
                    return; // stopping
                }

                _flushTimer?.Dispose();
                _flushTimer = null;
                Unavailable(EtwUnavailableReason.ProcessingFailed, ex);
            }
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

    private void Unavailable(EtwUnavailableReason reason, Exception ex)
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
            case EtwUnavailableReason.ProcessingFailed:
                _logger.LogError(ex, "SENTINEL ETW FileIO unavailable (etw_processing_failed): event processing stopped");
                break;
            default:
                _logger.LogError(ex, "SENTINEL ETW FileIO unavailable (etw_start_failed)");
                break;
        }
    }
}
