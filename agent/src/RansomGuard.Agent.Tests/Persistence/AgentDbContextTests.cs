using Microsoft.EntityFrameworkCore;
using RansomGuard.Agent.Core.Persistence;
using Shouldly;

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
    public void Should_create_detection_events_table() => _context.DetectionEvents.ShouldNotBeNull();

    [Fact]
    public void Should_create_alerts_table() => _context.Alerts.ShouldNotBeNull();

    [Fact]
    public void Should_create_agent_states_table() => _context.AgentStates.ShouldNotBeNull();

    [Fact]
    public void Should_create_audit_logs_table() => _context.AuditLogs.ShouldNotBeNull();

    [Fact]
    public async Task Should_have_all_four_tables_accessible()
    {
        (await _context.DetectionEvents.CountAsync()).ShouldBe(0);
        (await _context.Alerts.CountAsync()).ShouldBe(0);
        (await _context.AgentStates.CountAsync()).ShouldBe(0);
        (await _context.AuditLogs.CountAsync()).ShouldBe(0);
    }

    public void Dispose()
    {
        _context.Database.CloseConnection();
        _context.Dispose();
    }
}
