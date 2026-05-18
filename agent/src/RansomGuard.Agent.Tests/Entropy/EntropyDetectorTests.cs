using RansomGuard.Agent.Core.Configuration;
using RansomGuard.Agent.Core.Detection.Entropy;
using RansomGuard.Agent.Core.Persistence.Entities;
using Shouldly;

namespace RansomGuard.Agent.Tests.Entropy;

/// <summary>
/// Tests for <see cref="EntropyDetector"/> multi-signal detection rules.
/// </summary>
public sealed class EntropyDetectorTests
{
    private readonly EntropyDetector _detector;

    public EntropyDetectorTests()
    {
        _detector = new EntropyDetector(new EntropyOptions
        {
            AbsoluteThreshold = 7.5,
            DeltaThreshold = 2.5,
            DirectoryShiftThreshold = 1.5,
            DirectoryShiftMinFiles = 5,
            WhitelistedExtensions = [".zip", ".jpg", ".png", ".gz"],
            SusceptibleExtensions = [".txt", ".docx", ".pdf", ".xlsx", ".csv"]
        });
    }

    // Rule 1: Absolute high entropy on susceptible file

    [Fact]
    public void Rule1_DocxWithHighEntropy_FiresAlert()
    {
        var baseline = CreateBaseline(@"C:\Data\patient.docx", 4.5);
        EntropyAlert? alert = _detector.Analyze(@"C:\Data\patient.docx", 7.9, baseline);

        alert.ShouldNotBeNull();
        alert.RuleId.ShouldBe(1);
        alert.RuleName.ShouldBe("AbsoluteHighEntropy");
        alert.Severity.ShouldBe("Critical");
        alert.BaselineEntropy.ShouldBe(4.5);
        alert.CurrentEntropy.ShouldBe(7.9);
    }

    [Fact]
    public void Rule1_DocxWithNormalEntropy_NoAlert()
    {
        var baseline = CreateBaseline(@"C:\Data\patient.docx", 4.5);
        EntropyAlert? alert = _detector.Analyze(@"C:\Data\patient.docx", 5.0, baseline);

        alert.ShouldBeNull();
    }

    [Fact]
    public void Rule1_DocxWithHighBaselineAlready_NoAlert()
    {
        // File was already high entropy at baseline (encrypted document, legitimate)
        var baseline = CreateBaseline(@"C:\Data\encrypted.docx", 7.8);
        EntropyAlert? alert = _detector.Analyze(@"C:\Data\encrypted.docx", 7.9, baseline);

        alert.ShouldBeNull(); // Rule 1 requires baseline < 6.0
    }

    // Rule 2: Sudden entropy delta

    [Fact]
    public void Rule2_LargeDelta_FiresAlert()
    {
        var baseline = CreateBaseline(@"C:\Data\data.bin", 4.0);
        EntropyAlert? alert = _detector.Analyze(@"C:\Data\data.bin", 7.5, baseline);

        alert.ShouldNotBeNull();
        alert.RuleId.ShouldBe(2);
        alert.RuleName.ShouldBe("SuddenEntropyDelta");
        alert.Delta.ShouldBe(3.5, 0.01);
    }

    [Fact]
    public void Rule2_SmallDelta_NoAlert()
    {
        var baseline = CreateBaseline(@"C:\Data\data.bin", 6.0);
        EntropyAlert? alert = _detector.Analyze(@"C:\Data\data.bin", 7.5, baseline);

        alert.ShouldBeNull(); // Delta 1.5 < threshold 2.5
    }

    // Rule 3: Directory-wide shift

    [Fact]
    public void Rule3_MassEncryption_FiresCritical()
    {
        EntropyAlert? alert = _detector.AnalyzeDirectoryShift(@"C:\Hospital\Records", 8, 3.0);

        alert.ShouldNotBeNull();
        alert.RuleId.ShouldBe(3);
        alert.Severity.ShouldBe("Critical");
    }

    [Fact]
    public void Rule3_TooFewFiles_NoAlert()
    {
        EntropyAlert? alert = _detector.AnalyzeDirectoryShift(@"C:\Hospital\Records", 3, 3.0);

        alert.ShouldBeNull(); // 3 < MinFiles 5
    }

