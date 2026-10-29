using Microsoft.Extensions.Logging.Abstractions;
using RansomGuard.Agent.Core.Communication.Models;
using RansomGuard.Agent.Core.Configuration;
using RansomGuard.Agent.Core.Detection;
using RansomGuard.Agent.Core.Detection.Sentinel;
using RansomGuard.Agent.Core.Detection.Sentinel.Etw;
using RansomGuard.Agent.Core.Diagnostics;
using RansomGuard.Agent.Core.Persistence.Entities;
using RansomGuard.Agent.Service;
using RansomGuard.Agent.Tests.Diagnostics;
using Shouldly;

namespace RansomGuard.Agent.Tests.Detection.Sentinel;

/// <summary>
/// One write on a decoy is one alert, although two things watch the decoy.
///
/// <para>
/// Two paths raise <c>CanaryModified</c>. The real-time path -- the FileSystemWatcher and the ETW
/// session -- raises it when the write happens, and can name the writer because a write event is
/// there to match. The periodic integrity sweep raises it when a decoy's contents no longer match
/// its stored hash, and exists as a safety net for a write the real-time path missed entirely.
/// </para>
///
/// <para>
/// They used to collide. The real-time alert is followed by a restore, which rewrites the decoy
/// and stores the new hash; a sweep that ran in that interval found a file that did not match,
/// knew nothing of the alert a second earlier, and raised a second Critical alert on the same
/// write -- this one with no write event to attribute, so no writer and no process chain. On the
/// bench on 2026-10-26 the owner saw two alerts for one write, a second apart, and only the first
/// answered "who did it" or "what started it".
/// </para>
///
/// <para>
/// The sweep must stay silent only for the modification already reported and being restored, and
/// must still report the next one. That is what these two tests pin: one write, one alert, with
/// its attribution; and a second write afterwards, a second alert.
/// </para>
/// </summary>
public sealed class SentinelSweepDuplicateTests : IDisposable
{
    private const int Writer = 4242;

    // The production default, which is also what the bench ran when the duplicate was found: the
    // second alert landed one second after the first, one sweep later.
    //
    // Not faster. A 200ms sweep was tried, to hit the window on every run, and made the tests
    // flaky in the other direction: the sweep started winning the race for the *first* detection
    // too, before the watcher had turned the write into an alert, and then the alert carried no
    // attribution because there was no write event to match. The sweep has to be slow enough that
    // the real-time path reliably reports first -- which is the situation being tested.
    private const int SweepMs = 1000;

    // Several sweeps after the alert. A duplicate, if the gate were not there, arrives on the
    // first of them; this waits for four so a failure cannot be explained away as being early.
    private static readonly TimeSpan SeveralSweeps = TimeSpan.FromMilliseconds(SweepMs * 4);

    private static readonly DateTime WriterStart = DateTime.UtcNow.AddHours(-1);

    private readonly AgentOwnWrites _ownWrites = new();
    private readonly MonitorHarness _h;
    private readonly EmittingTrace _trace = new();
    private readonly ProcessCreationTimes _processes = new(pid => pid == Writer
        ? new ProcessFacts(WriterStart, "evil", @"C:\Users\x\AppData\Local\Temp\evil.exe")
        : null);

    public SentinelSweepDuplicateTests() => _h = new MonitorHarness(ownWrites: _ownWrites);

    public void Dispose() => _h.Dispose();

    [Fact(Timeout = 60_000)]
    public async Task One_write_raises_one_alert_and_it_carries_the_attribution()
    {
        SentinelCanary canary = await PlantedCanaryAsync();
        using SentinelMonitor monitor = await StartedSentinelAsync();

        await WriteAfterASweepAsync(canary);

        await Eventually.HoldsAsync(() => Alerts(canary) == 1, "the alert");
        AlertIngestRequest alert = Alert(canary);
        await Eventually.HoldsAsync(() => Complements(alert).Count == 1, "the attribution complement");
        Complements(alert)[0].State.ShouldBe("resolved");
        Complements(alert)[0].Pid.ShouldBe(Writer);

        // The assertion the defect failed: the count was 2, a second apart.
        await Task.Delay(SeveralSweeps);
        Alerts(canary).ShouldBe(1, "one write on a decoy is one alert");
    }

    [Fact(Timeout = 60_000)]
    public async Task A_second_write_after_the_restore_raises_a_second_alert()
    {
        SentinelCanary canary = await PlantedCanaryAsync();
        using SentinelMonitor monitor = await StartedSentinelAsync();

        await WriteAfterASweepAsync(canary);
        await Eventually.HoldsAsync(() => Alerts(canary) == 1, "the first alert");

        // Wait for the two things that actually have to be true, not for a stretch of time: the
        // decoy's contents are back (so the restore has run and stored its new hash), and the
        // watcher's grace on that repair has lapsed (so the next write is read as an attack and
        // not as the agent's own writing).
        await Eventually.HoldsAsync(
            () => Restored(canary) && !_ownWrites.IsOwn(canary.FilePath),
            "the decoy restored and its restore grace over");

        // Said out loud before the second write, because everything after it depends on this: a
        // decoy that has left surveillance cannot raise anything, and a bare timeout below would
        // blame the wrong thing.
        SentinelCanary? afterRestore = await _h.CanaryAsync(canary.Id);
        afterRestore.ShouldNotBeNull();
        afterRestore.Status.ShouldBe(CanaryStatus.Active, "a restored decoy stays watched");

        await WriteAfterASweepAsync(canary);

        // At least two: the point is that the second write is reported, not that nothing else
        // ever is. Pinning it to exactly 2 would make the test fail for a reason it is not about.
        await Eventually.HoldsAsync(() => Alerts(canary) >= 2, "the second write's own alert");
    }

