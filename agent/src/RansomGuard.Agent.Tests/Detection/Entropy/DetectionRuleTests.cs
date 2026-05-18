using RansomGuard.Agent.Core.Configuration;
using RansomGuard.Agent.Core.Detection.Entropy.DetectionRules;
using RansomGuard.Agent.Core.Persistence.Entities;
using Shouldly;

namespace RansomGuard.Agent.Tests.Detection.Entropy;

/// <summary>
/// Tests for individual entropy detection rules (spec A.3 — 16 tests).
/// </summary>
public sealed class DetectionRuleTests
{
    private static readonly EntropyOptions Options = new()
    {
        AbsoluteThreshold = 7.5,
        DeltaThreshold = 2.5,
        DirectoryShiftThreshold = 1.5,
        DirectoryShiftMinFiles = 5,
        WhitelistedExtensions = [".zip", ".jpg", ".png", ".gz", ".mp4"],
        SusceptibleExtensions = [".txt", ".docx", ".pdf", ".xlsx", ".csv"]
    };

    // --- Rule 1: Absolute Threshold ---

    [Fact]
    public void Rule1_DocxWithEntropyJump_FiresHighSeverity()
    {
        var rule = new AbsoluteThresholdRule(Options);
        var ctx = CreateContext(".docx", 7.9, CreateBaseline(4.5));
        var alert = rule.Evaluate(ctx);
        alert.ShouldNotBeNull();
        alert.RuleId.ShouldBe(1);
        alert.Severity.ShouldBe("Critical");
    }

    [Fact]
    public void Rule1_BaselineAlreadyHigh_DoesNotFire()
    {
        var rule = new AbsoluteThresholdRule(Options);
        var ctx = CreateContext(".docx", 7.9, CreateBaseline(7.0));
        rule.Evaluate(ctx).ShouldBeNull();
    }

    [Fact]
    public void Rule1_NonTextExtension_DoesNotFire()
    {
        var rule = new AbsoluteThresholdRule(Options);
        var ctx = CreateContext(".exe", 7.9, CreateBaseline(3.0));
        rule.Evaluate(ctx).ShouldBeNull();
    }

    [Fact]
    public void Rule1_EntropyBelowThreshold_DoesNotFire()
    {
        var rule = new AbsoluteThresholdRule(Options);
        var ctx = CreateContext(".docx", 6.0, CreateBaseline(4.5));
        rule.Evaluate(ctx).ShouldBeNull();
    }

    // --- Rule 2: Delta Threshold ---

    [Fact]
    public void Rule2_DeltaAbove2_5_Fires()
    {
        var rule = new DeltaThresholdRule(Options);
        var ctx = CreateContext(".bin", 7.5, CreateBaseline(4.0));
        var alert = rule.Evaluate(ctx);
        alert.ShouldNotBeNull();
        alert.RuleId.ShouldBe(2);
        alert.Delta.ShouldBe(3.5, 0.01);
    }

    [Fact]
    public void Rule2_EntropyBelow7_0_DoesNotFire()
    {
        var rule = new DeltaThresholdRule(Options);
        var ctx = CreateContext(".bin", 6.8, CreateBaseline(4.0));
        rule.Evaluate(ctx).ShouldBeNull();
    }

    [Fact]
    public void Rule2_SmallDelta_DoesNotFire()
    {
        var rule = new DeltaThresholdRule(Options);
        var ctx = CreateContext(".bin", 7.5, CreateBaseline(6.0));
        rule.Evaluate(ctx).ShouldBeNull();
    }

    [Fact]
    public void Rule2_NoBaseline_DoesNotFire()
    {
        var rule = new DeltaThresholdRule(Options);
        var ctx = CreateContext(".bin", 7.9, null);
        rule.Evaluate(ctx).ShouldBeNull();
    }

    // --- Rule 3: Directory Shift ---

    [Fact]
    public void Rule3_FiveFilesIn60Seconds_FiresCritical()
    {
        var rule = new DirectoryShiftRule(Options);
        var ctx = CreateContext(".txt", 7.5, null, recentAlerts: 8, avgDelta: 3.0);
        var alert = rule.Evaluate(ctx);
        alert.ShouldNotBeNull();
        alert.RuleId.ShouldBe(3);
        alert.Severity.ShouldBe("Critical");
    }

    [Fact]
    public void Rule3_FourFiles_DoesNotFire()
    {
        var rule = new DirectoryShiftRule(Options);
        var ctx = CreateContext(".txt", 7.5, null, recentAlerts: 4, avgDelta: 3.0);
        rule.Evaluate(ctx).ShouldBeNull();
    }

    [Fact]
    public void Rule3_ShiftBelow1_5_DoesNotFire()
    {
        var rule = new DirectoryShiftRule(Options);
        var ctx = CreateContext(".txt", 7.5, null, recentAlerts: 10, avgDelta: 0.5);
        rule.Evaluate(ctx).ShouldBeNull();
    }

    // --- Rule 4: Whitelist ---

    [Fact]
    public void Rule4_ZipExtension_Suppresses()
    {
        var rule = new ExtensionWhitelistRule(Options);
        var ctx = CreateContext(".zip", 7.9, CreateBaseline(3.0));
        var alert = rule.Evaluate(ctx);
        alert.ShouldNotBeNull();
        alert.Severity.ShouldBe("Suppressed");
    }

    [Fact]
    public void Rule4_LegitimateMp4_Suppresses()
    {
        var rule = new ExtensionWhitelistRule(Options);
        var ctx = CreateContext(".mp4", 7.5, null);
        rule.Evaluate(ctx).ShouldNotBeNull();
    }

    [Fact]
    public void Rule4_DocxNotSuppressed()
    {
        var rule = new ExtensionWhitelistRule(Options);
        var ctx = CreateContext(".docx", 7.9, CreateBaseline(4.0));
        rule.Evaluate(ctx).ShouldBeNull();
    }

    // --- Helpers ---

    private static EntropyEvaluationContext CreateContext(
        string ext, double currentEntropy, EntropyBaseline? baseline,
        int recentAlerts = 0, double avgDelta = 0) => new()
    {
        FilePath = $@"C:\Test\file{ext}",
        FileExtension = ext,
        CurrentEntropy = currentEntropy,
        CurrentSize = 4096,
        Baseline = baseline,
        RecentDirectoryAlertCount = recentAlerts,
        AverageDirectoryDelta = avgDelta
    };

    private static EntropyBaseline CreateBaseline(double entropy) => new()
    {
        FilePath = @"C:\Test\file.txt",
        DirectoryPath = @"C:\Test",
        FileExtension = ".txt",
        EntropyValue = entropy,
        FileSize = 4096
    };
}
