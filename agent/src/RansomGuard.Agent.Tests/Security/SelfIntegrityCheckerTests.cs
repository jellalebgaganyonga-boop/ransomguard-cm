using RansomGuard.Agent.Core.Security;
using Shouldly;

namespace RansomGuard.Agent.Tests.Security;

/// <summary>
/// Tests for <see cref="SelfIntegrityChecker"/> binary integrity verification.
/// </summary>
public sealed class SelfIntegrityCheckerTests : IDisposable
{
    private readonly string _testDir;

    public SelfIntegrityCheckerTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"ransomguard_integrity_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
    }

    [Fact]
    public void Verify_should_skip_when_no_hash_file_exists()
    {
        // In test context, there's no .sha256 file next to the test assembly
        IntegrityResult result = SelfIntegrityChecker.Verify();

        result.Status.ShouldBe(IntegrityStatus.Skipped);
    }

    [Fact]
    public void GenerateHashFile_should_create_companion_file()
    {
        string testFile = Path.Combine(_testDir, "test.exe");
        File.WriteAllText(testFile, "test binary content");

        SelfIntegrityChecker.GenerateHashFile(testFile);

        File.Exists(testFile + ".sha256").ShouldBeTrue();
        string hash = File.ReadAllText(testFile + ".sha256");
        hash.Length.ShouldBe(64); // SHA-256 hex length
    }

    [Fact]
    public void GenerateHashFile_should_produce_deterministic_hash()
    {
        string testFile = Path.Combine(_testDir, "deterministic.exe");
        File.WriteAllText(testFile, "identical content");

        SelfIntegrityChecker.GenerateHashFile(testFile);
        string hash1 = File.ReadAllText(testFile + ".sha256");

        // Regenerate
        SelfIntegrityChecker.GenerateHashFile(testFile);
        string hash2 = File.ReadAllText(testFile + ".sha256");

        hash1.ShouldBe(hash2);
    }

    [Fact]
    public void Modified_file_should_produce_different_hash()
    {
        string testFile = Path.Combine(_testDir, "modified.exe");
        File.WriteAllText(testFile, "original content");
        SelfIntegrityChecker.GenerateHashFile(testFile);
        string originalHash = File.ReadAllText(testFile + ".sha256");

        File.WriteAllText(testFile, "tampered content");
        SelfIntegrityChecker.GenerateHashFile(testFile);
        string tamperedHash = File.ReadAllText(testFile + ".sha256");

        originalHash.ShouldNotBe(tamperedHash);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_testDir)) Directory.Delete(_testDir, true); }
        catch { /* best-effort */ }
    }
}
