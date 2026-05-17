using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RansomGuard.Agent.Core.Persistence;
using RansomGuard.Agent.Core.Persistence.Entities;
using RansomGuard.Agent.Core.Persistence.Repositories;

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
        var alert = new Alert
        {
            Severity = "High",
            Title = "Suspicious mass file modification",
            Description = "50 files modified in 2 seconds in C:\\Patient-Records"
        };

        await _repository.AddAsync(alert);
        Alert? result = await _repository.GetByIdAsync(alert.Id);

        result.Should().NotBeNull();
        result!.Severity.Should().Be("High");
        result.Acknowledged.Should().BeFalse();
    }

    [Fact]
    public async Task GetUnacknowledgedAsync_should_return_only_unacknowledged()
    {
        var alert1 = new Alert { Severity = "High", Title = "Alert 1", Description = "Desc 1" };
        var alert2 = new Alert { Severity = "Low", Title = "Alert 2", Description = "Desc 2", Acknowledged = true };

        await _repository.AddAsync(alert1);
        await _repository.AddAsync(alert2);

        IReadOnlyList<Alert> results = await _repository.GetUnacknowledgedAsync();

        results.Should().HaveCount(1);
        results[0].Title.Should().Be("Alert 1");
    }

    [Fact]
    public async Task AcknowledgeAsync_should_mark_alert_acknowledged()
    {
        var alert = new Alert { Severity = "Critical", Title = "Ransomware detected", Description = "Details" };
        await _repository.AddAsync(alert);

        await _repository.AcknowledgeAsync(alert.Id);

        // Need a fresh context read to see the update
        Alert? result = await _context.Alerts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == alert.Id);
        result.Should().NotBeNull();
        result!.Acknowledged.Should().BeTrue();
    }

    public void Dispose()
    {
        _context.Database.CloseConnection();
        _context.Dispose();
    }
}
