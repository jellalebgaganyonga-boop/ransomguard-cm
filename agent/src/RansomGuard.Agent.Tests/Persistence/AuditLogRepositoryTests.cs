using Microsoft.EntityFrameworkCore;
using RansomGuard.Agent.Core.Persistence;
using RansomGuard.Agent.Core.Persistence.Entities;
using RansomGuard.Agent.Core.Persistence.Repositories;
using Shouldly;

namespace RansomGuard.Agent.Tests.Persistence;

/// <summary>
/// Tests for <see cref="AuditLogRepository"/> including hash chain integrity.
/// </summary>
public sealed class AuditLogRepositoryTests : IDisposable
{
    private readonly AgentDbContext _context;
    private readonly AuditLogRepository _repository;

    public AuditLogRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<AgentDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;

        _context = new AgentDbContext(options);
        _context.Database.OpenConnection();
        _context.Database.EnsureCreated();
        _repository = new AuditLogRepository(_context);
    }

    [Fact]
    public async Task AppendAsync_first_entry_should_have_no_previous_hash()
    {
        await _repository.AppendAsync("AgentStarted", "Agent started successfully");
        AuditLog? latest = await _repository.GetLatestAsync();

        latest.ShouldNotBeNull();
        latest.PreviousHash.ShouldBeNull();
        latest.CurrentHash.ShouldNotBeNullOrEmpty();
        latest.Action.ShouldBe("AgentStarted");
    }

    [Fact]
    public async Task AppendAsync_should_chain_hashes()
    {
        await _repository.AppendAsync("Action1", "First entry");
        AuditLog? first = await _repository.GetLatestAsync();

        await _repository.AppendAsync("Action2", "Second entry");
        AuditLog? second = await _repository.GetLatestAsync();

        second.ShouldNotBeNull();
        second.PreviousHash.ShouldBe(first!.CurrentHash);
        second.CurrentHash.ShouldNotBe(first.CurrentHash);
    }

    [Fact]
    public async Task AppendAsync_should_store_entity_reference()
    {
        Guid entityId = Guid.NewGuid();
        await _repository.AppendAsync("FileDetected", "New file detected", "DetectionEvent", entityId);
        AuditLog? latest = await _repository.GetLatestAsync();

        latest.ShouldNotBeNull();
        latest.EntityType.ShouldBe("DetectionEvent");
        latest.EntityId.ShouldBe(entityId);
    }

    [Fact]
    public async Task VerifyChainIntegrityAsync_should_return_true_for_valid_chain()
    {
        await _repository.AppendAsync("Action1", "Entry 1");
        await _repository.AppendAsync("Action2", "Entry 2");
        await _repository.AppendAsync("Action3", "Entry 3");

        (await _repository.VerifyChainIntegrityAsync()).ShouldBeTrue();
    }

    [Fact]
    public async Task VerifyChainIntegrityAsync_should_return_true_for_empty_chain()
    {
        (await _repository.VerifyChainIntegrityAsync()).ShouldBeTrue();
    }

    [Fact]
    public async Task VerifyChainIntegrityAsync_should_detect_tampered_entry()
    {
        await _repository.AppendAsync("Action1", "Entry 1");
        await _repository.AppendAsync("Action2", "Entry 2");

        AuditLog? firstEntry = await _context.AuditLogs.OrderBy(a => a.CreatedAt).FirstAsync();
        await _context.Database.ExecuteSqlRawAsync(
            "UPDATE AuditLogs SET CurrentHash = 'tampered_hash_value' WHERE Id = {0}", firstEntry.Id);

        (await _repository.VerifyChainIntegrityAsync()).ShouldBeFalse();
    }

    [Fact]
    public async Task GetByTimeRangeAsync_should_filter_entries()
    {
        await _repository.AppendAsync("Action1", "Entry 1");
        await Task.Delay(10);
        await _repository.AppendAsync("Action2", "Entry 2");

        DateTime from = DateTime.UtcNow.AddMinutes(-1);
        DateTime to = DateTime.UtcNow.AddMinutes(1);
        IReadOnlyList<AuditLog> results = await _repository.GetByTimeRangeAsync(from, to);

        results.Count.ShouldBe(2);
        // Verify ascending order
        results[0].CreatedAt.ShouldBeLessThanOrEqualTo(results[1].CreatedAt);
    }

    [Fact]
    public void ComputeHash_should_be_deterministic()
    {
        DateTime timestamp = new(2026, 5, 17, 12, 0, 0, DateTimeKind.Utc);
        string hash1 = AuditLog.ComputeHash("Action", "Details", timestamp, null);
        string hash2 = AuditLog.ComputeHash("Action", "Details", timestamp, null);
        hash1.ShouldBe(hash2);
    }

    [Fact]
    public void ComputeHash_should_differ_for_different_inputs()
    {
        DateTime timestamp = new(2026, 5, 17, 12, 0, 0, DateTimeKind.Utc);
        string hash1 = AuditLog.ComputeHash("Action1", "Details", timestamp, null);
        string hash2 = AuditLog.ComputeHash("Action2", "Details", timestamp, null);
        hash1.ShouldNotBe(hash2);
    }

    public void Dispose()
    {
        _context.Database.CloseConnection();
        _context.Dispose();
    }
}
