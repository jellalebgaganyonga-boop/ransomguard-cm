using RansomGuard.Agent.Core.Detection.Sentinel;
using RansomGuard.Agent.Tests.Diagnostics;
using Shouldly;

namespace RansomGuard.Agent.Tests.Sentinel;

/// <summary>
/// A fresh install must report nothing.
///
/// <para>
/// On the bench VM, installing the agent produced six Critical "canary modified" alerts before
/// anything had happened. SENTINEL plants its decoys at startup, and the FileSystemWatcher carries
/// no PID -- it says only that a file changed -- so each creation came back as an attack on a
/// canary. The set of paths the agent is about to write lived inside the monitor, while the writes
/// were done by the deployment service, so the monitor could not know they were its own.
/// </para>
///
/// <para>
/// <see cref="AgentOwnWrites"/> is that set, shared between the two. These tests hold the install
/// case and, just as importantly, the opposite one: marking a path must not become a way to write
/// to a canary unnoticed.
/// </para>
/// </summary>
public sealed class AgentOwnWritesTests
{
    private static ManualTimeProvider Clock() => new();

    private const string CanaryPath = @"C:\Users\Public\Documents\0001_dossier_patient.docx";

    [Fact]
    public void A_canary_the_agent_has_just_planted_is_its_own_write()
    {
        var time = Clock();
        var own = new AgentOwnWrites(time);

        own.Mark(CanaryPath, AgentOwnWrites.DeploymentGrace);

        own.IsOwn(CanaryPath).ShouldBeTrue();
    }

    [Fact]
    public void The_deployment_grace_outlasts_the_monitor_startup_delay()
    {
        // This is the whole install bug: the canaries are created before the watchers exist, so
        // their creation events only surface once watching starts -- after the monitor's own
        // startup delay. A grace shorter than that delay marks nothing useful.
        var time = Clock();
        var own = new AgentOwnWrites(time);
        own.Mark(CanaryPath, AgentOwnWrites.DeploymentGrace);

        time.Advance(TimeSpan.FromSeconds(30));

        own.IsOwn(CanaryPath).ShouldBeTrue("the watcher event for a planted canary arrives after the startup delay");
    }

    [Fact]
    public void Once_the_grace_has_passed_a_write_to_the_same_canary_is_an_attack_again()
    {
        var time = Clock();
        var own = new AgentOwnWrites(time);
        own.Mark(CanaryPath, AgentOwnWrites.DeploymentGrace);

        time.Advance(AgentOwnWrites.DeploymentGrace + TimeSpan.FromSeconds(1));

        // Suppressing an alert for a canary the agent planted must not suppress the alert for the
        // same canary when something else writes it a minute later.
        own.IsOwn(CanaryPath).ShouldBeFalse();
    }

    [Fact]
    public void A_restoration_is_graced_only_for_as_long_as_the_rewrite_takes()
    {
        var time = Clock();
        var own = new AgentOwnWrites(time);

        // Restoring one compromised canary is a single write whose event follows at once, so the
        // grace is short: a long one would leave a restored canary unwatched.
        own.Mark(CanaryPath, AgentOwnWrites.RestoreGrace);
        own.IsOwn(CanaryPath).ShouldBeTrue();

        time.Advance(AgentOwnWrites.RestoreGrace + TimeSpan.FromMilliseconds(1));
        own.IsOwn(CanaryPath).ShouldBeFalse();
    }

    /// <summary>
    /// The kernel's view of a write is not prompt. Windows' cache manager flushes a file long
    /// after the process wrote it, and ETW attributes that flush to a system PID rather than to
    /// the writer, so filtering the agent's own PID out of the session does not cover the agent's
    /// own writes. Measured on the bench: canaries written at 17:51:28, the same writes surfacing
    /// at 17:51:46.
    /// </summary>
    private static readonly TimeSpan ObservedKernelLazyWriteDelay = TimeSpan.FromSeconds(18);

    [Fact]
    public void The_kernel_window_outlives_the_delay_the_kernel_was_measured_to_take()
    {
        // Shorter than this is the sustained-attack loop: the agent repairs a decoy, the kernel
        // surfaces that repair as somebody else's write, the agent repairs it again.
        AgentOwnWrites.KernelReportDelay.ShouldBeGreaterThan(ObservedKernelLazyWriteDelay);
    }

