using Microsoft.EntityFrameworkCore;
using RansomGuard.Agent.Core.Persistence;
using RansomGuard.Agent.Core.Persistence.Entities;
using RansomGuard.Agent.Core.Persistence.Repositories;
using Shouldly;

namespace RansomGuard.Agent.Tests.Sentinel;

/// <summary>
/// Tests for <see cref="SentinelCanaryRepository"/> CRUD operations.
/// </summary>
public sealed class SentinelCanaryRepositoryTests : IDisposable
{
    private readonly AgentDbContext _context;
    private readonly SentinelCanaryRepository _repository;

    public SentinelCanaryRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<AgentDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;

        _context = new AgentDbContext(options);
        _context.Database.OpenConnection();
        _context.Database.EnsureCreated();
        _repository = new SentinelCanaryRepository(_context);
    }

    [Fact]
    public async Task AddAsync_should_persist_canary()
    {
        SentinelCanary canary = CreateCanary();
        await _repository.AddAsync(canary);

        SentinelCanary? result = await _repository.GetByIdAsync(canary.Id);
        result.ShouldNotBeNull();
        result.FilePath.ShouldBe(canary.FilePath);
        result.Status.ShouldBe(CanaryStatus.Active);
    }

    [Fact]
    public async Task GetByFilePathAsync_should_find_canary()
    {
        SentinelCanary canary = CreateCanary();
        await _repository.AddAsync(canary);

        SentinelCanary? result = await _repository.GetByFilePathAsync(canary.FilePath);
        result.ShouldNotBeNull();
        result.Id.ShouldBe(canary.Id);
    }

    [Fact]
    public async Task GetActiveAsync_should_return_only_active()
    {
        var active = CreateCanary(@"C:\Test\active.txt");
        var deleted = CreateCanary(@"C:\Test\deleted.txt");
        deleted.Status = CanaryStatus.Deleted;

        await _repository.AddAsync(active);
        await _repository.AddAsync(deleted);

        IReadOnlyList<SentinelCanary> results = await _repository.GetActiveAsync();
        results.Count.ShouldBe(1);
        results[0].FilePath.ShouldBe(@"C:\Test\active.txt");
    }

    [Fact]
    public async Task GetActiveByDirectoryAsync_should_filter_by_directory()
    {
        var canary1 = CreateCanary(@"C:\Dir1\file.txt", @"C:\Dir1");
        var canary2 = CreateCanary(@"C:\Dir2\file.txt", @"C:\Dir2");

        await _repository.AddAsync(canary1);
        await _repository.AddAsync(canary2);

        IReadOnlyList<SentinelCanary> results = await _repository.GetActiveByDirectoryAsync(@"C:\Dir1");
        results.Count.ShouldBe(1);
    }

    [Fact]
    public async Task UpdateAsync_should_modify_status()
    {
        SentinelCanary canary = CreateCanary();
        await _repository.AddAsync(canary);

        canary.Status = CanaryStatus.Modified;
        await _repository.UpdateAsync(canary);

        SentinelCanary? result = await _repository.GetByIdAsync(canary.Id);
        result.ShouldNotBeNull();
        result.Status.ShouldBe(CanaryStatus.Modified);
    }

    [Fact]
    public async Task AddAlertAsync_should_persist_alert()
    {
        var alert = new CanaryAlert
        {
            CanaryId = Guid.NewGuid(),
            CanaryPath = @"C:\Test\canary.txt",
            AlertType = CanaryAlertType.CanaryModified,
            Severity = AlertSeverity.Critical
        };

        await _repository.AddAlertAsync(alert);

        IReadOnlyList<CanaryAlert> alerts = await _repository.GetAlertsAsync();
        alerts.Count.ShouldBe(1);
        alerts[0].AlertType.ShouldBe(CanaryAlertType.CanaryModified);
        alerts[0].Severity.ShouldBe(AlertSeverity.Critical);
    }

    private static SentinelCanary CreateCanary(string? filePath = null, string? directory = null) => new()
    {
        FilePath = filePath ?? @"C:\Test\0001_dossier_patient.txt",
        FileName = Path.GetFileName(filePath ?? @"C:\Test\0001_dossier_patient.txt"),
        Directory = directory ?? @"C:\Test",
        TemplateUsed = "dossier_patient",
        OriginalContentHash = "abc123def456",
        FileSize = 2048
    };

    public void Dispose()
    {
        _context.Database.CloseConnection();
        _context.Dispose();
    }
}
