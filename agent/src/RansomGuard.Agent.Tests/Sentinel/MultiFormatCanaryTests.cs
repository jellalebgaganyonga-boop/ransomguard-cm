using System.IO.Compression;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using RansomGuard.Agent.Core.Detection.Sentinel;
using RansomGuard.Agent.Core.Detection.Sentinel.Generators;
using RansomGuard.Agent.Core.Persistence;
using RansomGuard.Agent.Core.Persistence.Entities;
using RansomGuard.Agent.Core.Persistence.Repositories;
using Shouldly;

namespace RansomGuard.Agent.Tests.Sentinel;

/// <summary>
/// Tests for multi-format canary file generation (.txt, .docx).
/// Verifies each format produces valid files that open correctly.
/// </summary>
public sealed class MultiFormatCanaryTests : IDisposable
{
    private readonly AgentDbContext _context;
    private readonly CanaryFileService _service;
    private readonly string _testDir;

    public MultiFormatCanaryTests()
    {
        var options = new DbContextOptionsBuilder<AgentDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;

        _context = new AgentDbContext(options);
        _context.Database.OpenConnection();
        _context.Database.EnsureCreated();
        var repository = new SentinelCanaryRepository(_context);
        var logger = new Mock<ILogger<CanaryFileService>>();
        _service = new CanaryFileService(repository, logger.Object);

        _testDir = Path.Combine(Path.GetTempPath(), $"ransomguard_format_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
    }

    [Fact]
    public void Txt_generator_should_produce_content()
    {
        var gen = new TxtCanaryGenerator();
        byte[] content = gen.Generate("dossier_patient");

        content.Length.ShouldBeGreaterThan(100);
        gen.Extension.ShouldBe(".txt");
    }

    [Fact]
    public void Docx_generator_should_produce_valid_openxml()
    {
        var gen = new DocxCanaryGenerator();
        byte[] content = gen.Generate("dossier_patient");

        // .docx is a ZIP archive — verify it has valid ZIP structure
        content.Length.ShouldBeGreaterThan(100);
        gen.Extension.ShouldBe(".docx");

        // Verify ZIP magic bytes (PK\x03\x04)
        content[0].ShouldBe((byte)'P');
        content[1].ShouldBe((byte)'K');

        // Verify it's a valid ZIP containing OpenXml parts
        using var ms = new MemoryStream(content);
        using var archive = new ZipArchive(ms, ZipArchiveMode.Read);
        archive.Entries.Count.ShouldBeGreaterThan(0);

        // OpenXml .docx must contain [Content_Types].xml
        archive.GetEntry("[Content_Types].xml").ShouldNotBeNull();
    }

    [Fact]
    public void Docx_content_should_contain_french_medical_text()
    {
        var gen = new DocxCanaryGenerator();
        byte[] content = gen.Generate("dossier_patient");

        // Extract the document.xml and check for medical content
        using var ms = new MemoryStream(content);
        using var archive = new ZipArchive(ms, ZipArchiveMode.Read);

        var docEntry = archive.GetEntry("word/document.xml");
        docEntry.ShouldNotBeNull();

        using var reader = new StreamReader(docEntry!.Open());
        string xml = reader.ReadToEnd();

        xml.ShouldContain("DOSSIER");
    }

    [Fact]
    public async Task Create_canary_with_docx_extension_should_produce_valid_file()
    {
        SentinelCanary canary = await _service.CreateCanaryAsync(_testDir, "dossier_patient", "0001_", ".docx");

        canary.FileName.ShouldEndWith(".docx");
        File.Exists(canary.FilePath).ShouldBeTrue();

        byte[] content = await File.ReadAllBytesAsync(canary.FilePath);
        content[0].ShouldBe((byte)'P');
        content[1].ShouldBe((byte)'K');
    }

    [Fact]
    public async Task Docx_canary_should_trigger_detection_when_modified()
    {
        SentinelCanary canary = await _service.CreateCanaryAsync(_testDir, "dossier_patient", "0001_", ".docx");

        (await _service.VerifyCanaryIntegrityAsync(canary)).ShouldBeTrue();

        // Tamper: append bytes to the file
        File.SetAttributes(canary.FilePath, FileAttributes.Normal);
        await File.AppendAllTextAsync(canary.FilePath, "RANSOMWARE");

        (await _service.VerifyCanaryIntegrityAsync(canary)).ShouldBeFalse();
    }

    [Fact]
    public void Factory_should_return_correct_generator_for_known_extensions()
    {
        CanaryFileGeneratorFactory.GetGenerator(".txt").Extension.ShouldBe(".txt");
        CanaryFileGeneratorFactory.GetGenerator(".docx").Extension.ShouldBe(".docx");
    }

    [Fact]
    public void Factory_should_default_to_txt_for_unknown_extension()
    {
        CanaryFileGeneratorFactory.GetGenerator(".xyz").Extension.ShouldBe(".txt");
    }

    public void Dispose()
    {
        _context.Database.CloseConnection();
        _context.Dispose();
        try { if (Directory.Exists(_testDir)) Directory.Delete(_testDir, true); }
        catch { /* best-effort */ }
    }
}
