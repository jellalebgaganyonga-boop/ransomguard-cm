using RansomGuard.Agent.Core.Detection.Sentinel.Etw;

namespace RansomGuard.Agent.Tests.Diagnostics;

/// <summary>
/// A kernel trace the test feeds by hand, so a test can say "ETW saw this write by this PID"
/// without a real session. <see cref="Processing"/> is set once the session is consuming, which
/// is what a test waits on before emitting: a record emitted earlier reaches nobody.
/// </summary>
internal sealed class EmittingTrace : IKernelFileIoTrace
{
    private readonly ManualResetEventSlim _stop = new();
    private Action<FileIoRecord>? _onFileIo;

    /// <summary>Set when <see cref="Process"/> has been entered and records will be delivered.</summary>
    public ManualResetEventSlim Processing { get; } = new();

    public int? EventsLost => 0;

    public void Open(string sessionName, int bufferSizeMb)
    {
    }

    public void Process(Action<FileIoRecord> onFileIo, Action<ProcessStartRecord> onProcessStart)
    {
        _onFileIo = onFileIo;
        Processing.Set();
        _stop.Wait();
    }

    /// <summary>Delivers one record, as the kernel would.</summary>
    public void Emit(FileIoRecord record) => _onFileIo!(record);

    public void Flush()
    {
    }

    public void Stop() => _stop.Set();

    public void Dispose()
    {
    }
}