    [Fact]
    public void A_restored_canary_is_still_the_agents_own_write_when_the_kernel_reports_it()
    {
        var time = Clock();
        var own = new AgentOwnWrites(time);
        own.Mark(CanaryPath, AgentOwnWrites.RestoreGrace);

        time.Advance(ObservedKernelLazyWriteDelay);

        own.WasWrittenByAgent(CanaryPath).ShouldBeTrue(
            "the kernel surfaced the agent's own rewrite 18 s later, attributed to a system PID");
    }

    [Fact]
    public void A_repaired_canary_goes_straight_back_under_watch()
    {
        // The kernel window must not become a minute in which a decoy the agent has just repaired
        // is unwatched. The watcher is prompt, so its grace stays short and it keeps reporting.
        var time = Clock();
        var own = new AgentOwnWrites(time);
        own.Mark(CanaryPath, AgentOwnWrites.RestoreGrace);

        time.Advance(TimeSpan.FromSeconds(5));

        own.IsOwn(CanaryPath).ShouldBeFalse(
            "a write 5 s after the agent repaired the decoy is an attack and must be reported");
    }

    [Fact]
    public void A_canary_the_agent_never_wrote_is_never_its_own()
    {
        var own = new AgentOwnWrites(Clock());

        own.IsOwn(CanaryPath).ShouldBeFalse();
    }

    [Fact]
    public void The_same_path_in_another_case_is_still_the_agents_own_write()
    {
        // Windows paths are case-insensitive and the watcher does not always report the casing the
        // deployment used; a case difference must not turn a planted canary into an attack.
        var own = new AgentOwnWrites(Clock());
        own.Mark(CanaryPath, AgentOwnWrites.DeploymentGrace);

        own.IsOwn(CanaryPath.ToUpperInvariant()).ShouldBeTrue();
    }

    [Fact]
    public void A_write_that_failed_drops_its_mark_immediately()
    {
        var own = new AgentOwnWrites(Clock());
        own.Mark(CanaryPath, AgentOwnWrites.DeploymentGrace);

        own.Clear(CanaryPath);

        // Nothing is going to arrive for it, and the path must not stay excused for a minute.
        own.IsOwn(CanaryPath).ShouldBeFalse();
    }

    [Fact]
    public void An_expired_mark_is_dropped_as_it_is_read_so_the_set_does_not_grow()
    {
        var time = Clock();
        var own = new AgentOwnWrites(time);

        for (int i = 0; i < 100; i++)
        {
            own.Mark($@"C:\Users\Public\Documents\{i:D4}_dossier_patient.docx", AgentOwnWrites.RestoreGrace);
        }

        own.Count.ShouldBe(100);

        // Past the watcher's grace the path is watched again, but the entry stays: the kernel may
        // still be about to report that same write, and dropping it here would bring back the
        // alert the agent raises against itself.
        time.Advance(AgentOwnWrites.RestoreGrace + TimeSpan.FromSeconds(1));
        for (int i = 0; i < 100; i++)
        {
            own.IsOwn($@"C:\Users\Public\Documents\{i:D4}_dossier_patient.docx").ShouldBeFalse();
        }

        own.Count.ShouldBe(100, "the kernel has not had its window yet");

        // Past the kernel's window too, nobody can still be asking about these writes, so the
        // entries go as they are read and the set does not grow.
        time.Advance(AgentOwnWrites.KernelReportDelay);
        for (int i = 0; i < 100; i++)
        {
            own.IsOwn($@"C:\Users\Public\Documents\{i:D4}_dossier_patient.docx").ShouldBeFalse();
        }

        own.Count.ShouldBe(0);
    }

    [Fact]
    public void Every_canary_of_a_fresh_install_is_marked_before_the_watchers_see_it()
    {
        // The install plants several canaries per directory. All of them must be excused, not the
        // last one: six alerts was six canaries.
        var time = Clock();
        var own = new AgentOwnWrites(time);

        string[] planted =
        [
            @"C:\Users\Public\Documents\0001_dossier_patient.docx",
            @"C:\Users\Public\Documents\0002_analyses_laboratoire.xlsx",
            @"C:\Users\Public\Documents\0003_imagerie_medicale.pdf",
            @"C:\Users\Public\Desktop\0001_dossier_patient.docx",
            @"C:\Users\Public\Desktop\0002_analyses_laboratoire.xlsx",
            @"C:\Users\Public\Desktop\0003_imagerie_medicale.pdf",
        ];

        foreach (string path in planted)
        {
            own.Mark(path, AgentOwnWrites.DeploymentGrace);
        }

        time.Advance(TimeSpan.FromSeconds(20));

        planted.ShouldAllBe(p => own.IsOwn(p));
    }
}
