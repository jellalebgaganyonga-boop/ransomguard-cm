using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RansomGuard.Agent.Core.Persistence;
using RansomGuard.Agent.Core.Persistence.Entities;
using RansomGuard.Agent.Core.Persistence.Repositories;

namespace RansomGuard.Agent.Tests.Persistence;

/// <summary>
/// Tests for <see cref="DetectionEventRepository"/> CRUD operations.
/// </summary>
public sealed class DetectionEventRepositoryTests : IDisposable
{
    private readonly AgentDbContext _context;
    private readonly DetectionEventRepository _repository;

    public DetectionEventRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<AgentDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;

        _context = new AgentDbContext(options);
        _context.Database.OpenConnection();
        _context.Database.EnsureCreated();
        _repository = new DetectionEventRepository(_context);
    }

    [Fact]
    public async Task AddAsync_should_persist_event()
    {
        var detectionEvent = new DetectionEvent
        {
            EventType = "Created",
            FilePath = @"C:\Test\file.txt"
        };

        await _repository.AddAsync(detectionEvent);

        int count = await _repository.CountAsync();
        count.Should().Be(1);
    }

    [Fact]
    public async Task GetByIdAsync_should_return_persisted_event()
    {
        var detectionEvent = new DetectionEvent
        {
            EventType = "Modified",
            FilePath = @"C:\Test\data.xlsx"
        };

        await _repository.AddAsync(detectionEvent);
        DetectionEvent? result = await _repository.GetByIdAsync(detectionEvent.Id);

        result.Should().NotBeNull();
        result!.EventType.Should().Be("Modified");
        result.FilePath.Should().Be(@"C:\Test\data.xlsx");
    }

    [Fact]
    public async Task GetByIdAsync_should_return_null_for_nonexistent()
    {
        DetectionEvent? result = await _repository.GetByIdAsync(Guid.NewGuid());

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetByTimeRangeAsync_should_filter_by_timestamp()
    {
        DateTime now = DateTime.UtcNow;
        var event1 = new DetectionEvent { EventType = "Created", FilePath = @"C:\1.txt", Timestamp = now.AddHours(-2) };
        var event2 = new DetectionEvent { EventType = "Created", FilePath = @"C:\2.txt", Timestamp = now.AddHours(-1) };
        var event3 = new DetectionEvent { EventType = "Created", FilePath = @"C:\3.txt", Timestamp = now.AddHours(1) };

        await _repository.AddAsync(event1);
        await _repository.AddAsync(event2);
        await _repository.AddAsync(event3);

        IReadOnlyList<DetectionEvent> results = await _repository.GetByTimeRangeAsync(
            now.AddHours(-3), now);

        results.Should().HaveCount(2);
    }

    [Fact]
    public async Task DeleteOlderThanAsync_should_remove_old_events()
    {
        DateTime now = DateTime.UtcNow;
        var oldEvent = new DetectionEvent
        {
            EventType = "Created",
            FilePath = @"C:\old.txt",
            CreatedAt = now.AddDays(-100)
        };
        var newEvent = new DetectionEvent
        {
            EventType = "Created",
            FilePath = @"C:\new.txt",
            CreatedAt = now
        };

        await _repository.AddAsync(oldEvent);
        await _repository.AddAsync(newEvent);

        int deleted = await _repository.DeleteOlderThanAsync(now.AddDays(-90));

        deleted.Should().Be(1);
        int remaining = await _repository.CountAsync();
        remaining.Should().Be(1);
    }

    public void Dispose()
    {
        _context.Database.CloseConnection();
        _context.Dispose();
    }
}
