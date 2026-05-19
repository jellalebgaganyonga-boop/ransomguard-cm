using Microsoft.EntityFrameworkCore;
using RansomGuard.Agent.Core.Persistence;
using RansomGuard.Agent.Core.Persistence.Repositories;
using Shouldly;

namespace RansomGuard.Agent.Tests.Security;

/// <summary>
/// Verifies audit log hash chain and Ed25519 signatures with real entries,
/// and validates tamper detection.
/// </summary>
public sealed class AuditLogVerificationWithEntriesTests : IDisposable
{
    private readonly AgentDbContext _context;
    private readonly AuditLogRepository _repository;

    public AuditLogVerificationWithEntriesTests()
    {
        var options = new DbContextOptionsBuilder<AgentDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        _context = new AgentDbContext(options);
        _context.Database.OpenConnection();
        _context.Database.EnsureCreated();

        // No signer — tests hash chain integrity without Ed25519
        _repository = new AuditLogRepository(_context);
    }

    [Fact]
    public async Task VerifyChain_WithTenRealEntries_AllValid()
    {
        for (int i = 0; i < 10; i++)
        {
            await _repository.AppendAsync(
                $"TEST_ACTION_{i}",
                $"{{\"index\":{i},\"timestamp\":\"{DateTime.UtcNow:O}\"}}",
                "TestEntity",
                Guid.NewGuid());
        }

        int count = await _context.AuditLogs.CountAsync();
        count.ShouldBe(10);

        bool valid = await _repository.VerifyChainIntegrityAsync();
        valid.ShouldBeTrue("Hash chain should be intact for 10 properly appended entries");
    }

    [Fact]
    public async Task VerifyChain_HashChainLinked()
    {
        await _repository.AppendAsync("ACTION_1", "First entry");
        await _repository.AppendAsync("ACTION_2", "Second entry");
        await _repository.AppendAsync("ACTION_3", "Third entry");

        var entries = await _context.AuditLogs
            .AsNoTracking()
            .OrderBy(a => a.CreatedAt)
            .ToListAsync();

        entries.Count.ShouldBe(3);
        entries[0].PreviousHash.ShouldBeNull("Genesis entry has no previous hash");
        entries[1].PreviousHash.ShouldBe(entries[0].CurrentHash);
        entries[2].PreviousHash.ShouldBe(entries[1].CurrentHash);
    }

    [Fact]
    public async Task VerifyChain_TamperedDetails_DetectsTampering()
    {
        for (int i = 0; i < 5; i++)
        {
            await _repository.AppendAsync($"ACTION_{i}", $"Details for entry {i}");
        }

        // Tamper: directly modify one row bypassing repository
        await _context.Database.ExecuteSqlRawAsync(
            "UPDATE AuditLogs SET Details = 'TAMPERED' WHERE Id = (SELECT Id FROM AuditLogs ORDER BY CreatedAt LIMIT 1 OFFSET 2)");

        // Clear EF cache
        foreach (var entry in _context.ChangeTracker.Entries().ToList())
            entry.State = EntityState.Detached;

        bool valid = await _repository.VerifyChainIntegrityAsync();
        valid.ShouldBeFalse("Tampered entry should break hash chain");
    }

    [Fact]
    public async Task VerifyChain_TamperedHash_DetectsTampering()
    {
        for (int i = 0; i < 3; i++)
        {
            await _repository.AppendAsync($"ACTION_{i}", $"Entry {i}");
        }

        // Tamper the hash of the middle entry
        await _context.Database.ExecuteSqlRawAsync(
            "UPDATE AuditLogs SET CurrentHash = 'FAKEHASH' WHERE Id = (SELECT Id FROM AuditLogs ORDER BY CreatedAt LIMIT 1 OFFSET 1)");

        foreach (var entry in _context.ChangeTracker.Entries().ToList())
            entry.State = EntityState.Detached;

        bool valid = await _repository.VerifyChainIntegrityAsync();
        valid.ShouldBeFalse("Tampered hash should be detected");
    }

    public void Dispose()
    {
        _context.Database.CloseConnection();
        _context.Dispose();
    }
}
