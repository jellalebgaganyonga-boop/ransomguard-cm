using Microsoft.EntityFrameworkCore;
using RansomGuard.Agent.Core.Persistence;
using RansomGuard.Agent.Core.Persistence.Entities;
using RansomGuard.Agent.Core.Persistence.Repositories;
using Shouldly;

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
        var detectionEvent = new DetectionEvent { EventType = "Created", FilePath = @"C:\Test\file.txt" };
        await _repository.AddAsync(detectionEvent);
        (await _repository.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task GetByIdAsync_should_return_persisted_event()
    {
        var detectionEvent = new DetectionEvent { EventType = "Modified", FilePath = @"C:\Test\data.xlsx" };
        await _repository.AddAsync(detectionEvent);

        DetectionEvent? result = await _repository.GetByIdAsync(detectionEvent.Id);
        result.ShouldNotBeNull();
        result.EventType.ShouldBe("Modified");
        result.FilePath.ShouldBe(@"C:\Test\data.xlsx");
    }

    [Fact]
    public async Task GetByIdAsync_should_return_null_for_nonexistent()
    {
        DetectionEvent? result = await _repository.GetByIdAsync(Guid.NewGuid());
        result.ShouldBeNull();
    }

    [Fact]
    public async Task GetByTimeRangeAsync_should_filter_by_timestamp()
    {
        DateTime now = DateTime.UtcNow;
        await _repository.AddAsync(new DetectionEvent { EventType = "Created", FilePath = @"C:\1.txt", Timestamp = now.AddHours(-2) });
        await _repository.AddAsync(new DetectionEvent { EventType = "Created", FilePath = @"C:\2.txt", Timestamp = now.AddHours(-1) });
        await _repository.AddAsync(new DetectionEvent { EventType = "Created", FilePath = @"C:\3.txt", Timestamp = now.AddHours(1) });

        IReadOnlyList<DetectionEvent> results = await _repository.GetByTimeRangeAsync(now.AddHours(-3), now);
        results.Count.ShouldBe(2);
    }

    [Fact]
    public async Task DeleteOlderThanAsync_should_remove_old_events()
    {
        DateTime now = DateTime.UtcNow;
        await _repository.AddAsync(new DetectionEvent { EventType = "Created", FilePath = @"C:\old.txt", CreatedAt = now.AddDays(-100) });
        await _repository.AddAsync(new DetectionEvent { EventType = "Created", FilePath = @"C:\new.txt", CreatedAt = now });

        int deleted = await _repository.DeleteOlderThanAsync(now.AddDays(-90));
        deleted.ShouldBe(1);
        (await _repository.CountAsync()).ShouldBe(1);
    }

    public void Dispose()
    {
        _context.Database.CloseConnection();
        _context.Dispose();
    }
}
