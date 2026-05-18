using RansomGuard.Agent.Core.Detection.Genealogy;
using RansomGuard.Agent.Core.Detection.Genealogy.Patterns;
using Shouldly;

namespace RansomGuard.Agent.Tests.Detection.Genealogy;

/// <summary>
/// Tests for the 2 additional MITRE patterns: T1566 (EmailAttachment) and T1218 (SignedBinaryProxy).
/// </summary>
public sealed class AdditionalPatternTests
{
    [Fact]
    public void EmailAttachment_OutlookSpawnsExeOutsideProgramFiles_Flags()
    {
        var rule = new EmailAttachmentRule();
        var tree = BuildTree(
            root: Snap("malware", execPath: @"C:\Users\test\AppData\Local\Temp\invoice.exe"),
            parent: Snap("outlook"));

        var flag = rule.Evaluate(tree);
        flag.ShouldNotBeNull();
        flag.TechniqueId.ShouldBe("T1566");
    }

    [Fact]
    public void EmailAttachment_OutlookSpawnsExeInProgramFiles_DoesNotFlag()
    {
        var rule = new EmailAttachmentRule();
        var tree = BuildTree(
            root: Snap("legitimate", execPath: @"C:\Program Files\App\app.exe"),
            parent: Snap("outlook"));

        rule.Evaluate(tree).ShouldBeNull();
    }

    [Fact]
    public void SignedBinaryProxy_ExplorerSpawnsRundll32_Flags()
    {
        var rule = new SignedBinaryProxyRule();
        var tree = BuildTree(
            root: Snap("rundll32"),
            parent: Snap("explorer"));

        var flag = rule.Evaluate(tree);
        flag.ShouldNotBeNull();
        flag.TechniqueId.ShouldBe("T1218");
    }

    [Fact]
    public void SignedBinaryProxy_ExplorerSpawnsMshta_Flags()
    {
        var rule = new SignedBinaryProxyRule();
        var tree = BuildTree(
            root: Snap("mshta"),
            parent: Snap("explorer"));

        rule.Evaluate(tree).ShouldNotBeNull();
    }

    [Fact]
    public void SignedBinaryProxy_ExplorerSpawnsNotepad_DoesNotFlag()
    {
        var rule = new SignedBinaryProxyRule();
        var tree = BuildTree(
            root: Snap("notepad"),
            parent: Snap("explorer"));

        rule.Evaluate(tree).ShouldBeNull();
    }

    [Fact]
    public void EmailAttachment_NonOutlookParent_DoesNotFlag()
    {
        var rule = new EmailAttachmentRule();
        var tree = BuildTree(
            root: Snap("app", execPath: @"C:\Temp\app.exe"),
            parent: Snap("explorer"));

        rule.Evaluate(tree).ShouldBeNull();
    }

    private static ProcessTree BuildTree(ProcessSnapshot root, ProcessSnapshot parent) => new()
    {
        Root = root,
        Ancestors = [parent]
    };

    private static ProcessSnapshot Snap(string name, string? execPath = null) => new()
    {
        ProcessId = Random.Shared.Next(1000, 65000),
        ParentProcessId = Random.Shared.Next(1, 1000),
        ProcessName = name,
        ExecutablePath = execPath,
        CommandLine = null,
        StartTime = DateTime.UtcNow,
        WorkingSetBytes = 0
    };
}
