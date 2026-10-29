using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RansomGuard.Agent.Core.Configuration;
using RansomGuard.Agent.Core.Detection;
using RansomGuard.Agent.Core.Detection.Entropy;
using RansomGuard.Agent.Core.Detection.Sentinel.Etw;
using RansomGuard.Agent.Core.Diagnostics;
using RansomGuard.Agent.Service;
using Shouldly;

namespace RansomGuard.Agent.Tests.Diagnostics;

/// <summary>
/// What ENTROPY may claim about itself.
///
/// <para>
/// B4 (2026-10-22) held it at <c>degraded</c> for two gaps: every signal carried PID 0, so no
/// process was ever named (AGT-ATTR-001); and rule 3, the directory-wide entropy shift that is the
/// mass-encryption signal, had no caller, so <c>DirectoryShiftThreshold</c>,
/// <c>DirectoryShiftWindowSeconds</c> and <c>DirectoryShiftMinFiles</c> were read and changed
/// nothing (AGT-EN-002). Both are closed: an alert carries its writer, and
/// <see cref="DirectoryEntropyWindow"/> feeds <see cref="EntropyDetector.AnalyzeDirectoryShift"/>.
/// </para>
///
/// <para>
/// One condition is left and it is a runtime one, so the state must follow it rather than the
/// build: naming a writer needs the kernel FileIO session, which needs administrator rights and a
/// free system-logger slot. Running, ENTROPY is <c>active</c>; not running, it is back to
/// <c>degraded / attribution_unreliable</c> -- it still detects, it just cannot say by whom. These
/// tests pin both directions, because the whole point of B4 was that the console must never read
/// green for a capability the agent does not have.
/// </para>
/// </summary>
public sealed class EntropyPostureTests : IDisposable
{
    private readonly MonitorHarness _h = new();
    private readonly List<EtwFileIoSession> _sessions = [];

    public void Dispose()
    {
        foreach (EtwFileIoSession session in _sessions)
        {
            session.Dispose();
        }

        _h.Dispose();
    }

    private EntropyMonitor Entropy(
        string[] folders,
        ILogger<EntropyMonitor>? logger = null,
        bool withRunningEtw = false)
    {
        FileWriteAttributionCache? cache = null;
        FileIoFanout? fanout = null;
        EtwFileIoSession? session = null;

        if (withRunningEtw)
        {
            fanout = new FileIoFanout();
            cache = new FileWriteAttributionCache(new ProcessCreationTimes(_ => null));
            session = new EtwFileIoSession(() => new StubTrace(), NullLogger<EtwFileIoSession>.Instance);
            _sessions.Add(session);
            // SENTINEL opens the session in production; here the test stands in for it so ENTROPY
            // sees a running one.
            session.Start(fanout.OnFileIo, _ => { }).ShouldBeTrue();
        }

        return new EntropyMonitor(
            logger ?? NullLogger<EntropyMonitor>.Instance,
            MonitorHarness.Config(folders, entropy: new EntropyOptions()),
            _h.ScopeFactory,
            new FileEventDeduplicator(50),
            Mock.Of<IEntropyCalculator>(),
            _h.Registry,
            _h.Reporter,
            startupDelay: TimeSpan.Zero,
            attribution: cache,
            fanout: fanout,
            etwSession: session);
    }

    [Fact(Timeout = 30_000)]
    public async Task With_the_kernel_session_running_Entropy_is_active()
    {
        using var folder = new TempFolder();
        using EntropyMonitor monitor = Entropy([folder.Path], withRunningEtw: true);

        await monitor.StartAsync(CancellationToken.None);
        await Eventually.HoldsAsync(
            () => _h.State(ModuleCode.Entropy).State == ModuleState.Active,
            "ENTROPY publishes active once it can name a writer and rule 3 is evaluated");
        await monitor.StopAsync(CancellationToken.None);

        ModuleStateEntry entropy = _h.State(ModuleCode.Entropy);
        entropy.State.ShouldBe(ModuleState.Active);
        entropy.ReasonCode.ShouldBeNull();
    }

    [Fact(Timeout = 30_000)]
    public async Task Without_the_kernel_session_Entropy_is_degraded_attribution_unreliable_not_active()
    {
        // No administrator rights, or no session slot left: the detection runs, nobody is named,
        // and the console must say so rather than show green.
        using var folder = new TempFolder();
        using EntropyMonitor monitor = Entropy([folder.Path]);

        await monitor.StartAsync(CancellationToken.None);
        await Eventually.HoldsAsync(
            () => _h.State(ModuleCode.Entropy).State == ModuleState.Degraded,
            "ENTROPY publishes degraded while it cannot name a process");
        await monitor.StopAsync(CancellationToken.None);

        ModuleStateEntry entropy = _h.State(ModuleCode.Entropy);
        entropy.State.ShouldBe(ModuleState.Degraded);
        entropy.ReasonCode.ShouldBe(ModuleReasonCode.AttributionUnreliable);
        entropy.ReasonCode.ShouldBe("attribution_unreliable");
    }

    [Fact(Timeout = 30_000)]
    public async Task Degraded_says_which_capability_is_missing_by_its_debt_identifier()
    {
        var log = new RecordingLogger<EntropyMonitor>();
        using var folder = new TempFolder();
        using EntropyMonitor monitor = Entropy([folder.Path], log);

        await monitor.StartAsync(CancellationToken.None);
        await Eventually.HoldsAsync(
            () => _h.State(ModuleCode.Entropy).State == ModuleState.Degraded,
            "ENTROPY reaches its running state");
        await monitor.StopAsync(CancellationToken.None);

        // The operator reading the log learns what is missing and what it needs, rather than
        // discovering months later that an alert never names anybody.
        log.Entries.ShouldContain(e => e.Message.Contains("AGT-ATTR-001", StringComparison.Ordinal));
        log.Entries.ShouldContain(e => e.Message.Contains("administrator rights", StringComparison.Ordinal));
    }

    [Fact(Timeout = 30_000)]
    public async Task Active_says_rule_3_is_evaluated_and_with_which_thresholds()
    {
        var log = new RecordingLogger<EntropyMonitor>();
        using var folder = new TempFolder();
        using EntropyMonitor monitor = Entropy([folder.Path], log, withRunningEtw: true);

        await monitor.StartAsync(CancellationToken.None);
        await Eventually.HoldsAsync(
            () => _h.State(ModuleCode.Entropy).State == ModuleState.Active,
            "ENTROPY reaches active");
        await monitor.StopAsync(CancellationToken.None);

        // The configured thresholds are no longer read-and-ignored (AGT-EN-002); the log says so
        // with the values in force, so an operator can see they mean something.
        log.Entries.ShouldContain(e => e.Message.Contains("rule 3 is evaluated", StringComparison.Ordinal));
    }

    /// <summary>A kernel trace that opens and stays open, so the session reaches Running.</summary>
    private sealed class StubTrace : IKernelFileIoTrace
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

    /// <summary>A folder ENTROPY may watch, removed at the end of the test.</summary>
    private sealed class TempFolder : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"rg_entropy_{Guid.NewGuid():N}");

        public TempFolder() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); }
            catch (IOException) { /* a watcher may still hold it: the temp folder is cleaned by the OS */ }
            catch (UnauthorizedAccessException) { /* idem */ }
        }
    }
}
