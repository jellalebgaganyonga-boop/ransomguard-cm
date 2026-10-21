using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RansomGuard.Agent.Core.Configuration;
using RansomGuard.Agent.Core.Detection;
using RansomGuard.Agent.Core.Detection.Entropy;
using RansomGuard.Agent.Core.Detection.Sentinel;
using RansomGuard.Agent.Core.Diagnostics;
using RansomGuard.Agent.Core.Persistence.Entities;
using RansomGuard.Agent.Service;
using Shouldly;

namespace RansomGuard.Agent.Tests.Diagnostics;

/// <summary>
/// The state SENTINEL and ENTROPY publish, on the real monitors (CORRECTIF2_REVUE points 1 and 4):
/// a module watching no folder is not "active"; a failing periodic check degrades SENTINEL
/// without switching off its real-time detection.
/// </summary>
public sealed class MonitorPostureTests : IDisposable
{
    private readonly MonitorHarness _h = new();

    public void Dispose() => _h.Dispose();

    private SentinelMonitor Sentinel(string[] folders, ILogger<SentinelMonitor>? logger = null) => new(
        logger ?? NullLogger<SentinelMonitor>.Instance,
        MonitorHarness.Config([], sentinel: new SentinelOptions
        {
            WatchDirectories = folders,
            CanaryTemplates = ["dossier_patient"],
            CheckIntervalMs = 50,
            EnableRealTimeRegeneration = false,
        }),
        _h.ScopeFactory,
        new FileEventDeduplicator(50),
        _h.AlertQueue,
        _h.Registry,
        _h.Reporter,
        startupDelay: TimeSpan.Zero);

    private EntropyMonitor Entropy(string[] folders, ILogger<EntropyMonitor>? logger = null) => new(
        logger ?? NullLogger<EntropyMonitor>.Instance,
        MonitorHarness.Config(folders, entropy: new EntropyOptions()),
        _h.ScopeFactory,
        new FileEventDeduplicator(50),
        Mock.Of<IEntropyCalculator>(),
        _h.Registry,
        _h.Reporter,
        startupDelay: TimeSpan.Zero);

    // ── Point 1: no_watch_directory ─────────────────────────────────────────────

    [Fact(Timeout = 30_000)]
    public async Task Sentinel_with_no_existing_folder_is_inactive_no_watch_directory_not_active()
    {
        using SentinelMonitor monitor = Sentinel([MonitorHarness.MissingFolder(), MonitorHarness.MissingFolder()]);

        await monitor.StartAsync(CancellationToken.None);
        await Eventually.HoldsAsync(() => _h.State(ModuleCode.Sentinel).ReasonCode == ModuleReasonCode.NoWatchDirectory,
            "SENTINEL publishes no_watch_directory");
        await monitor.StopAsync(CancellationToken.None);

        _h.State(ModuleCode.Sentinel).State.ShouldBe(ModuleState.Inactive);
        _h.Registry.Published.ShouldNotContain(p => p.Code == ModuleCode.Sentinel && p.State == ModuleState.Active);
    }

    [Fact(Timeout = 30_000)]
    public async Task Entropy_with_no_existing_folder_is_inactive_no_watch_directory_not_active()
    {
        using EntropyMonitor monitor = Entropy([MonitorHarness.MissingFolder()]);

        await monitor.StartAsync(CancellationToken.None);
        await Eventually.HoldsAsync(() => _h.State(ModuleCode.Entropy).ReasonCode == ModuleReasonCode.NoWatchDirectory,
            "ENTROPY publishes no_watch_directory");
        await monitor.StopAsync(CancellationToken.None);

        _h.State(ModuleCode.Entropy).State.ShouldBe(ModuleState.Inactive);
        _h.Registry.Published.ShouldNotContain(p => p.Code == ModuleCode.Entropy && p.State == ModuleState.Active);
    }

    [Fact(Timeout = 30_000)]
    public async Task Sentinel_with_some_folders_missing_is_active_and_names_each_missing_one()
    {
        string missingA = MonitorHarness.MissingFolder(), missingB = MonitorHarness.MissingFolder();
        var log = new RecordingLogger<SentinelMonitor>();
        using SentinelMonitor monitor = Sentinel([_h.Folder, missingA, missingB], log);

        await monitor.StartAsync(CancellationToken.None);
        await Eventually.HoldsAsync(() => _h.State(ModuleCode.Sentinel).State == ModuleState.Active, "SENTINEL active");
        await monitor.StopAsync(CancellationToken.None);

        foreach (string missing in new[] { missingA, missingB })
        {
            log.Entries.ShouldContain(e => e.Level == LogLevel.Warning && e.Message.Contains(missing));
        }

        log.Entries.ShouldNotContain(e => e.Level == LogLevel.Warning && e.Message.Contains(_h.Folder));
    }

