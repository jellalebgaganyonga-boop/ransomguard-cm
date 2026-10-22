using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RansomGuard.Agent.Core.Configuration;
using RansomGuard.Agent.Core.Detection;
using RansomGuard.Agent.Core.Detection.Entropy;
using RansomGuard.Agent.Core.Diagnostics;
using RansomGuard.Agent.Service;
using Shouldly;

namespace RansomGuard.Agent.Tests.Diagnostics;

/// <summary>
/// What ENTROPY may claim about itself (B4, 2026-10-22). It watches, computes Shannon entropy and
/// alerts — that part is real. Two gaps are known and open, so "active" would overstate it:
/// <list type="bullet">
///   <item>every ENTROPY signal carries PID 0: no process is named (AGT-ATTR-001);</item>
///   <item>rule 3, the directory-wide entropy shift that is the mass-encryption signal, has no
///   caller: <c>DirectoryShiftThreshold</c>, <c>DirectoryShiftWindowSeconds</c> and
///   <c>DirectoryShiftMinFiles</c> are read from the configuration and change nothing
///   (AGT-EN-002).</item>
/// </list>
/// A module that detects but cannot say who, and does not evaluate the rule its own configuration
/// promises, reports <c>degraded</c>.
/// </summary>
public sealed class EntropyPostureTests : IDisposable
{
    private readonly MonitorHarness _h = new();

    public void Dispose() => _h.Dispose();

    private EntropyMonitor Entropy(string[] folders, ILogger<EntropyMonitor>? logger = null) => new(
        logger ?? NullLogger<EntropyMonitor>.Instance,
        MonitorHarness.Config(folders, entropy: new EntropyOptions()),
        _h.ScopeFactory,
        new FileEventDeduplicator(50),
        Mock.Of<IEntropyCalculator>(),
        _h.Registry,
        _h.Reporter,
        startupDelay: TimeSpan.Zero);

    [Fact(Timeout = 30_000)]
    public async Task Entropy_watching_a_real_folder_reports_degraded_attribution_unreliable_not_active()
    {
        using var folder = new TempFolder();
        using EntropyMonitor monitor = Entropy([folder.Path]);

        await monitor.StartAsync(CancellationToken.None);
        await Eventually.HoldsAsync(
            () => _h.State(ModuleCode.Entropy).State == ModuleState.Degraded,
            "ENTROPY publishes degraded while it names no process and does not evaluate rule 3");
        await monitor.StopAsync(CancellationToken.None);

        ModuleStateEntry entropy = _h.State(ModuleCode.Entropy);
        entropy.State.ShouldBe(ModuleState.Degraded);
        entropy.ReasonCode.ShouldBe(ModuleReasonCode.AttributionUnreliable);
        entropy.ReasonCode.ShouldBe("attribution_unreliable");
    }

    [Fact(Timeout = 30_000)]
    public async Task Entropy_says_at_start_which_rules_it_evaluates_and_which_it_does_not()
    {
        var log = new RecordingLogger<EntropyMonitor>();
        using var folder = new TempFolder();
        using EntropyMonitor monitor = Entropy([folder.Path], log);

        await monitor.StartAsync(CancellationToken.None);
        await Eventually.HoldsAsync(
            () => _h.State(ModuleCode.Entropy).State == ModuleState.Degraded,
            "ENTROPY reaches its running state");
        await monitor.StopAsync(CancellationToken.None);

        // The operator reading the log learns the two gaps by their debt identifiers, not by
        // discovering months later that a configured threshold does nothing.
        log.Entries.ShouldContain(e => e.Message.Contains("AGT-EN-002", StringComparison.Ordinal));
        log.Entries.ShouldContain(e => e.Message.Contains("AGT-ATTR-001", StringComparison.Ordinal));
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
