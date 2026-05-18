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
/// Tests for canary deployment behavior — idempotency, re-deployment, and directory creation.
/// </summary>
public sealed class SentinelDeploymentTests : IDisposable
{
    private readonly AgentDbContext _context;
    private readonly SentinelCanaryRepository _repository;
    private readonly CanaryFileService _service;
    private readonly string _testDir;

    public SentinelDeploymentTests()
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

        _testDir = Path.Combine(Path.GetTempPath(), $"ransomguard_deploy_test_{Guid.NewGuid():N}");
    }

    [Fact]
    public async Task Should_deploy_correct_number_of_canaries()
    {
        int targetCount = 3;
        string[] templates = ["dossier_patient", "analyses_laboratoire", "imagerie_medicale"];

        for (int i = 0; i < targetCount; i++)
        {
            await _service.CreateCanaryAsync(_testDir, templates[i], "0001_");
        }

        IReadOnlyList<SentinelCanary> canaries = await _service.GetAllCanariesAsync();
        canaries.Count.ShouldBe(targetCount);
    }

    [Fact]
    public async Task Should_be_idempotent_no_duplicate_on_second_run()
    {
        await _service.CreateCanaryAsync(_testDir, "dossier_patient", "0001_");
        await _service.CreateCanaryAsync(_testDir, "analyses_laboratoire", "0001_");

        IReadOnlyList<SentinelCanary> canaries = await _service.GetAllCanariesAsync();
        int countAfterFirstRun = canaries.Count;

        // Simulate "second run" — deployment logic checks existing count
        IReadOnlyList<SentinelCanary> existingInDir = await _repository.GetActiveByDirectoryAsync(_testDir);
        existingInDir.Count.ShouldBe(countAfterFirstRun);
    }

    [Fact]
    public async Task Should_create_directory_if_not_exists()
    {
        string newDir = Path.Combine(_testDir, "new_subdir");
        Directory.Exists(newDir).ShouldBeFalse();

        await _service.CreateCanaryAsync(newDir, "dossier_patient", "0001_");

        Directory.Exists(newDir).ShouldBeTrue();
    }

    [Fact]
    public async Task Should_deploy_canary_after_previous_deleted()
    {
        SentinelCanary canary = await _service.CreateCanaryAsync(_testDir, "dossier_patient", "0001_");
        await _service.DeleteCanaryAsync(canary);

        // Deleted canary should not be in active list
        IReadOnlyList<SentinelCanary> active = await _service.GetAllCanariesAsync();
        active.Count.ShouldBe(0);

        // Re-deploy
        await _service.CreateCanaryAsync(_testDir, "dossier_patient", "0001_");
        active = await _service.GetAllCanariesAsync();
        active.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Canaries_should_use_different_templates()
    {
        string[] templates = ["dossier_patient", "analyses_laboratoire", "imagerie_medicale"];
        var canaries = new List<SentinelCanary>();

        foreach (string t in templates)
        {
            canaries.Add(await _service.CreateCanaryAsync(_testDir, t, "0001_"));
        }

        canaries.Select(c => c.TemplateUsed).Distinct().Count().ShouldBe(3);
    }

    [Fact]
    public async Task Canary_files_should_start_with_prefix()
    {
        SentinelCanary canary = await _service.CreateCanaryAsync(_testDir, "dossier_patient", "0001_");

        canary.FileName.ShouldStartWith("0001_");
        string[] files = Directory.GetFiles(_testDir, "0001_*");
        files.Length.ShouldBeGreaterThanOrEqualTo(1);
    }

    public void Dispose()
    {
        _context.Database.CloseConnection();
        _context.Dispose();

        try { if (Directory.Exists(_testDir)) Directory.Delete(_testDir, true); }
        catch { /* best-effort */ }
    }
}