    [Fact(Timeout = 30_000)]
    public async Task Entropy_with_some_folders_missing_is_active_and_names_each_missing_one()
    {
        string missing = MonitorHarness.MissingFolder();
        var log = new RecordingLogger<EntropyMonitor>();
        using EntropyMonitor monitor = Entropy([_h.Folder, missing], log);

        await monitor.StartAsync(CancellationToken.None);
        await Eventually.HoldsAsync(() => _h.State(ModuleCode.Entropy).State == ModuleState.Active, "ENTROPY active");
        await monitor.StopAsync(CancellationToken.None);

        log.Entries.ShouldContain(e => e.Level == LogLevel.Warning && e.Message.Contains(missing));
    }

    // ── Point 4: SENTINEL degraded, never switched off ──────────────────────────

    [Fact(Timeout = 30_000)]
    public async Task Sentinel_turns_degraded_at_the_fifth_consecutive_failed_check_not_before_and_never_stops()
    {
        _h.IntegrityCheck.Failing = true;
        using SentinelMonitor monitor = Sentinel([_h.Folder]);

        await monitor.StartAsync(CancellationToken.None);
        await Eventually.HoldsAsync(() => _h.State(ModuleCode.Sentinel).State == ModuleState.Degraded, "SENTINEL degraded");
        int callsWhenDegraded = _h.IntegrityCheck.Calls;

        // Let it keep failing well past the threshold: still degraded, never stopped.
        await Eventually.HoldsAsync(() => _h.IntegrityCheck.Calls >= callsWhenDegraded + 10, "ten more failed checks");
        await monitor.StopAsync(CancellationToken.None);

        _h.State(ModuleCode.Sentinel).ReasonCode.ShouldBe(ModuleReasonCode.IntegrityCheckFailing);
        callsWhenDegraded.ShouldBeGreaterThanOrEqualTo(SentinelMonitor.MaxConsecutiveIntegrityCheckFailures);
        var sentinel = _h.Registry.Published.Where(p => p.Code == ModuleCode.Sentinel).ToList();
        sentinel.ShouldBe([
            (ModuleCode.Sentinel, ModuleState.Active, (string?)null),
            (ModuleCode.Sentinel, ModuleState.Degraded, ModuleReasonCode.IntegrityCheckFailing),
        ]);
    }

    [Fact(Timeout = 30_000)]
    public async Task Sentinel_degraded_comes_back_to_active_at_the_first_successful_check()
    {
        _h.IntegrityCheck.Failing = true;
        using SentinelMonitor monitor = Sentinel([_h.Folder]);

        await monitor.StartAsync(CancellationToken.None);
        await Eventually.HoldsAsync(() => _h.State(ModuleCode.Sentinel).State == ModuleState.Degraded, "SENTINEL degraded");

        _h.IntegrityCheck.Failing = false;
        await Eventually.HoldsAsync(() => _h.State(ModuleCode.Sentinel).State == ModuleState.Active, "SENTINEL active again");
        await monitor.StopAsync(CancellationToken.None);

        _h.State(ModuleCode.Sentinel).ReasonCode.ShouldBeNull();
    }

    [Fact(Timeout = 30_000)]
    public async Task A_canary_modified_while_sentinel_is_degraded_is_still_detected_in_real_time()
    {
        SentinelCanary canary = await _h.CreateCanaryAsync(_h.Folder);
        _h.IntegrityCheck.Failing = true; // the periodic check cannot see anything
        using SentinelMonitor monitor = Sentinel([_h.Folder]);

        await monitor.StartAsync(CancellationToken.None);
        await Eventually.HoldsAsync(() => _h.State(ModuleCode.Sentinel).State == ModuleState.Degraded, "SENTINEL degraded");

        await File.AppendAllTextAsync(canary.FilePath, "\nENCRYPTED");

        await Eventually.HoldsAsync(() =>
        {
            lock (_h.Forwarded)
            {
                return _h.Forwarded.Any(a => a.AlertType == "SentinelCanaryModified"
                    && a.RawPayload is not null && Equals(a.RawPayload["canary_path"], canary.FilePath));
            }
        }, "the modification is forwarded to the GRID");
        await monitor.StopAsync(CancellationToken.None);

        _h.State(ModuleCode.Sentinel).State.ShouldBe(ModuleState.Degraded); // detected while degraded
    }
}