    /// <summary>A decoy that has been on disk long enough that a write to it is an attack.</summary>
    private async Task<SentinelCanary> PlantedCanaryAsync()
    {
        SentinelCanary canary = await _h.CreateCanaryAsync(_h.Folder);

        // Planting marks the path for a minute (DeploymentGrace). On a workstation the install is
        // long over by the time anybody writes to a decoy; here it would forgive the test's write.
        _ownWrites.Clear(canary.FilePath);
        return canary;
    }

    /// <summary>
    /// Whether the decoy carries its generated contents again rather than the attacker's. Reading
    /// can land in the middle of the rewrite, which is simply "not yet".
    /// </summary>
    private static bool Restored(SentinelCanary canary)
    {
        try
        {
            return !File.ReadAllText(canary.FilePath).Contains("ENCRYPTED", StringComparison.Ordinal);
        }
        catch (IOException)
        {
            return false;
        }
    }

    /// <summary>One write, seen by both the watcher and ETW, exactly as a real one is.</summary>
    private async Task WriteAsync(SentinelCanary canary)
    {
        await AppendAsync(canary.FilePath, "\nENCRYPTED");
        _trace.Emit(new FileIoRecord(FileIoOperation.Write, canary.FilePath, Writer, DateTime.UtcNow, 0, 0));
    }

    /// <summary>
    /// Appends, waiting the file out if the agent has it open.
    ///
    /// <para>
    /// Checking a decoy means reading it, and a read holds the file against writers for that
    /// instant: writing just as a sweep comes round got "the process cannot access the file
    /// because it is being used by another process" on CI. That is not the test finding anything
    /// -- a real attacker meets the same lock and comes back a moment later, which is all this
    /// does. It waits for the file to be writable instead of assuming it already is.
    /// </para>
    /// </summary>
    private static async Task AppendAsync(string path, string text)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                await File.AppendAllTextAsync(path, text);
                return;
            }
            catch (IOException) when (attempt < 150)
            {
                await Task.Delay(20);
            }
        }
    }

    /// <summary>
    /// Writes just after a sweep has come round, so the next one is a whole interval away.
    ///
    /// <para>
    /// Without this the test is a coin toss on a loaded machine. The watcher reports a write in
    /// 10 to 16 ms; a sweep landing inside that gap finds the decoy modified first and raises the
    /// alert itself, with no write event to attribute it to -- and the test then fails for the
    /// opposite reason to the one it is about. Waiting for a sweep to start is a real condition
    /// and leaves the whole interval clear.
    /// </para>
    /// </summary>
    private async Task WriteAfterASweepAsync(SentinelCanary canary)
    {
        int before = _h.IntegrityCheck.Calls;
        await Eventually.HoldsAsync(() => _h.IntegrityCheck.Calls > before, "a sweep to come round");
        await WriteAsync(canary);
    }

    private async Task<SentinelMonitor> StartedSentinelAsync()
    {
        var session = new EtwFileIoSession(() => _trace, NullLogger<EtwFileIoSession>.Instance,
            activeSessions: () => []);
        var monitor = new SentinelMonitor(
            NullLogger<SentinelMonitor>.Instance,
            MonitorHarness.Config([], sentinel: new SentinelOptions
            {
                WatchDirectories = [_h.Folder],
                CanaryTemplates = ["dossier_patient"],
                CheckIntervalMs = SweepMs,
                // The restore is the half of the race the sweep collides with, so it has to be on.
                EnableRealTimeRegeneration = true,
                EtwEnabled = true,
            }),
            _h.ScopeFactory,
            new FileEventDeduplicator(50),
            _h.AlertQueue,
            _h.Registry,
            _h.Reporter,
            startupDelay: TimeSpan.Zero,
            etwSession: session,
            processTimes: _processes,
            ownWrites: _ownWrites);
        await monitor.StartAsync(CancellationToken.None);
        await Eventually.HoldsAsync(() => _h.State(ModuleCode.Sentinel).State == ModuleState.Active, "SENTINEL active");
        _trace.Processing.Wait(TimeSpan.FromSeconds(5)).ShouldBeTrue("the ETW session never started processing");
        return monitor;
    }

    private AlertIngestRequest Alert(SentinelCanary canary)
    {
        lock (_h.Forwarded)
        {
            return _h.Forwarded.First(a => a.AlertType == "SentinelCanaryModified"
                && Equals(a.RawPayload["canary_path"], canary.FilePath));
        }
    }

    private List<AttributionComplementRequest> Complements(AlertIngestRequest alert)
    {
        lock (_h.Attributions)
        {
            return _h.Attributions.Where(a => a.ClientMessageId == alert.ClientMessageId)
                .Select(a => a.Complement).ToList();
        }
    }

    private int Alerts(SentinelCanary canary)
    {
        lock (_h.Forwarded)
        {
            return _h.Forwarded.Count(a => a.AlertType == "SentinelCanaryModified"
                && Equals(a.RawPayload["canary_path"], canary.FilePath));
        }
    }
}
