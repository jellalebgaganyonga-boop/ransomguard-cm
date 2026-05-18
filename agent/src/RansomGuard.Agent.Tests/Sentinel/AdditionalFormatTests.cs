using System.IO.Compression;
using DocumentFormat.OpenXml.Packaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using RansomGuard.Agent.Core.Detection.Sentinel;
using RansomGuard.Agent.Core.Detection.Sentinel.Generators;
using RansomGuard.Agent.Core.Persistence;
using RansomGuard.Agent.Core.Persistence.Repositories;
using Shouldly;
using SixLabors.ImageSharp;

namespace RansomGuard.Agent.Tests.Sentinel;

/// <summary>
/// Tests for .pdf, .jpg, .xlsx canary generators — validates file format correctness.
/// </summary>
public sealed class AdditionalFormatTests : IDisposable
{
    private readonly AgentDbContext _context;
    private readonly CanaryFileService _service;
    private readonly string _testDir;

    public AdditionalFormatTests()
    {
        var options = new DbContextOptionsBuilder<AgentDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        _context = new AgentDbContext(options);
        _context.Database.OpenConnection();
        _context.Database.EnsureCreated();
        var repo = new SentinelCanaryRepository(_context);
        _service = new CanaryFileService(repo, new Mock<ILogger<CanaryFileService>>().Object);
        _testDir = Path.Combine(Path.GetTempPath(), $"ransomguard_format2_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
    }

    // --- PDF ---

    [Fact]
    public void Pdf_generator_should_produce_valid_pdf_magic_bytes()
    {
        var gen = new PdfCanaryGenerator();
        byte[] content = gen.Generate("dossier_patient");

        content.Length.ShouldBeGreaterThan(100);
        gen.Extension.ShouldBe(".pdf");

        // PDF magic bytes: %PDF
        System.Text.Encoding.ASCII.GetString(content, 0, 4).ShouldBe("%PDF");
    }

    [Fact]
    public async Task Pdf_canary_should_trigger_detection_when_modified()
    {
        var canary = await _service.CreateCanaryAsync(_testDir, "analyses_laboratoire", "0001_", ".pdf");
        (await _service.VerifyCanaryIntegrityAsync(canary)).ShouldBeTrue();

        File.SetAttributes(canary.FilePath, FileAttributes.Normal);
        await File.AppendAllTextAsync(canary.FilePath, "TAMPERED");
        (await _service.VerifyCanaryIntegrityAsync(canary)).ShouldBeFalse();
    }

    // --- JPEG ---

    [Fact]
    public void Jpg_generator_should_produce_valid_jpeg_markers()
    {
        var gen = new JpgCanaryGenerator();
        byte[] content = gen.Generate("imagerie_medicale");

        content.Length.ShouldBeGreaterThan(100);
        gen.Extension.ShouldBe(".jpg");

        // JPEG SOI marker: FF D8 FF
        content[0].ShouldBe((byte)0xFF);
        content[1].ShouldBe((byte)0xD8);
        content[2].ShouldBe((byte)0xFF);

        // JPEG EOI marker: FF D9 at end
        content[^2].ShouldBe((byte)0xFF);
        content[^1].ShouldBe((byte)0xD9);
    }

    [Fact]
    public void Jpg_should_load_as_valid_image()
    {
        var gen = new JpgCanaryGenerator();
        byte[] content = gen.Generate("imagerie_medicale");

        using var ms = new MemoryStream(content);
        using Image image = Image.Load(ms);

        image.Width.ShouldBe(800);
        image.Height.ShouldBe(600);
    }

    [Fact]
    public async Task Jpg_canary_should_trigger_detection_when_modified()
    {
        var canary = await _service.CreateCanaryAsync(_testDir, "imagerie_medicale", "0001_", ".jpg");
        (await _service.VerifyCanaryIntegrityAsync(canary)).ShouldBeTrue();

        File.SetAttributes(canary.FilePath, FileAttributes.Normal);
        await File.AppendAllTextAsync(canary.FilePath, "TAMPERED");
        (await _service.VerifyCanaryIntegrityAsync(canary)).ShouldBeFalse();
    }

    // --- XLSX ---

    [Fact]
    public void Xlsx_generator_should_produce_valid_zip_structure()
    {
        var gen = new XlsxCanaryGenerator();
        byte[] content = gen.Generate("registre_patients");

        content.Length.ShouldBeGreaterThan(100);
        gen.Extension.ShouldBe(".xlsx");

        // XLSX is a ZIP archive: PK magic bytes
        content[0].ShouldBe((byte)'P');
        content[1].ShouldBe((byte)'K');
    }

    [Fact]
    public void Xlsx_should_open_as_valid_spreadsheet()
    {
        var gen = new XlsxCanaryGenerator();
        byte[] content = gen.Generate("registre_patients");

        using var ms = new MemoryStream(content);
        using SpreadsheetDocument doc = SpreadsheetDocument.Open(ms, false);

        doc.WorkbookPart.ShouldNotBeNull();
        doc.WorkbookPart!.Workbook!.Sheets!.Count().ShouldBe(1);
    }

    [Fact]
    public async Task Xlsx_canary_should_trigger_detection_when_modified()
    {
        var canary = await _service.CreateCanaryAsync(_testDir, "registre_patients", "0001_", ".xlsx");
        (await _service.VerifyCanaryIntegrityAsync(canary)).ShouldBeTrue();

        File.SetAttributes(canary.FilePath, FileAttributes.Normal);
        await File.AppendAllTextAsync(canary.FilePath, "TAMPERED");
        (await _service.VerifyCanaryIntegrityAsync(canary)).ShouldBeFalse();
    }

    // --- Factory ---

    [Theory]
    [InlineData(".pdf")]
    [InlineData(".jpg")]
    [InlineData(".xlsx")]
    [InlineData(".docx")]
    [InlineData(".txt")]
    public void Factory_should_return_correct_generator_for_all_extensions(string ext)
    {
        ICanaryFileGenerator gen = CanaryFileGeneratorFactory.GetGenerator(ext);
        gen.Extension.ShouldBe(ext);
    }

    public void Dispose()
    {
        _context.Database.CloseConnection();
        _context.Dispose();
        try { if (Directory.Exists(_testDir)) Directory.Delete(_testDir, true); }
        catch { /* best-effort */ }
    }
}
