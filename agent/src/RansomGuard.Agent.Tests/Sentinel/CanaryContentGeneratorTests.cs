using RansomGuard.Agent.Core.Detection.Sentinel;
using Shouldly;

namespace RansomGuard.Agent.Tests.Sentinel;

/// <summary>
/// Tests for <see cref="CanaryContentGenerator"/> medical content generation.
/// </summary>
public sealed class CanaryContentGeneratorTests
{
    [Theory]
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
    public void Patient_record_should_contain_french_medical_keywords()
    {
        (byte[] content, _) = CanaryContentGenerator.Generate("dossier_patient");
        string text = System.Text.Encoding.UTF8.GetString(content);

        text.ShouldContain("DOSSIER MÉDICAL");
        text.ShouldContain("Patient");
        text.ShouldContain("Médecin");
        text.ShouldContain("ANTÉCÉDENTS");
    }

    [Fact]
    public void Lab_analysis_should_contain_medical_values()
    {
        (byte[] content, _) = CanaryContentGenerator.Generate("analyses_laboratoire");
        string text = System.Text.Encoding.UTF8.GetString(content);

        text.ShouldContain("HÉMATOLOGIE");
        text.ShouldContain("Hémoglobine");
        text.ShouldContain("Glycémie");
    }

    [Fact]
    public void Imaging_report_should_contain_radiology_terms()
    {
        (byte[] content, _) = CanaryContentGenerator.Generate("imagerie_medicale");
        string text = System.Text.Encoding.UTF8.GetString(content);

        text.ShouldContain("IMAGERIE MÉDICALE");
        text.ShouldContain("RADIOLOGIE");
    }

    [Fact]
    public void Prescription_should_contain_medication_structure()
    {
        (byte[] content, _) = CanaryContentGenerator.Generate("prescription_pharmacie");
        string text = System.Text.Encoding.UTF8.GetString(content);

        text.ShouldContain("ORDONNANCE");
        text.ShouldContain("PRESCRIPTION");
    }

    [Fact]
    public void Consultation_should_contain_clinical_exam()
    {
        (byte[] content, _) = CanaryContentGenerator.Generate("rapport_consultation");
        string text = System.Text.Encoding.UTF8.GetString(content);

        text.ShouldContain("CONSULTATION");
        text.ShouldContain("EXAMEN CLINIQUE");
    }

    [Fact]
    public void Two_generations_should_produce_different_content()
    {
        (byte[] content1, _) = CanaryContentGenerator.Generate("dossier_patient");
        (byte[] content2, _) = CanaryContentGenerator.Generate("dossier_patient");

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
        (byte[] content, _) = CanaryContentGenerator.Generate("dossier_patient");
        string text = System.Text.Encoding.UTF8.GetString(content);

        text.ShouldContain("2024/017");
    }

    [Fact]
    public void Unknown_template_should_fall_back_to_patient_record()
    {
        (byte[] content, _) = CanaryContentGenerator.Generate("unknown_template");
        string text = System.Text.Encoding.UTF8.GetString(content);

        text.ShouldContain("DOSSIER MÉDICAL");
    }
}
