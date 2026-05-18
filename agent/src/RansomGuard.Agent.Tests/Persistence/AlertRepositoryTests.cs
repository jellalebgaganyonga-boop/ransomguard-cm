using Microsoft.EntityFrameworkCore;
using RansomGuard.Agent.Core.Persistence;
using RansomGuard.Agent.Core.Persistence.Entities;
using RansomGuard.Agent.Core.Persistence.Repositories;
using Shouldly;

namespace RansomGuard.Agent.Tests.Persistence;

/// <summary>
/// Tests for <see cref="AlertRepository"/> CRUD operations.
/// </summary>
public sealed class AlertRepositoryTests : IDisposable
{
    private readonly AgentDbContext _context;
    private readonly AlertRepository _repository;

    public AlertRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<AgentDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;

        _context = new AgentDbContext(options);
        _context.Database.OpenConnection();
        _context.Database.EnsureCreated();
        _repository = new AlertRepository(_context);
    }

    [Fact]
    public async Task AddAsync_should_persist_alert()
    {
        var alert = new Alert { Severity = "High", Title = "Suspicious mass file modification", Description = "50 files modified in 2 seconds" };
        await _repository.AddAsync(alert);

        Alert? result = await _repository.GetByIdAsync(alert.Id);
        result.ShouldNotBeNull();
        result.Severity.ShouldBe("High");
        result.Acknowledged.ShouldBeFalse();
    }

    [Fact]
    public async Task GetUnacknowledgedAsync_should_return_only_unacknowledged()
    {
        await _repository.AddAsync(new Alert { Severity = "High", Title = "Alert 1", Description = "Desc 1" });
        await _repository.AddAsync(new Alert { Severity = "Low", Title = "Alert 2", Description = "Desc 2", Acknowledged = true });

        IReadOnlyList<Alert> results = await _repository.GetUnacknowledgedAsync();
        results.Count.ShouldBe(1);
        results[0].Title.ShouldBe("Alert 1");
    }

    [Fact]
    public async Task AcknowledgeAsync_should_mark_alert_acknowledged()
    {
        var alert = new Alert { Severity = "Critical", Title = "Ransomware detected", Description = "Details" };
        await _repository.AddAsync(alert);

        await _repository.AcknowledgeAsync(alert.Id);

        Alert? result = await _context.Alerts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == alert.Id);
        result.ShouldNotBeNull();
        result.Acknowledged.ShouldBeTrue();
    }

    public void Dispose()
    {
        _context.Database.CloseConnection();
        _context.Dispose();
    }
}
