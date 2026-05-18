using RansomGuard.Agent.Core.Detection.Genealogy;
using Shouldly;

namespace RansomGuard.Agent.Tests.Genealogy;

/// <summary>
/// Tests for <see cref="SuspiciousPatternDetector"/> MITRE ATT&CK pattern detection.
/// </summary>
public sealed class SuspiciousPatternTests
{
    [Fact]
    public void VssadminDeleteShadows_Detected_T1490()
    {
        var tree = BuildTree(
            root: CreateSnapshot("vssadmin", cmdLine: "vssadmin.exe delete shadows /all /quiet"),
            parent: CreateSnapshot("cmd"));

        IReadOnlyList<SuspiciousPatternFlag> flags = SuspiciousPatternDetector.Analyze(tree);

        flags.ShouldContain(f => f.TechniqueId == "T1490");
        flags.ShouldContain(f => f.Severity == "Critical");
    }

    [Fact]
    public void BcdeditDisableRecovery_Detected_T1490()
    {
        var tree = BuildTree(
            root: CreateSnapshot("bcdedit", cmdLine: "bcdedit /set {default} recoveryenabled no"),
            parent: CreateSnapshot("cmd"));

        IReadOnlyList<SuspiciousPatternFlag> flags = SuspiciousPatternDetector.Analyze(tree);

        flags.ShouldContain(f => f.TechniqueId == "T1490");
    }

    [Fact]
    public void OfficeSpawningPowerShell_Detected_T1059()
    {
        var tree = BuildTree(
            root: CreateSnapshot("powershell"),
            parent: CreateSnapshot("winword"));

        IReadOnlyList<SuspiciousPatternFlag> flags = SuspiciousPatternDetector.Analyze(tree);

        flags.ShouldContain(f => f.TechniqueId == "T1059.001");
        flags.ShouldContain(f => f.Description.Contains("winword"));
    }

    [Fact]
    public void EncodedPowerShell_Detected_T1027()
    {
        var tree = BuildTree(
            root: CreateSnapshot("powershell", cmdLine: "powershell.exe -EncodedCommand JABzAD0AIgBIAGU="),
            parent: CreateSnapshot("cmd"));

        IReadOnlyList<SuspiciousPatternFlag> flags = SuspiciousPatternDetector.Analyze(tree);

        flags.ShouldContain(f => f.TechniqueId == "T1027");
    }

    [Fact]
    public void ExeInTemp_Detected_T1059()
    {
        var tree = BuildTree(
            root: CreateSnapshot("malware", execPath: @"C:\Users\test\AppData\Local\Temp\malware.exe"),
            parent: CreateSnapshot("explorer"));

        IReadOnlyList<SuspiciousPatternFlag> flags = SuspiciousPatternDetector.Analyze(tree);

        flags.ShouldContain(f => f.TechniqueId == "T1059");
        flags.ShouldContain(f => f.Description.Contains("%TEMP%"));
    }

    [Fact]
    public void LegitimateProcess_NoFlags()
    {
        var tree = BuildTree(
            root: CreateSnapshot("notepad", execPath: @"C:\Windows\System32\notepad.exe"),
            parent: CreateSnapshot("explorer"));

        IReadOnlyList<SuspiciousPatternFlag> flags = SuspiciousPatternDetector.Analyze(tree);

        flags.Count.ShouldBe(0);
    }

    [Fact]
    public void MultiplePatterns_AllDetected()
    {
        // vssadmin called by PowerShell with encoded command
        var grandparent = CreateSnapshot("winword");
        var parent = CreateSnapshot("powershell", cmdLine: "powershell.exe -EncodedCommand JABz");
        var root = CreateSnapshot("vssadmin", cmdLine: "vssadmin.exe delete shadows /all");

        var tree = new ProcessTree
        {
            Root = root,
            Ancestors = [parent, grandparent]
        };

        IReadOnlyList<SuspiciousPatternFlag> flags = SuspiciousPatternDetector.Analyze(tree);

        // Should detect: T1490 (vssadmin), T1027 (encoded PowerShell), T1059.001 (winword → powershell)
        flags.Count.ShouldBeGreaterThanOrEqualTo(3);
    }

    private static ProcessTree BuildTree(ProcessSnapshot root, ProcessSnapshot parent) => new()
    {
        Root = root,
        Ancestors = [parent]
    };

    private static ProcessSnapshot CreateSnapshot(
        string name, string? cmdLine = null, string? execPath = null) => new()
    {
        ProcessId = Random.Shared.Next(1000, 65000),
        ParentProcessId = Random.Shared.Next(1, 1000),
        ProcessName = name,
        ExecutablePath = execPath,
        CommandLine = cmdLine,
        StartTime = DateTime.UtcNow,
        WorkingSetBytes = 0
    };
}
