using Microsoft.Extensions.Logging;
using Moq;
using RansomGuard.Agent.Core.Detection.Genealogy;
using Shouldly;

namespace RansomGuard.Agent.Tests.Genealogy;

/// <summary>
/// GENEALOGY's attribution used to be measured at 0/120 (AttributionBench): it started from the
/// Restart Manager, which answers "who holds this file open right now", and ransomware writes and
/// closes. By the time an alert was enriched there was nobody left to name, so no tree was ever
/// built and the module reported attribution_unreliable.
///
/// <para>
/// The store below starts instead from the writer the kernel FileIO session names while the write
/// is happening, when the process is still alive. These tests hold that contract: a tree is built
/// for the attributed writer, the session thread is never blocked, a PID Windows has recycled is
/// refused rather than answered wrongly, and a store that captures nothing says so instead of
/// letting the console show GENEALOGY as healthy.
/// </para>
/// </summary>
public sealed class ProcessAncestryStoreTests
{
    private static ProcessSnapshot Snapshot(int pid, int parentPid, string name, DateTime startedAt) => new()
    {
        ProcessId = pid,
        ParentProcessId = parentPid,
        ProcessName = name,
        StartTime = startedAt,
        ExecutablePath = $@"C:\Windows\System32\{name}.exe",
    };

    /// <summary>explorer.exe -> cmd.exe: the chain the bench trigger actually produces.</summary>
    private static ProcessTree Tree(int pid, DateTime startedAt) => new()
    {
        Root = Snapshot(pid, 4321, "cmd", startedAt),
        Ancestors = [Snapshot(4321, 1000, "explorer", startedAt.AddMinutes(-30))],
    };

    private static ProcessAncestryStore Store(IProcessSnapshotService snapshots, int agentPid = 999_999) =>
        new(snapshots, new Mock<ILogger<ProcessAncestryStore>>().Object, agentPid: agentPid);

    /// <summary>Waits for the background drain loop, which is deliberately not synchronous.</summary>
    private static async Task<bool> Eventually(Func<bool> condition)
    {
        for (int i = 0; i < 100 && !condition(); i++)
        {
            await Task.Delay(20);
        }

        return condition();
    }

    [Fact]
    public async Task The_tree_is_built_for_the_process_the_write_was_attributed_to()
    {
        DateTime startedAt = new(2026, 10, 25, 9, 0, 0, DateTimeKind.Utc);
        var snapshots = new Mock<IProcessSnapshotService>();
        snapshots.Setup(s => s.BuildProcessTree(3792, It.IsAny<int>())).Returns(Tree(3792, startedAt));

        using ProcessAncestryStore store = Store(snapshots.Object);
        store.Start();

        store.OnWriterSeen(3792);

        (await Eventually(() => store.Captured_ == 1)).ShouldBeTrue("the writer's ancestry was never captured");

        ProcessTree? tree = store.TryGet(3792, startedAt);
        tree.ShouldNotBeNull();
        tree.Root.ProcessName.ShouldBe("cmd");
        tree.Summary.ShouldBe("explorer -> cmd");
        tree.Depth.ShouldBe(1);
    }

    [Fact]
    public async Task A_writer_that_has_already_exited_is_counted_as_missed_and_nothing_is_invented()
    {
        var snapshots = new Mock<IProcessSnapshotService>();
        snapshots.Setup(s => s.BuildProcessTree(It.IsAny<int>(), It.IsAny<int>())).Returns((ProcessTree?)null);

        using ProcessAncestryStore store = Store(snapshots.Object);
        store.Start();

        store.OnWriterSeen(3792);

        (await Eventually(() => store.Missed == 1)).ShouldBeTrue();
        store.Captured_.ShouldBe(0);
        store.TryGet(3792, DateTime.UtcNow).ShouldBeNull();
    }

    [Fact]
    public async Task A_tree_whose_root_started_at_another_moment_is_refused_as_a_reused_pid()
    {
        DateTime captured = new(2026, 10, 25, 9, 0, 0, DateTimeKind.Utc);
        var snapshots = new Mock<IProcessSnapshotService>();
        snapshots.Setup(s => s.BuildProcessTree(3792, It.IsAny<int>())).Returns(Tree(3792, captured));

        using ProcessAncestryStore store = Store(snapshots.Object);
        store.Start();
        store.OnWriterSeen(3792);
        (await Eventually(() => store.Captured_ == 1)).ShouldBeTrue();

        // Windows recycles PIDs within minutes. The alert being enriched names PID 3792 started
        // half an hour later: a different process. Naming the captured ancestors here would put an
        // innocent process in the console as the writer of a canary.
        store.TryGet(3792, captured.AddMinutes(30)).ShouldBeNull();

        // The same PID at the moment it was captured still answers.
        store.TryGet(3792, captured).ShouldNotBeNull();
    }

