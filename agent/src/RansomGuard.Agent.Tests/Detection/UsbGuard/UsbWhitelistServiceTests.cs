using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using RansomGuard.Agent.Core.Detection.UsbGuard;
using RansomGuard.Agent.Core.Persistence;
using RansomGuard.Agent.Core.Persistence.Entities;
using Shouldly;

namespace RansomGuard.Agent.Tests.Detection.UsbGuard;

/// <summary>
/// Tests for <see cref="UsbWhitelistService"/> — serial hashing, whitelist lookup,
/// expiry, policy levels, and temporary approval.
/// </summary>
public sealed class UsbWhitelistServiceTests : IDisposable
{
    private readonly AgentDbContext _context;
    private readonly UsbWhitelistService _service;
    private readonly byte[] _salt;

    public UsbWhitelistServiceTests()
    {
        var options = new DbContextOptionsBuilder<AgentDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        _context = new AgentDbContext(options);
        _context.Database.OpenConnection();
        _context.Database.EnsureCreated();

        _salt = RandomNumberGenerator.GetBytes(32);
        _service = new UsbWhitelistService(_context, new Mock<ILogger<UsbWhitelistService>>().Object, _salt);
    }

    [Fact]
    public async Task Serial_Hashed_With_Persistent_Salt()
    {
        var entry = await _service.AddAsync("SERIAL123", "Test Drive", UsbPolicyLevel.StandardScan);

        entry.SerialNumberHash.ShouldNotBeNullOrEmpty();
        entry.SerialNumberHash.ShouldNotBe("SERIAL123", "Serial should be hashed, not stored plaintext");
        entry.SerialNumberHash.Length.ShouldBeGreaterThan(20, "Hash should be base64-encoded SHA-256");
    }

    [Fact]
    public async Task Whitelist_Check_By_Hash_Returns_Correct_Result()
    {
        await _service.AddAsync("KNOWN_SERIAL", "Office Drive", UsbPolicyLevel.Trusted);

        var (isWhitelisted, level) = await _service.IsWhitelistedAsync("KNOWN_SERIAL");
        isWhitelisted.ShouldBeTrue();
        level.ShouldBe(UsbPolicyLevel.Trusted);

        var (unknown, _) = await _service.IsWhitelistedAsync("UNKNOWN_SERIAL");
        unknown.ShouldBeFalse();
    }

    [Fact]
    public async Task ExpiresAt_Auto_Deactivates_Entry()
    {
        // Add entry that expired in the past
        await _service.AddAsync("EXPIRED_SERIAL", "Old Drive", UsbPolicyLevel.StandardScan,
            DateTime.UtcNow.AddHours(-1));

        var (isWhitelisted, _) = await _service.IsWhitelistedAsync("EXPIRED_SERIAL");
        isWhitelisted.ShouldBeFalse("Expired entry should not match");
    }

    [Fact]
    public async Task PolicyLevel_Trusted_Identified()
    {
        await _service.AddAsync("TRUSTED_SERIAL", "Admin Drive", UsbPolicyLevel.Trusted);

        var (_, level) = await _service.IsWhitelistedAsync("TRUSTED_SERIAL");
        level.ShouldBe(UsbPolicyLevel.Trusted);
    }

    [Fact]
    public async Task PolicyLevel_BlockAlways_Returns_Not_Whitelisted()
    {
        await _service.AddAsync("BLOCKED_SERIAL", "Suspicious Drive", UsbPolicyLevel.BlockAlways);

        var (isWhitelisted, level) = await _service.IsWhitelistedAsync("BLOCKED_SERIAL");
        isWhitelisted.ShouldBeFalse("BlockAlways should return not whitelisted");
        level.ShouldBe(UsbPolicyLevel.BlockAlways);
    }

    [Fact]
    public async Task Concurrent_Whitelist_Queries_Thread_Safe()
    {
        await _service.AddAsync("CONCURRENT_SERIAL", "Shared Drive", UsbPolicyLevel.StandardScan);

        var tasks = Enumerable.Range(0, 20).Select(_ =>
            _service.IsWhitelistedAsync("CONCURRENT_SERIAL"));

        var results = await Task.WhenAll(tasks);

        foreach (var (isWhitelisted, _) in results)
        {
            isWhitelisted.ShouldBeTrue();
        }
    }

    [Fact]
    public async Task Temporary_Approval_Sets_Expiry()
    {
        var entry = await _service.TemporaryApproveAsync("TEMP_SERIAL", TimeSpan.FromHours(2), "Emergency maintenance");

        entry.ExpiresAt.ShouldNotBeNull();
        entry.ExpiresAt!.Value.ShouldBeGreaterThan(DateTime.UtcNow.AddHours(1));
        entry.Description.ShouldContain("TEMP");
    }

    [Fact]
    public async Task ListActive_Returns_Only_Active_Entries()
    {
        await _service.AddAsync("ACTIVE1", "Drive A", UsbPolicyLevel.StandardScan);
        await _service.AddAsync("ACTIVE2", "Drive B", UsbPolicyLevel.DeepScan);
        var toRemove = await _service.AddAsync("REMOVED", "Drive C", UsbPolicyLevel.Trusted);
        await _service.RemoveAsync(toRemove.Id);

        var active = await _service.ListActiveAsync();
        active.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Remove_Deactivates_Entry()
    {
        var entry = await _service.AddAsync("TO_REMOVE", "Remove Me", UsbPolicyLevel.StandardScan);
        await _service.RemoveAsync(entry.Id);

        var (isWhitelisted, _) = await _service.IsWhitelistedAsync("TO_REMOVE");
        isWhitelisted.ShouldBeFalse();
    }

    [Fact]
    public async Task Query_Performance_With_100_Entries()
    {
        // Add 100 entries
        for (int i = 0; i < 100; i++)
        {
            await _service.AddAsync($"SERIAL_{i:D4}", $"Device {i}", UsbPolicyLevel.StandardScan);
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var (found, _) = await _service.IsWhitelistedAsync("SERIAL_0050");
        sw.Stop();

        found.ShouldBeTrue();
        sw.ElapsedMilliseconds.ShouldBeLessThan(500, "Whitelist lookup should be fast");
    }

    public void Dispose()
    {
        _context.Database.CloseConnection();
        _context.Dispose();
    }
}
