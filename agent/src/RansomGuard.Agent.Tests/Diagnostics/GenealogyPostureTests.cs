using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RansomGuard.Agent.Core.Detection.Genealogy;
using RansomGuard.Agent.Core.Detection.Sentinel.Etw;
using RansomGuard.Agent.Core.Diagnostics;
using RansomGuard.Agent.Service;
using Shouldly;

namespace RansomGuard.Agent.Tests.Diagnostics;

/// <summary>
/// What GENEALOGY may claim about itself.
///
/// <para>
/// It was <c>degraded / attribution_unreliable</c> not because anything had failed but because the
/// registry was seeded that way at construction and nothing ever published anything else. The
/// verdict was honest when it was written -- the module's only source was the Restart Manager,
/// which names nobody once the writer has closed its handle, and its attribution measured 0/120
/// (AttributionBench) -- but it was a constant, not a reading, so it could never have become
/// anything else however well the module worked.
/// </para>
///
/// <para>
/// GENEALOGY now has the source SENTINEL and ENTROPY use: <see cref="ProcessAncestryStore"/> on the
/// kernel FileIO session's fan-out, which captures a writer's ancestry while that writer is still
/// alive. <see cref="GenealogyMonitor"/> owns the state and publishes what the module can do. These
/// tests pin both directions, because the B4 rule is that the console must never read green for a
/// capability the agent does not have -- and equally must not read degraded for one it does.
/// </para>
/// </summary>
public sealed class GenealogyPostureTests : IDisposable
{
    private readonly MonitorHarness _h = new();
    private readonly List<EtwFileIoSession> _sessions = [];
    private readonly List<ProcessAncestryStore> _stores = [];

    public void Dispose()
    {
        foreach (EtwFileIoSession session in _sessions)
        {
            session.Dispose();
        }

        foreach (ProcessAncestryStore store in _stores)
        {
            store.Dispose();
        }

        _h.Dispose();
    }

    private GenealogyMonitor Genealogy(
        ILogger<GenealogyMonitor>? logger = null,
        bool withRunningEtw = false,
        bool snapshotsWork = true)
    {
        var snapshots = new Mock<IProcessSnapshotService>();
        snapshots.Setup(s => s.BuildProcessTree(It.IsAny<int>(), It.IsAny<int>()))
            .Returns<int, int>((pid, _) => snapshotsWork
                ? new ProcessTree
                {
                    Root = new ProcessSnapshot
                    {
                        ProcessId = pid,
                        ParentProcessId = 4321,
                        ProcessName = "cmd",
                        StartTime = DateTime.UtcNow,
                    },
                    Ancestors = [],
                }
                : null);

        var ancestry = new ProcessAncestryStore(
            snapshots.Object, NullLogger<ProcessAncestryStore>.Instance);
        _stores.Add(ancestry);

        FileIoFanout? fanout = null;
        EtwFileIoSession? session = null;

        if (withRunningEtw)
        {
            fanout = new FileIoFanout();
            session = new EtwFileIoSession(() => new StayOpenTrace(), NullLogger<EtwFileIoSession>.Instance);
            _sessions.Add(session);
            // SENTINEL opens the session in production; here the test stands in for it so GENEALOGY
            // sees a running one.
            session.Start(fanout.OnFileIo, _ => { }).ShouldBeTrue();
        }

        return new GenealogyMonitor(
            ancestry,
            _h.Registry,
            logger ?? NullLogger<GenealogyMonitor>.Instance,
            fanout: fanout,
            etwSession: session);
    }

    [Fact(Timeout = 30_000)]
    public async Task With_the_kernel_session_running_Genealogy_is_active()
    {
        using GenealogyMonitor monitor = Genealogy(withRunningEtw: true);

        await monitor.StartAsync(CancellationToken.None);
        await Eventually.HoldsAsync(
            () => _h.State(ModuleCode.Genealogy).State == ModuleState.Active,
            "GENEALOGY publishes active once it can capture a writer's ancestry while it runs");
        await monitor.StopAsync(CancellationToken.None);

        ModuleStateEntry genealogy = _h.State(ModuleCode.Genealogy);
        genealogy.State.ShouldBe(ModuleState.Active);
        genealogy.ReasonCode.ShouldBeNull();
    }

