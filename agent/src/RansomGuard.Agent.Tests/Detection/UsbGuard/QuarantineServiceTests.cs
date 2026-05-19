using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using RansomGuard.Agent.Core.Detection.UsbGuard.Quarantine;
using RansomGuard.Agent.Core.Persistence;
using RansomGuard.Agent.Core.Persistence.Entities;
using Shouldly;

namespace RansomGuard.Agent.Tests.Detection.UsbGuard;

/// <summary>
/// Tests for <see cref="QuarantineService"/> — AES-256-GCM encryption, restore, cleanup.
/// </summary>
public sealed class QuarantineServiceTests : IDisposable
{
    private readonly string _testDir;
    private readonly string _quarantineDir;
    private readonly AgentDbContext _context;
    private readonly QuarantineService _service;
    private readonly byte[] _key;

    public QuarantineServiceTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"rg_qtest_{Guid.NewGuid():N}");
        _quarantineDir = Path.Combine(_testDir, "quarantine");
        Directory.CreateDirectory(_testDir);

        var options = new DbContextOptionsBuilder<AgentDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        _context = new AgentDbContext(options);
        _context.Database.OpenConnection();
        _context.Database.EnsureCreated();

        _key = RandomNumberGenerator.GetBytes(32);
        _service = new QuarantineService(_context,
            new Mock<ILogger<QuarantineService>>().Object,
            _quarantineDir, _key);
    }

    [Fact]
    public async Task File_Quarantined_And_Encrypted_Successfully()
    {
        string filePath = Path.Combine(_testDir, "suspicious.exe");
        await File.WriteAllBytesAsync(filePath, new byte[] { 0x4D, 0x5A, 0x90, 0x00, 0x03 });

        var record = await _service.QuarantineAsync(filePath, "PE disguised as PDF",
            QuarantineSeverity.Critical, "SERIAL_HASH");

        record.ShouldNotBeNull();
        record.IsEncrypted.ShouldBeTrue();
        record.OriginalSha256.ShouldNotBeNullOrEmpty();
        record.QuarantineSha256.ShouldNotBeNullOrEmpty();
        File.Exists(record.QuarantinePath).ShouldBeTrue("Quarantine file should exist");
    }

    [Fact]
    public async Task Original_File_Moved_Not_Copied()
    {
        string filePath = Path.Combine(_testDir, "malware.bin");
        await File.WriteAllBytesAsync(filePath, RandomNumberGenerator.GetBytes(1024));

        await _service.QuarantineAsync(filePath, "Suspicious binary",
            QuarantineSeverity.High, "SERIAL");

        File.Exists(filePath).ShouldBeFalse("Original file should be deleted after quarantine");
    }

    [Fact]
    public async Task Hash_Verified_Before_And_After_Encryption()
    {
        string filePath = Path.Combine(_testDir, "data.bin");
        byte[] content = RandomNumberGenerator.GetBytes(2048);
        await File.WriteAllBytesAsync(filePath, content);
        string expectedHash = Convert.ToHexString(SHA256.HashData(content));

        var record = await _service.QuarantineAsync(filePath, "Test",
            QuarantineSeverity.Medium, "SERIAL");

        record.OriginalSha256.ShouldBe(expectedHash);
        record.QuarantineSha256.ShouldNotBe(expectedHash, "Encrypted hash should differ from original");
    }

    [Fact]
    public async Task Restore_Verifies_Hash_Integrity()
    {
        string filePath = Path.Combine(_testDir, "restore_test.bin");
        byte[] content = RandomNumberGenerator.GetBytes(512);
        await File.WriteAllBytesAsync(filePath, content);

        var record = await _service.QuarantineAsync(filePath, "Test restore",
            QuarantineSeverity.Low, "SERIAL");

        bool restored = await _service.RestoreAsync(record.Id, "Approved by admin");

        restored.ShouldBeTrue();
        File.Exists(filePath).ShouldBeTrue("File should be restored to original path");

        byte[] restoredContent = await File.ReadAllBytesAsync(filePath);
        restoredContent.ShouldBe(content, "Restored content should match original");
    }

    [Fact]
    public async Task Retention_Auto_Cleanup_At_Expiry()
    {
        string filePath = Path.Combine(_testDir, "expired.bin");
        await File.WriteAllBytesAsync(filePath, new byte[] { 1, 2, 3 });

        var record = await _service.QuarantineAsync(filePath, "Will expire",
            QuarantineSeverity.Low, "SERIAL");

        // Directly update the tracked entity's RetainUntil via EF
        var tracked = await _context.Set<QuarantinedFile>().FindAsync(record.Id);
        tracked.ShouldNotBeNull();
        // Use reflection on init-only property (test-only hack)
        typeof(QuarantinedFile).GetProperty(nameof(QuarantinedFile.RetainUntil))!
            .SetValue(tracked, DateTime.UtcNow.AddDays(-1));
        await _context.SaveChangesAsync();

        foreach (var entry in _context.ChangeTracker.Entries().ToList())
            entry.State = EntityState.Detached;

        int cleaned = await _service.CleanupExpiredAsync();
        cleaned.ShouldBeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task Restore_Nonexistent_Returns_False()
    {
        bool result = await _service.RestoreAsync(Guid.NewGuid(), "Test");
        result.ShouldBeFalse();
    }

    [Fact]
    public async Task List_Active_Returns_Only_Non_Restored()
    {
        string f1 = Path.Combine(_testDir, "active.bin");
        string f2 = Path.Combine(_testDir, "restored.bin");
        await File.WriteAllBytesAsync(f1, new byte[] { 1 });
        await File.WriteAllBytesAsync(f2, new byte[] { 2 });

        await _service.QuarantineAsync(f1, "Active", QuarantineSeverity.Low, "S1");
        var toRestore = await _service.QuarantineAsync(f2, "Will restore", QuarantineSeverity.Low, "S2");
        await _service.RestoreAsync(toRestore.Id, "Approved");

        var active = await _service.ListActiveAsync();
        active.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Concurrent_Quarantines_Handled()
    {
        var tasks = Enumerable.Range(0, 5).Select(async i =>
        {
            string path = Path.Combine(_testDir, $"concurrent_{i}.bin");
            await File.WriteAllBytesAsync(path, RandomNumberGenerator.GetBytes(256));
            return _service.QuarantineAsync(path, $"Concurrent test {i}",
                QuarantineSeverity.Medium, "SERIAL");
        });

        var records = await Task.WhenAll(tasks);
        records.Length.ShouldBe(5);
        records.Select(r => r.Id).Distinct().Count().ShouldBe(5);
    }

    [Fact]
    public async Task Quarantine_Creates_Directory_If_Missing()
    {
        string newDir = Path.Combine(_testDir, "new_quarantine");
        var service = new QuarantineService(_context,
            new Mock<ILogger<QuarantineService>>().Object, newDir, _key);

        string filePath = Path.Combine(_testDir, "dir_test.bin");
        await File.WriteAllBytesAsync(filePath, new byte[] { 0xFF });

        var record = await service.QuarantineAsync(filePath, "Test", QuarantineSeverity.Low, "S");
        Directory.Exists(newDir).ShouldBeTrue();
    }

    [Fact]
    public async Task Quarantine_Record_Persisted_In_Database()
    {
        string filePath = Path.Combine(_testDir, "db_test.bin");
        await File.WriteAllBytesAsync(filePath, new byte[] { 0xDE, 0xAD });

        var record = await _service.QuarantineAsync(filePath, "DB persistence test",
            QuarantineSeverity.High, "DB_SERIAL");

        var fromDb = await _context.Set<QuarantinedFile>().FindAsync(record.Id);
        fromDb.ShouldNotBeNull();
        fromDb.QuarantineReason.ShouldBe("DB persistence test");
        fromDb.Severity.ShouldBe(QuarantineSeverity.High);
        fromDb.SourceUsbSerial.ShouldBe("DB_SERIAL");
        fromDb.IsEncrypted.ShouldBeTrue();
    }

    public void Dispose()
    {
        _context.Database.CloseConnection();
        _context.Dispose();
        try { if (Directory.Exists(_testDir)) Directory.Delete(_testDir, true); }
        catch { }
    }
}
