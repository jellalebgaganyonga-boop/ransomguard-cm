using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Moq;
using RansomGuard.Agent.Core.Detection.Genealogy;
using Shouldly;

namespace RansomGuard.Agent.Tests.EndToEnd;

/// <summary>
/// Live process tests for MITRE T1490 (Inhibit System Recovery) pattern detection.
/// Spawns real processes and validates the pattern detector against live process snapshots.
/// Non-destructive: does NOT execute vssadmin delete shadows.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class MitreT1490LiveProcessTests
{
    private readonly ProcessSnapshotService _snapshotService;

    public MitreT1490LiveProcessTests()
    {
        _snapshotService = new ProcessSnapshotService(
            new Mock<ILogger<ProcessSnapshotService>>().Object);
    }

    [Fact(Timeout = 60_000)]
    public void VssadminDeleteShadows_SyntheticSnapshot_DetectsT1490()
    {
        // Use current test process as the live snapshot source
        // (cmd.exe /c exits too fast for WMI to capture)
        ProcessSnapshot? liveSnapshot = _snapshotService.CaptureProcess(Environment.ProcessId);

        liveSnapshot.ShouldNotBeNull("Should capture current test process");
        liveSnapshot.ProcessName.ShouldNotBeNullOrEmpty();

        // Build synthetic snapshot simulating vssadmin with realistic command line
        var syntheticSnapshot = new ProcessSnapshot
        {
            ProcessId = liveSnapshot.ProcessId,
            ParentProcessId = liveSnapshot.ParentProcessId,
            ProcessName = "vssadmin",
            ExecutablePath = @"C:\Windows\System32\vssadmin.exe",
            CommandLine = "vssadmin.exe delete shadows /all /quiet",
            StartTime = liveSnapshot.StartTime,
            WorkingSetBytes = liveSnapshot.WorkingSetBytes
        };

        var tree = new ProcessTree
        {
            Root = syntheticSnapshot,
            Ancestors = [new ProcessSnapshot
            {
                ProcessId = liveSnapshot.ParentProcessId,
                ParentProcessId = 0,
                ProcessName = "cmd",
                CommandLine = "cmd.exe /c vssadmin.exe delete shadows /all /quiet",
                WorkingSetBytes = 0
            }]
        };

        IReadOnlyList<SuspiciousPatternFlag> flags = SuspiciousPatternDetector.Analyze(tree);

        flags.ShouldContain(f => f.TechniqueId == "T1490",
            "T1490 vssadmin delete shadows pattern must fire");
        flags.First(f => f.TechniqueId == "T1490").Severity.ShouldBe("Critical");
    }

    [Fact(Timeout = 60_000)]
    public void BcdeditRecoveryDisable_SyntheticSnapshot_DetectsT1490()
    {
        var snapshot = new ProcessSnapshot
        {
            ProcessId = 9999,
            ParentProcessId = 1234,
            ProcessName = "bcdedit",
            ExecutablePath = @"C:\Windows\System32\bcdedit.exe",
            CommandLine = "bcdedit /set {default} recoveryenabled no",
            StartTime = DateTime.UtcNow,
            WorkingSetBytes = 1024
        };

        var tree = new ProcessTree
        {
            Root = snapshot,
            Ancestors = [new ProcessSnapshot
            {
                ProcessId = 1234,
                ParentProcessId = 0,
                ProcessName = "powershell",
                WorkingSetBytes = 0
            }]
        };

        IReadOnlyList<SuspiciousPatternFlag> flags = SuspiciousPatternDetector.Analyze(tree);

        flags.ShouldContain(f => f.TechniqueId == "T1490");
        flags.First(f => f.TechniqueId == "T1490").Severity.ShouldBe("Critical");
    }

    [Fact(Timeout = 60_000)]
    public void LiveProcessCapture_CurrentProcess_SnapshotValid()
    {
        // Verify we can capture a real live process for forensic analysis
        ProcessSnapshot? snapshot = _snapshotService.CaptureProcess(Environment.ProcessId);

        snapshot.ShouldNotBeNull();
        snapshot.ProcessId.ShouldBe(Environment.ProcessId);
        snapshot.ProcessName.ShouldNotBeNullOrEmpty();
        snapshot.WorkingSetBytes.ShouldBeGreaterThan(0);

        // Build full tree
        ProcessTree? tree = _snapshotService.BuildProcessTree(Environment.ProcessId);
        tree.ShouldNotBeNull();
        tree.Depth.ShouldBeGreaterThan(0, "Test process should have at least one ancestor");
        tree.Summary.ShouldContain("->");
    }
}
