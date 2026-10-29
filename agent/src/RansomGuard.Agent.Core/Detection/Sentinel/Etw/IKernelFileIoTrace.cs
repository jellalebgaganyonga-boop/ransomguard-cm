using System.Runtime.Versioning;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using Microsoft.Diagnostics.Tracing.Session;

namespace RansomGuard.Agent.Core.Detection.Sentinel.Etw;

/// <summary>
/// The kernel trace behind <see cref="EtwFileIoSession"/>: TraceEvent in production, a fake in the
/// tests. It only opens, delivers, flushes and stops; the lifecycle, the failure reasons and the
/// flush cadence belong to the session.
/// </summary>
public interface IKernelFileIoTrace : IDisposable
{
    /// <summary>
    /// Opens the kernel session. Throws as TraceEvent does: <see cref="UnauthorizedAccessException"/>
    /// without administrator rights, a <see cref="System.Runtime.InteropServices.COMException"/>
    /// carrying the Windows error otherwise (no session slot left, for one).
    /// </summary>
    void Open(string sessionName, int bufferSizeMb);

    /// <summary>Delivers the events, on the calling thread, until <see cref="Stop"/>.</summary>
    void Process(Action<FileIoRecord> onFileIo, Action<ProcessStartRecord> onProcessStart);

    /// <summary>Flushes the session's real-time buffers to the consumer.</summary>
    void Flush();

    /// <summary>Stops the session; <see cref="Process"/> returns.</summary>
    void Stop();

    /// <summary>Events the session dropped, cumulative; null when it cannot be read (session stopped).</summary>
    int? EventsLost { get; }
}

/// <summary>
/// The TraceEvent kernel session: FileIOInit + DiskFileIO + Process (ADR-025, decision 1).
/// FileIOInit carries the PID of the process that issues each request; DiskFileIO lets TraceEvent
/// resolve the names of files opened before the session (0 of 409 without it, 235 of 237 with);
/// Process gives the start time of the processes born during the session.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class TraceEventKernelFileIoTrace : IKernelFileIoTrace
{
    /// <summary>The keywords of the session.</summary>
    public const KernelTraceEventParser.Keywords Keywords =
        KernelTraceEventParser.Keywords.FileIOInit
        | KernelTraceEventParser.Keywords.DiskFileIO
        | KernelTraceEventParser.Keywords.Process;

    private TraceEventSession? _session;

    /// <inheritdoc />
    public int? EventsLost
    {
        get
        {
            try
            {
                return _session?.EventsLost;
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                return null; // readable only while the session runs
            }
        }
    }

    /// <inheritdoc />
    public void Open(string sessionName, int bufferSizeMb)
    {
        _session = new TraceEventSession(sessionName) { BufferSizeMB = bufferSizeMb, StopOnDispose = true };
        _session.EnableKernelProvider(Keywords);
    }

    /// <inheritdoc />
    public void Process(Action<FileIoRecord> onFileIo, Action<ProcessStartRecord> onProcessStart)
    {
        TraceEventSession session = _session ?? throw new InvalidOperationException("Open the session first.");
        KernelTraceEventParser kernel = session.Source.Kernel;

        kernel.FileIOCreate += e => onFileIo(new FileIoRecord(
            FileIoOperation.Create, e.FileName, e.ProcessID, e.TimeStamp.ToUniversalTime(), Disposition(e), 0));
        kernel.FileIOWrite += e => onFileIo(new FileIoRecord(
            FileIoOperation.Write, e.FileName, e.ProcessID, e.TimeStamp.ToUniversalTime(), 0, 0));
        kernel.FileIOSetInfo += e => onFileIo(new FileIoRecord(
            FileIoOperation.SetInfo, e.FileName, e.ProcessID, e.TimeStamp.ToUniversalTime(), 0, e.InfoClass));
        kernel.FileIODelete += e => onFileIo(new FileIoRecord(
            FileIoOperation.Delete, e.FileName, e.ProcessID, e.TimeStamp.ToUniversalTime(), 0, e.InfoClass));
        kernel.FileIORename += e => onFileIo(new FileIoRecord(
            FileIoOperation.Rename, e.FileName, e.ProcessID, e.TimeStamp.ToUniversalTime(), 0, e.InfoClass));
        kernel.ProcessStart += e => onProcessStart(new ProcessStartRecord(e.ProcessID, e.TimeStamp.ToUniversalTime()));

        session.Source.Process();
    }

    /// <inheritdoc />
    public void Flush() => _session?.Flush();

    /// <inheritdoc />
    public void Stop() => _session?.Stop();

    /// <inheritdoc />
    public void Dispose() => _session?.Dispose();

    // The NT disposition (ADR-025, rule A). TraceEvent extracts it from the high byte of the raw
    // CreateOptions and exposes it as CreateDisposition, under Win32 names but with the NT values:
    // CREATE_ALWAYS = 5 = FILE_OVERWRITE_IF, OPEN_EXISTING = 1 = FILE_OPEN (measured, EtwFileIoProbe,
    // run 38813859055). PayloadByName("CreateOptions") is already masked: its high byte is 0.
    private static uint Disposition(FileIOCreateTraceData e) => (uint)e.CreateDisposition;
}
