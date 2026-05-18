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
/// Tests for <see cref="CanaryFileService"/> file creation and integrity verification.
/// </summary>
public sealed class CanaryFileServiceTests : IDisposable
{
    private readonly AgentDbContext _context;
    private readonly SentinelCanaryRepository _repository;
    private readonly CanaryFileService _service;
    private readonly string _testDir;

    public CanaryFileServiceTests()
    {
        var options = new DbContextOptionsBuilder<AgentDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;

        _context = new AgentDbContext(options);
        _context.Database.OpenConnection();
        _context.Database.EnsureCreated();
        _repository = new SentinelCanaryRepository(_context);

        var logger = new Mock<ILogger<CanaryFileService>>();
        _service = new CanaryFileService(_repository, logger.Object);

        _testDir = Path.Combine(Path.GetTempPath(), $"ransomguard_canary_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
    }

    [Fact]
    public async Task CreateCanaryAsync_should_create_file_on_disk()
    {
        SentinelCanary canary = await _service.CreateCanaryAsync(_testDir, "dossier_patient", "0001_");

        File.Exists(canary.FilePath).ShouldBeTrue();
        canary.FileName.ShouldStartWith("0001_dossier_patient_");
        canary.Directory.ShouldBe(_testDir);
        canary.TemplateUsed.ShouldBe("dossier_patient");
        canary.FileSize.ShouldBeGreaterThan(0);
        canary.OriginalContentHash.Length.ShouldBe(64);
    }

    [Fact]
    public async Task CreateCanaryAsync_should_persist_to_database()
    {
        SentinelCanary canary = await _service.CreateCanaryAsync(_testDir, "analyses_laboratoire", "0001_");

        SentinelCanary? fromDb = await _repository.GetByIdAsync(canary.Id);
        fromDb.ShouldNotBeNull();
        fromDb.FilePath.ShouldBe(canary.FilePath);
    }

    [Fact]
    public async Task VerifyCanaryIntegrityAsync_should_return_true_for_unmodified()
    {
        SentinelCanary canary = await _service.CreateCanaryAsync(_testDir, "dossier_patient", "0001_");

        bool isIntact = await _service.VerifyCanaryIntegrityAsync(canary);
        isIntact.ShouldBeTrue();
    }

    [Fact]
    public async Task VerifyCanaryIntegrityAsync_should_return_false_for_modified()
    {
        SentinelCanary canary = await _service.CreateCanaryAsync(_testDir, "dossier_patient", "0001_");

        // Tamper with the file
        await File.AppendAllTextAsync(canary.FilePath, "RANSOMWARE WAS HERE");

        bool isIntact = await _service.VerifyCanaryIntegrityAsync(canary);
        isIntact.ShouldBeFalse();
    }

    [Fact]
    public async Task VerifyCanaryIntegrityAsync_should_return_false_for_missing()
    {
        SentinelCanary canary = await _service.CreateCanaryAsync(_testDir, "dossier_patient", "0001_");
        File.Delete(canary.FilePath);

        bool isIntact = await _service.VerifyCanaryIntegrityAsync(canary);
        isIntact.ShouldBeFalse();
    }

    [Fact]
    public async Task DeleteCanaryAsync_should_remove_file_and_update_status()
    {
        SentinelCanary canary = await _service.CreateCanaryAsync(_testDir, "dossier_patient", "0001_");
        await _service.DeleteCanaryAsync(canary);

        File.Exists(canary.FilePath).ShouldBeFalse();
        canary.Status.ShouldBe(CanaryStatus.Deleted);
    }

    [Fact]
    public async Task GetAllCanariesAsync_should_return_active_only()
    {
        await _service.CreateCanaryAsync(_testDir, "dossier_patient", "0001_");
        SentinelCanary toDelete = await _service.CreateCanaryAsync(_testDir, "analyses_laboratoire", "0001_");
        await _service.DeleteCanaryAsync(toDelete);

        IReadOnlyList<SentinelCanary> active = await _service.GetAllCanariesAsync();
        active.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Two_canaries_should_have_different_content()
    {
        SentinelCanary c1 = await _service.CreateCanaryAsync(_testDir, "dossier_patient", "0001_");
        SentinelCanary c2 = await _service.CreateCanaryAsync(_testDir, "dossier_patient", "0001_");

        c1.OriginalContentHash.ShouldNotBe(c2.OriginalContentHash);
    }

    public void Dispose()
    {
        _context.Database.CloseConnection();
        _context.Dispose();

        try { if (Directory.Exists(_testDir)) Directory.Delete(_testDir, true); }
        catch { /* best-effort cleanup */ }
    }
}