    [Fact]
    public async Task The_same_writer_is_captured_once_however_many_files_it_writes()
    {
        DateTime startedAt = new(2026, 10, 25, 9, 0, 0, DateTimeKind.Utc);
        var snapshots = new Mock<IProcessSnapshotService>();
        snapshots.Setup(s => s.BuildProcessTree(3792, It.IsAny<int>())).Returns(Tree(3792, startedAt));

        using ProcessAncestryStore store = Store(snapshots.Object);
        store.Start();

        // One ransomware process encrypting a folder produces thousands of writes. If each asked
        // for a capture, the queue would hold nothing but this PID and every other writer on the
        // machine would be dropped.
        for (int i = 0; i < 5_000; i++)
        {
            store.OnWriterSeen(3792);
        }

        (await Eventually(() => store.Captured_ == 1)).ShouldBeTrue();
        store.Requested.ShouldBe(1);
        store.Dropped.ShouldBe(0);
        snapshots.Verify(s => s.BuildProcessTree(3792, It.IsAny<int>()), Times.Once);
    }

    [Theory]
    [InlineData(0)]       // the system's deferred writes
    [InlineData(4)]       // the System process
    [InlineData(999_999)] // the agent itself
    public void The_system_and_the_agent_are_never_captured(int pid)
    {
        var snapshots = new Mock<IProcessSnapshotService>();

        using ProcessAncestryStore store = Store(snapshots.Object);
        store.Start();

        store.OnWriterSeen(pid);

        store.Requested.ShouldBe(0);
        snapshots.Verify(s => s.BuildProcessTree(It.IsAny<int>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public void The_session_thread_is_not_blocked_by_a_slow_snapshot()
    {
        // AGT-AUD-002: OnWriterSeen runs on the kernel session's own callback thread. WMI, which
        // the snapshot needs for the command line, costs tens of milliseconds; paying that there
        // would make the agent drop kernel events and lose the writes it exists to see.
        var snapshots = new Mock<IProcessSnapshotService>();
        snapshots.Setup(s => s.BuildProcessTree(It.IsAny<int>(), It.IsAny<int>()))
            .Returns(() =>
            {
                Thread.Sleep(2_000);
                return Tree(3792, DateTime.UtcNow);
            });

        using ProcessAncestryStore store = Store(snapshots.Object);
        store.Start();

        long before = Environment.TickCount64;
        for (int pid = 5_000; pid < 5_050; pid++)
        {
            store.OnWriterSeen(pid);
        }

        long elapsed = Environment.TickCount64 - before;
        elapsed.ShouldBeLessThan(500, $"50 calls took {elapsed} ms: the session thread is waiting on the snapshot");
    }

    [Fact]
    public void A_store_that_was_never_started_is_not_healthy()
    {
        // This is what makes GENEALOGY report degraded instead of showing green with no tree
        // behind it: the module state follows IsHealthy, it is not assumed.
        using ProcessAncestryStore store = Store(new Mock<IProcessSnapshotService>().Object);

        store.IsHealthy.ShouldBeFalse();
    }

    [Fact]
    public async Task A_store_that_captures_nothing_it_is_asked_for_is_not_healthy()
    {
        var snapshots = new Mock<IProcessSnapshotService>();
        snapshots.Setup(s => s.BuildProcessTree(It.IsAny<int>(), It.IsAny<int>())).Returns((ProcessTree?)null);

        using ProcessAncestryStore store = Store(snapshots.Object);
        store.Start();

        // Started and draining, so healthy until something is asked of it.
        store.IsHealthy.ShouldBeTrue();

        store.OnWriterSeen(3792);
        (await Eventually(() => store.Missed == 1)).ShouldBeTrue();

        store.IsHealthy.ShouldBeFalse("every capture missed, so the module must not read active");
    }

    [Fact]
    public async Task A_started_store_that_captures_what_it_is_asked_for_is_healthy()
    {
        DateTime startedAt = new(2026, 10, 25, 9, 0, 0, DateTimeKind.Utc);
        var snapshots = new Mock<IProcessSnapshotService>();
        snapshots.Setup(s => s.BuildProcessTree(3792, It.IsAny<int>())).Returns(Tree(3792, startedAt));

        using ProcessAncestryStore store = Store(snapshots.Object);
        store.Start();
        store.OnWriterSeen(3792);

        (await Eventually(() => store.Captured_ == 1)).ShouldBeTrue();
        store.IsHealthy.ShouldBeTrue();
    }
}
