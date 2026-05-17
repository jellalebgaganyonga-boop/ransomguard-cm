using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RansomGuard.Agent.Core.Persistence;

namespace RansomGuard.Agent.Tests.Persistence;

/// <summary>
/// Tests that <see cref="AgentDbContext"/> creates the expected schema.
/// </summary>
public sealed class AgentDbContextTests : IDisposable
{
    private readonly AgentDbContext _context;

    public AgentDbContextTests()
    {
        var options = new DbContextOptionsBuilder<AgentDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;

        _context = new AgentDbContext(options);
        _context.Database.OpenConnection();
        _context.Database.EnsureCreated();
    }

    [Fact]
    public void Should_create_detection_events_table()
    {
        _context.DetectionEvents.Should().NotBeNull();
    }

    [Fact]
    public void Should_create_alerts_table()
    {
        _context.Alerts.Should().NotBeNull();
    }

    [Fact]
    public void Should_create_agent_states_table()
    {
        _context.AgentStates.Should().NotBeNull();
    }

    [Fact]
    public void Should_create_audit_logs_table()
    {
        _context.AuditLogs.Should().NotBeNull();
    }

    [Fact]
    public async Task Should_have_all_four_tables_accessible()
    {
        int eventCount = await _context.DetectionEvents.CountAsync();
        int alertCount = await _context.Alerts.CountAsync();
        int stateCount = await _context.AgentStates.CountAsync();
        int auditCount = await _context.AuditLogs.CountAsync();

        eventCount.Should().Be(0);
        alertCount.Should().Be(0);
        stateCount.Should().Be(0);
        auditCount.Should().Be(0);
    }

    public void Dispose()
    {
        _context.Database.CloseConnection();
        _context.Dispose();
    }
}
