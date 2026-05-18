using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using RansomGuard.Agent.Core.Detection.Sentinel;
using RansomGuard.Agent.Core.Persistence;
using RansomGuard.Agent.Core.Persistence.Entities;
using RansomGuard.Agent.Core.Persistence.Repositories;
using Shouldly;

namespace RansomGuard.Agent.Tests.Sentinel;

/// <summary>
/// Tests that canary files are created with Hidden + System attributes (CWE-732).
/// Verifies they remain visible to file enumeration (ransomware vector preserved).
/// </summary>
public sealed class CanaryHiddenAttributeTests : IDisposable
{
    private readonly AgentDbContext _context;
    private readonly CanaryFileService _service;
    private readonly string _testDir;

    public CanaryHiddenAttributeTests()
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

        _testDir = Path.Combine(Path.GetTempPath(), $"ransomguard_hidden_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
    }

    [Fact]
    public async Task Created_canary_should_have_hidden_and_system_attributes()
    {
        SentinelCanary canary = await _service.CreateCanaryAsync(_testDir, "dossier_patient", "0001_");

        FileAttributes attrs = File.GetAttributes(canary.FilePath);
        (attrs & FileAttributes.Hidden).ShouldBe(FileAttributes.Hidden);
        (attrs & FileAttributes.System).ShouldBe(FileAttributes.System);
    }

    [Fact]
    public async Task Hidden_canary_should_be_enumerable_by_directory_get_files()
    {
        await _service.CreateCanaryAsync(_testDir, "dossier_patient", "0001_");

        // Default Directory.GetFiles includes hidden files — same as ransomware's FindFirstFile
        string[] files = Directory.GetFiles(_testDir, "0001_*");
        files.Length.ShouldBeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task Hidden_canary_should_exist_via_file_exists()
    {
        SentinelCanary canary = await _service.CreateCanaryAsync(_testDir, "dossier_patient", "0001_");

        File.Exists(canary.FilePath).ShouldBeTrue();
    }

    [Fact]
    public async Task Detection_should_fire_on_hidden_file_modification()
    {
        SentinelCanary canary = await _service.CreateCanaryAsync(_testDir, "dossier_patient", "0001_");

        // Modify the hidden file
        File.SetAttributes(canary.FilePath, FileAttributes.Normal);
        await File.AppendAllTextAsync(canary.FilePath, "TAMPERED");
        File.SetAttributes(canary.FilePath, FileAttributes.Hidden | FileAttributes.System);

        bool isIntact = await _service.VerifyCanaryIntegrityAsync(canary);
        isIntact.ShouldBeFalse();
    }

    public void Dispose()
    {
        _context.Database.CloseConnection();
        _context.Dispose();
        try { if (Directory.Exists(_testDir)) Directory.Delete(_testDir, true); }
        catch { /* best-effort */ }
    }
}
