using RansomGuard.Agent.Core.Detection.Sentinel;
using Shouldly;

namespace RansomGuard.Agent.Tests.Sentinel;

/// <summary>
/// Tests for <see cref="CanaryContentGenerator"/> medical content generation.
/// </summary>
public sealed class CanaryContentGeneratorTests
{
    [Theory]
    [InlineData("patient_record")]
    [InlineData("lab_results")]
    [InlineData("medical_imaging")]
    [InlineData("pharmacy_prescription")]
    [InlineData("consultation_report")]
    // The French names a canary planted before 2026-10-25 still carries.
    [InlineData("dossier_patient")]
    [InlineData("analyses_laboratoire")]
    [InlineData("imagerie_medicale")]
    [InlineData("prescription_pharmacie")]
    [InlineData("rapport_consultation")]
    public void Should_generate_content_for_all_templates(string template)
    {
        (byte[] content, string extension) = CanaryContentGenerator.Generate(template);

        content.Length.ShouldBeGreaterThan(100);
        extension.ShouldBe(".txt");
    }

    [Fact]
    public void Patient_record_reads_like_a_patient_record()
    {
        (byte[] content, _) = CanaryContentGenerator.Generate("patient_record");
        string text = System.Text.Encoding.UTF8.GetString(content);

        text.ShouldContain("MEDICAL RECORD");
        text.ShouldContain("Patient");
        text.ShouldContain("doctor");
        text.ShouldContain("MEDICAL HISTORY");
    }

    [Fact]
    public void Lab_analysis_should_contain_medical_values()
    {
        (byte[] content, _) = CanaryContentGenerator.Generate("lab_results");
        string text = System.Text.Encoding.UTF8.GetString(content);

        text.ShouldContain("FULL BLOOD COUNT");
        text.ShouldContain("Haemoglobin");
        text.ShouldContain("glucose");
    }

    [Fact]
    public void Imaging_report_should_contain_radiology_terms()
    {
        (byte[] content, _) = CanaryContentGenerator.Generate("medical_imaging");
        string text = System.Text.Encoding.UTF8.GetString(content);

        text.ShouldContain("MEDICAL IMAGING");
        text.ShouldContain("RADIOLOGY");
    }

    [Fact]
    public void Prescription_should_contain_medication_structure()
    {
        (byte[] content, _) = CanaryContentGenerator.Generate("pharmacy_prescription");
        string text = System.Text.Encoding.UTF8.GetString(content);

        text.ShouldContain("PRESCRIPTION");
        text.ShouldContain("PRESCRIPTION");
    }

    [Fact]
    public void Consultation_should_contain_clinical_exam()
    {
        (byte[] content, _) = CanaryContentGenerator.Generate("consultation_report");
        string text = System.Text.Encoding.UTF8.GetString(content);

        text.ShouldContain("CONSULTATION");
        text.ShouldContain("EXAMINATION");
    }

    [Fact]
    public void Two_generations_should_produce_different_content()
    {
        (byte[] content1, _) = CanaryContentGenerator.Generate("patient_record");
        (byte[] content2, _) = CanaryContentGenerator.Generate("patient_record");

        string hash1 = CanaryContentGenerator.ComputeHash(content1);
        string hash2 = CanaryContentGenerator.ComputeHash(content2);

        hash1.ShouldNotBe(hash2);
    }

    [Fact]
    public void Hash_should_be_deterministic_for_same_content()
    {
        byte[] content = System.Text.Encoding.UTF8.GetBytes("test content");

        string hash1 = CanaryContentGenerator.ComputeHash(content);
        string hash2 = CanaryContentGenerator.ComputeHash(content);

        hash1.ShouldBe(hash2);
        hash1.Length.ShouldBe(64); // SHA-256 hex = 64 chars
    }

    [Fact]
    public void Content_should_reference_cameroon_law()
    {
        (byte[] content, _) = CanaryContentGenerator.Generate("patient_record");
        string text = System.Text.Encoding.UTF8.GetString(content);

        text.ShouldContain("2024/017");
    }

    [Fact]
    public void Unknown_template_should_fall_back_to_patient_record()
    {
        (byte[] content, _) = CanaryContentGenerator.Generate("unknown_template");
        string text = System.Text.Encoding.UTF8.GetString(content);

        text.ShouldContain("MEDICAL RECORD");
    }
}
