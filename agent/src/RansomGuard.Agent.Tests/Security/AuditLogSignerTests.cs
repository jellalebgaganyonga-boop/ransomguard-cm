using System.Runtime.Versioning;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using RansomGuard.Agent.Core.Persistence;
using RansomGuard.Agent.Core.Persistence.Repositories;
using RansomGuard.Agent.Core.Security.Cryptography;
using Shouldly;

namespace RansomGuard.Agent.Tests.Security;

/// <summary>
/// Tests for Ed25519 audit log signing and verification.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class AuditLogSignerTests : IDisposable
{
    private readonly AuditLogSigner _signer;
    private readonly string _keyDir;

    public AuditLogSignerTests()
    {
        _keyDir = Path.Combine(Path.GetTempPath(), $"ransomguard_signer_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_keyDir);
        _signer = new AuditLogSigner(_keyDir, new Mock<ILogger<AuditLogSigner>>().Object);
    }

    [Fact]
    public void Sign_and_verify_round_trip_should_succeed()
    {
        Guid id = Guid.NewGuid();
        DateTime ts = DateTime.UtcNow;
        string action = "TestAction";
        string? prevHash = null;
        string currHash = "abc123def456abc123def456abc123def456abc123def456abc123def456abcd";

        string signature = _signer.Sign(id, ts, action, prevHash, currHash);
        signature.ShouldNotBeNullOrEmpty();

        bool valid = _signer.Verify(id, ts, action, prevHash, currHash, signature);
        valid.ShouldBeTrue();
    }

    [Fact]
    public void Verify_should_detect_tampered_timestamp()
    {
        Guid id = Guid.NewGuid();
        DateTime ts = DateTime.UtcNow;
        string action = "TestAction";
        string currHash = "abc123";

        string signature = _signer.Sign(id, ts, action, null, currHash);

        bool valid = _signer.Verify(id, ts.AddSeconds(1), action, null, currHash, signature);
        valid.ShouldBeFalse();
    }

    [Fact]
    public void Verify_should_detect_tampered_action()
    {
        Guid id = Guid.NewGuid();
        DateTime ts = DateTime.UtcNow;
        string currHash = "abc123";

        string signature = _signer.Sign(id, ts, "OriginalAction", null, currHash);

        bool valid = _signer.Verify(id, ts, "TamperedAction", null, currHash, signature);
        valid.ShouldBeFalse();
    }

    [Fact]
    public void Verify_should_detect_tampered_hash()
    {
        Guid id = Guid.NewGuid();
        DateTime ts = DateTime.UtcNow;

        string signature = _signer.Sign(id, ts, "Action", null, "original_hash");

        bool valid = _signer.Verify(id, ts, "Action", null, "tampered_hash", signature);
        valid.ShouldBeFalse();
    }

    [Fact]
    public void Verify_should_reject_invalid_base64_signature()
    {
        Guid id = Guid.NewGuid();
        DateTime ts = DateTime.UtcNow;

        bool valid = _signer.Verify(id, ts, "Action", null, "hash", "not-valid-base64!!!");
        valid.ShouldBeFalse();
    }

    [Fact]
    public void Key_persistence_should_survive_reload()
    {
        Guid id = Guid.NewGuid();
        DateTime ts = DateTime.UtcNow;
        string signature = _signer.Sign(id, ts, "Action", null, "hash");

        // Create new signer instance from same key directory
        var signer2 = new AuditLogSigner(_keyDir, new Mock<ILogger<AuditLogSigner>>().Object);

        bool valid = signer2.Verify(id, ts, "Action", null, "hash", signature);
        valid.ShouldBeTrue();
    }

    [Fact]
    public void Public_key_should_be_exported()
    {
        string pubKeyPath = Path.Combine(_keyDir, "audit.pub");
        File.Exists(pubKeyPath).ShouldBeTrue();

        string pubKey = _signer.GetPublicKeyBase64();
        pubKey.ShouldNotBeNullOrEmpty();
        pubKey.Length.ShouldBeGreaterThan(20); // Ed25519 public key is 32 bytes = 44 chars base64
    }

    [Fact]
    public async Task Signed_audit_log_chain_should_verify()
    {
        var options = new DbContextOptionsBuilder<AgentDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;

        using var context = new AgentDbContext(options);
        context.Database.OpenConnection();
        context.Database.EnsureCreated();

        var repo = new AuditLogRepository(context, _signer);

        for (int i = 0; i < 50; i++)
        {
            await repo.AppendAsync($"Action{i}", $"Details for entry {i}");
        }

        bool valid = await repo.VerifyChainIntegrityAsync();
        valid.ShouldBeTrue();
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_keyDir)) Directory.Delete(_keyDir, true); }
        catch { /* best-effort */ }
    }
}