    [Fact]
    public void Rule3_SmallShift_NoAlert()
    {
        EntropyAlert? alert = _detector.AnalyzeDirectoryShift(@"C:\Hospital\Records", 10, 0.5);

        alert.ShouldBeNull(); // 0.5 < threshold 1.5
    }

    // Rule 4: Whitelist

    [Fact]
    public void Rule4_WhitelistedExtension_NoAlert()
    {
        var baseline = CreateBaseline(@"C:\Data\archive.zip", 3.0);
        EntropyAlert? alert = _detector.Analyze(@"C:\Data\archive.zip", 7.9, baseline);

        alert.ShouldBeNull();
    }

    [Fact]
    public void Rule4_JpgHighEntropy_NoAlert()
    {
        var baseline = CreateBaseline(@"C:\Data\photo.jpg", 7.0);
        EntropyAlert? alert = _detector.Analyze(@"C:\Data\photo.jpg", 7.9, baseline);

        alert.ShouldBeNull();
    }

    // Edge cases

    [Fact]
    public void NoBaseline_NoAlert()
    {
        EntropyAlert? alert = _detector.Analyze(@"C:\Data\new-file.txt", 5.0, null);
        alert.ShouldBeNull();
    }

    [Fact]
    public void NoBaseline_HighEntropy_SusceptibleFile_FiresRule1()
    {
        // No baseline but very high entropy on susceptible ext → Rule 1 fires (baseline defaults to 0)
        EntropyAlert? alert = _detector.Analyze(@"C:\Data\patient.docx", 7.9, null);

        alert.ShouldNotBeNull();
        alert.RuleId.ShouldBe(1);
    }

    // --- Additional edge-case tests ---

    [Fact]
    public void Rule2_NonSusceptibleExtension_HighDelta_FiresAlert()
    {
        var baseline = CreateBaseline(@"C:\Data\archive.bin", 3.5);
        EntropyAlert? alert = _detector.Analyze(@"C:\Data\archive.bin", 7.5, baseline);

        alert.ShouldNotBeNull();
        alert.RuleId.ShouldBe(2);
        alert.RuleName.ShouldBe("SuddenEntropyDelta");
    }

    [Fact]
    public void Analyze_FileWithNoExtension_NoRule1()
    {
        var baseline = CreateBaseline(@"C:\Data\README", 3.0);
        EntropyAlert? alert = _detector.Analyze(@"C:\Data\README", 7.9, baseline);

        // No extension → not in SusceptibleExtensions → Rule 1 doesn't fire
        // But delta 4.9 > 2.5 and current 7.9 > 7.0 → Rule 2 fires
        alert.ShouldNotBeNull();
        alert.RuleId.ShouldBe(2);
    }

    [Fact]
    public void Analyze_EntropyExactlyAtAbsoluteThreshold_Rule1DoesNotFire()
    {
        var baseline = CreateBaseline(@"C:\Data\notes.txt", 4.0);
        // Exactly at threshold (7.5), not above it → Rule 1 won't fire (requires >)
        // But Rule 2 fires: delta 3.5 > 2.5 and current 7.5 > 7.0
        EntropyAlert? alert = _detector.Analyze(@"C:\Data\notes.txt", 7.5, baseline);

        alert.ShouldNotBeNull();
        alert.RuleId.ShouldBe(2); // Rule 2 fires, not Rule 1
    }

    [Fact]
    public void Rule3_ExactlyAtMinFiles_FiresAlert()
    {
        EntropyAlert? alert = _detector.AnalyzeDirectoryShift(@"C:\Hospital\Records", 5, 2.0);

        alert.ShouldNotBeNull();
        alert.RuleId.ShouldBe(3);
        alert.Severity.ShouldBe("Critical");
    }

    private static EntropyBaseline CreateBaseline(string path, double entropy) => new()
    {
        FilePath = path,
        DirectoryPath = Path.GetDirectoryName(path)!,
        FileExtension = Path.GetExtension(path).ToLowerInvariant(),
        EntropyValue = entropy,
        FileSize = 4096
    };
}
