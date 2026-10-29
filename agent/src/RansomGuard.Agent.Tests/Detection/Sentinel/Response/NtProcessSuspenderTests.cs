using System.Diagnostics;
using RansomGuard.Agent.Core.Detection.Sentinel.Response;
using Shouldly;

namespace RansomGuard.Agent.Tests.Detection.Sentinel.Response;

/// <summary>ADR-026, 2b: NtSuspendProcess on a real child process, then NtResumeProcess.</summary>
public sealed class NtProcessSuspenderTests
{
    [Fact(Timeout = 30_000)]
    public async Task A_real_process_is_suspended_every_thread_then_resumed()
    {
        using Process child = Process.Start(new ProcessStartInfo("cmd.exe", "/c ping -n 60 127.0.0.1 >nul")
        {
            CreateNoWindow = true, UseShellExecute = false,
        })!;
        try
        {
            var suspender = new NtProcessSuspender();

            suspender.Suspend(child.Id, out string? error).ShouldBe(SuspendOutcome.Done, error);
            await WaitUntilAsync(() => AllThreads(child.Id, suspended: true), "every thread suspended");

            suspender.Resume(child.Id, out error).ShouldBe(SuspendOutcome.Done, error);
            await WaitUntilAsync(() => AllThreads(child.Id, suspended: false), "every thread resumed");
        }
        finally
        {
            child.Kill(entireProcessTree: true);
        }
    }

    [Fact]
    public void A_PID_nobody_holds_is_not_found()
    {
        // PIDs are multiples of 4; a huge odd value belongs to nobody.
        new NtProcessSuspender().Suspend(2_000_000_001, out string? error).ShouldBe(SuspendOutcome.NotFound, error);
    }

    private static bool AllThreads(int pid, bool suspended)
    {
        using Process process = Process.GetProcessById(pid);
        process.Refresh();
        var threads = process.Threads.Cast<ProcessThread>().ToList();
        return threads.Count > 0 && threads.All(t =>
            (t.ThreadState == System.Diagnostics.ThreadState.Wait && t.WaitReason == ThreadWaitReason.Suspended) == suspended);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, string what)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            DateTime.UtcNow.ShouldBeLessThan(deadline, $"not reached within 5 s: {what}");
            await Task.Delay(50);
        }
    }
}