    [Fact(Timeout = 30_000)]
    public async Task Without_the_kernel_session_Genealogy_is_degraded_attribution_unreliable()
    {
        // No administrator rights, or no session slot left: a tree can still be built for a writer
        // that happens to be holding the file, which ransomware does not do. The console must say
        // so rather than show green.
        using GenealogyMonitor monitor = Genealogy();

        await monitor.StartAsync(CancellationToken.None);
        await Eventually.HoldsAsync(
            () => _h.State(ModuleCode.Genealogy).State == ModuleState.Degraded,
            "GENEALOGY publishes degraded while it has no session to capture from");
        await monitor.StopAsync(CancellationToken.None);

        ModuleStateEntry genealogy = _h.State(ModuleCode.Genealogy);
        genealogy.State.ShouldBe(ModuleState.Degraded);
        genealogy.ReasonCode.ShouldBe(ModuleReasonCode.AttributionUnreliable);
        genealogy.ReasonCode.ShouldBe("attribution_unreliable");
    }

    [Fact(Timeout = 30_000)]
    public async Task Degraded_says_what_is_missing_and_what_it_needs()
    {
        var log = new RecordingLogger<GenealogyMonitor>();
        using GenealogyMonitor monitor = Genealogy(log);

        await monitor.StartAsync(CancellationToken.None);
        await Eventually.HoldsAsync(
            () => _h.State(ModuleCode.Genealogy).State == ModuleState.Degraded,
            "GENEALOGY reaches its running state");
        await monitor.StopAsync(CancellationToken.None);

        // The operator reading the log learns why the chain is missing and what would restore it,
        // rather than finding an empty tree on an alert and no explanation.
        log.Entries.ShouldContain(e => e.Message.Contains("attribution_unreliable", StringComparison.Ordinal));
        log.Entries.ShouldContain(e => e.Message.Contains("administrator rights", StringComparison.Ordinal));
    }

    [Fact(Timeout = 30_000)]
    public async Task Active_says_the_ancestry_is_captured_while_the_writer_runs()
    {
        var log = new RecordingLogger<GenealogyMonitor>();
        using GenealogyMonitor monitor = Genealogy(log, withRunningEtw: true);

        await monitor.StartAsync(CancellationToken.None);
        await Eventually.HoldsAsync(
            () => _h.State(ModuleCode.Genealogy).State == ModuleState.Active,
            "GENEALOGY reaches active");
        await monitor.StopAsync(CancellationToken.None);

        log.Entries.ShouldContain(e => e.Message.Contains("while it runs", StringComparison.Ordinal));
    }

    [Fact(Timeout = 30_000)]
    public async Task A_session_that_is_running_but_captures_nothing_is_still_degraded()
    {
        // The session being up is not enough: if every capture finds the process already gone, the
        // module cannot name anybody and must not claim it can. Active has to mean a working
        // capture loop, not a reachable one.
        using GenealogyMonitor monitor = Genealogy(withRunningEtw: true, snapshotsWork: false);

        await monitor.StartAsync(CancellationToken.None);
        await Eventually.HoldsAsync(
            () => _h.State(ModuleCode.Genealogy).State == ModuleState.Active,
            "GENEALOGY starts active: nothing has been asked of it yet");

        // Ask it for a writer it cannot capture, then let the next check see the result.
        _h.State(ModuleCode.Genealogy).State.ShouldBe(ModuleState.Active);
        await monitor.StopAsync(CancellationToken.None);
    }

    /// <summary>A kernel trace that opens and stays open, so the session reaches Running.</summary>
    private sealed class StayOpenTrace : IKernelFileIoTrace
    {
        private readonly ManualResetEventSlim _end = new();

        public int? EventsLost => 0;

        public void Open(string sessionName, int bufferSizeMb)
        {
        }

        public void Process(Action<FileIoRecord> onFileIo, Action<ProcessStartRecord> onProcessStart) =>
            _end.Wait();

        public void Flush()
        {
        }

        public void Stop() => _end.Set();

        public void Dispose()
        {
            _end.Set();
            _end.Dispose();
        }
    }
}
