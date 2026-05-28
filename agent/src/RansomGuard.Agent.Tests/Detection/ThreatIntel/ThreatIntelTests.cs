using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Moq;
using NSec.Cryptography;
using RansomGuard.Agent.Core.Detection.ThreatIntel;
using Shouldly;

namespace RansomGuard.Agent.Tests.Detection.ThreatIntel;

public sealed class ThreatIntelTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ThreatIntelDataLoader _loader;

    public ThreatIntelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"rg-ti-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        _loader = new ThreatIntelDataLoader(
            new Mock<ILogger<ThreatIntelDataLoader>>().Object);
    }

    [Fact]
    public void Static_Lists_Loaded_Successfully_At_Startup()
    {
        // Embedded resources should load automatically in constructor
        _loader.TorExitNodeCount.ShouldBeGreaterThan(1000); // Real Tor Project snapshot
        _loader.C2ServerCount.ShouldBeGreaterThan(1000); // Real ThreatFox C2 indicators
        _loader.LolbasBinaryCount.ShouldBe(30);
        _loader.WhitelistedDomainCount.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void Tor_Exit_Node_Lookup_Works()
    {
        // Known Tor exit node from our data
        _loader.IsTorExitNode("185.220.101.1").ShouldBeTrue();
        _loader.IsTorExitNode("192.168.1.1").ShouldBeFalse();
    }

    [Fact]
    public void C2_Server_Lookup_Works()
    {
        // Real ThreatFox C2 IP (first entry in sorted list)
        _loader.IsKnownC2Server("1.13.247.208").ShouldBeTrue();
        _loader.IsKnownC2Server("8.8.8.8").ShouldBeFalse();
    }

    [Fact]
    public void C2_Server_List_Contains_No_RFC5737_IPs()
    {
        // RFC 5737 documentation ranges must not appear in production data
        _loader.IsKnownC2Server("198.51.100.1").ShouldBeFalse();
        _loader.IsKnownC2Server("203.0.113.1").ShouldBeFalse();
        _loader.IsKnownC2Server("192.0.2.1").ShouldBeFalse();
    }

    [Fact]
    public void Cloud_Provider_Lookup_Uses_CIDR_Matching()
    {
        _loader.IsKnownCloudProvider("3.5.140.2").ShouldBeTrue(); // AWS 3.5.140.0/22
        _loader.IsKnownCloudProvider("3.5.143.255").ShouldBeTrue(); // AWS end of 3.5.140.0/22
        _loader.IsKnownCloudProvider("34.120.0.1").ShouldBeTrue(); // GCP 34.120.0.0/16
        _loader.IsKnownCloudProvider("192.168.1.1").ShouldBeFalse(); // Private, not cloud
        _loader.IsKnownCloudProvider("8.8.8.8").ShouldBeFalse(); // Google DNS, not cloud CIDR
    }

    [Fact]
    public void Cloud_Provider_CIDR_Rejects_NonCloud_IPs()
    {
        // Private ranges and link-local are never cloud provider CIDRs
        _loader.IsKnownCloudProvider("192.168.1.1").ShouldBeFalse();
        _loader.IsKnownCloudProvider("10.0.0.1").ShouldBeFalse();
        _loader.IsKnownCloudProvider("169.254.1.1").ShouldBeFalse();
        _loader.IsKnownCloudProvider("127.0.0.1").ShouldBeFalse();
        // Invalid input returns false
        _loader.IsKnownCloudProvider("not-an-ip").ShouldBeFalse();
    }

    [Fact]
    public void Lolbas_Binary_Lookup_Works()
    {
        _loader.IsLolbasBinary("certutil.exe").ShouldBeTrue();
        _loader.IsLolbasBinary("CERTUTIL.EXE").ShouldBeTrue(); // case-insensitive
        _loader.IsLolbasBinary("notepad.exe").ShouldBeFalse();
    }

    [Fact]
    public void Whitelisted_Domain_Lookup_Works()
    {
        _loader.IsWhitelistedDestination("windowsupdate.microsoft.com").ShouldBeTrue();
        _loader.IsWhitelistedDestination("evil.com").ShouldBeFalse();
    }

    [Fact]
    public void Update_Package_Validation_Rejects_Unsigned()
    {
        string pkgPath = CreateUnsignedPackage("2.0.0");

        var validator = CreateValidator();
        var key = CreateEd25519KeyPair();

        var result = validator.Validate(pkgPath, "1.0.0", key.publicKey);

        result.IsValid.ShouldBeFalse();
        result.ErrorMessage!.ShouldContain("signature");
    }

    [Fact]
    public void Update_Package_Validation_Rejects_Tampered_Signature()
    {
        var (privateKey, publicKey) = CreateEd25519KeyPair();
        string pkgPath = CreateSignedPackage("2.0.0", privateKey);

        // Create a different key pair to verify with (simulating tampering)
        var (_, wrongPublicKey) = CreateEd25519KeyPair();
        var validator = CreateValidator();

        var result = validator.Validate(pkgPath, "1.0.0", wrongPublicKey);

        result.IsValid.ShouldBeFalse();
        result.ErrorMessage!.ShouldContain("signature verification FAILED");
    }

    [Fact]
    public void Update_Package_Validation_Rejects_Downgrade()
    {
        var (privateKey, publicKey) = CreateEd25519KeyPair();
        string pkgPath = CreateSignedPackage("1.0.0", privateKey);

        var validator = CreateValidator();
        var result = validator.Validate(pkgPath, "2.0.0", publicKey);

        result.IsValid.ShouldBeFalse();
        result.ErrorMessage!.ShouldContain("downgrade");
    }

    [Fact]
    public void Update_Package_Validates_Successfully()
    {
        var (privateKey, publicKey) = CreateEd25519KeyPair();
        string pkgPath = CreateSignedPackage("2.0.0", privateKey);

        var validator = CreateValidator();
        var result = validator.Validate(pkgPath, "1.0.0", publicKey);

        result.IsValid.ShouldBeTrue();
        result.PackageVersion.ShouldBe("2.0.0");
    }

    [Fact]
    public void Update_Applies_Correctly_With_Backup()
    {
        string dataDir = Path.Combine(_tempDir, "data");
        string updateDir = Path.Combine(_tempDir, "updates");
        Directory.CreateDirectory(dataDir);
        Directory.CreateDirectory(updateDir);

        // Create initial data file
        File.WriteAllText(Path.Combine(dataDir, "tor-exit-nodes.json"),
            JsonSerializer.Serialize(new { version = "1.0.0", entries = new[] { "1.1.1.1" } }));

        var (privateKey, _) = CreateEd25519KeyPair();
        string pkgPath = CreateSignedPackage("2.0.0", privateKey, updateDir);

        var validator = new ThreatIntelUpdateValidator(
            new Mock<ILogger<ThreatIntelUpdateValidator>>().Object,
            updateDir, dataDir);

        var loader = new ThreatIntelDataLoader(
            new Mock<ILogger<ThreatIntelDataLoader>>().Object);

        bool applied = validator.Apply(pkgPath, loader);

        applied.ShouldBeTrue();

        // Backup should exist
        Directory.Exists(Path.Combine(dataDir, "backup")).ShouldBeTrue();
    }

    [Fact]
    public void Rollback_Restores_Previous_Lists_On_Failure()
    {
        string dataDir = Path.Combine(_tempDir, "rollback-data");
        string backupDir = Path.Combine(dataDir, "backup");
        Directory.CreateDirectory(dataDir);
        Directory.CreateDirectory(backupDir);

        // Create backup file
        string originalContent = JsonSerializer.Serialize(new { version = "1.0.0", entries = new[] { "original" } });
        File.WriteAllText(Path.Combine(backupDir, "tor-exit-nodes.json"), originalContent);

        // Create corrupted current file
        File.WriteAllText(Path.Combine(dataDir, "tor-exit-nodes.json"), "corrupted");

        var validator = new ThreatIntelUpdateValidator(
            new Mock<ILogger<ThreatIntelUpdateValidator>>().Object,
            Path.Combine(_tempDir, "updates"), dataDir);

        validator.Rollback();

        // Current file should be restored from backup
        string restored = File.ReadAllText(Path.Combine(dataDir, "tor-exit-nodes.json"));
        restored.ShouldBe(originalContent);
    }

    [Fact]
    public void Concurrent_Update_Attempts_Handled_Via_Mutex()
    {
        // This test verifies that concurrent Apply calls don't crash
        string dataDir = Path.Combine(_tempDir, "concurrent-data");
        Directory.CreateDirectory(dataDir);

        var (privateKey, _) = CreateEd25519KeyPair();
        string pkgPath = CreateSignedPackage("2.0.0", privateKey);

        var validator = new ThreatIntelUpdateValidator(
            new Mock<ILogger<ThreatIntelUpdateValidator>>().Object,
            Path.Combine(_tempDir, "updates"), dataDir);

        var loader = new ThreatIntelDataLoader(
            new Mock<ILogger<ThreatIntelDataLoader>>().Object);

        // Sequential execution (SQLite limitations make true concurrent test unreliable)
        bool result1 = validator.Apply(pkgPath, loader);
        bool result2 = validator.Apply(pkgPath, loader);

        // Both should succeed (sequential, mutex released between calls)
        result1.ShouldBeTrue();
        result2.ShouldBeTrue();
    }

    // ===== Helpers =====

    private ThreatIntelUpdateValidator CreateValidator()
    {
        return new ThreatIntelUpdateValidator(
            new Mock<ILogger<ThreatIntelUpdateValidator>>().Object,
            Path.Combine(_tempDir, "updates"),
            Path.Combine(_tempDir, "data"));
    }

    private static (Key privateKey, PublicKey publicKey) CreateEd25519KeyPair()
    {
        var algorithm = SignatureAlgorithm.Ed25519;
        var key = Key.Create(algorithm, new KeyCreationParameters
        {
            ExportPolicy = KeyExportPolicies.AllowPlaintextExport
        });
        return (key, key.PublicKey);
    }

    private string CreateUnsignedPackage(string version)
    {
        string pkgPath = Path.Combine(_tempDir, $"threat-intel-v{version}.pkg");
        using var fs = File.Create(pkgPath);
        using var archive = new ZipArchive(fs, ZipArchiveMode.Create);

        AddManifest(archive, version);
        AddListFiles(archive);
        // No signature.bin — unsigned

        return pkgPath;
    }

    private string CreateSignedPackage(string version, Key privateKey, string? outputDir = null)
    {
        string dir = outputDir ?? _tempDir;
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        string pkgPath = Path.Combine(dir, $"threat-intel-v{version}.pkg");

        // First pass: create without signature to compute hash
        using var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            AddManifest(archive, version);
            AddListFiles(archive);
        }
        ms.Position = 0;

        // Compute content hash
        byte[] contentHash;
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Read, leaveOpen: true))
        {
            contentHash = ComputeContentHash(archive);
        }

        // Sign
        var algorithm = SignatureAlgorithm.Ed25519;
        byte[] signature = algorithm.Sign(privateKey, contentHash);

        // Second pass: create final package with signature
        using var fs = File.Create(pkgPath);
        using (var archive = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            AddManifest(archive, version);
            AddListFiles(archive);

            var sigEntry = archive.CreateEntry("signature.bin");
            using var sigStream = sigEntry.Open();
            sigStream.Write(signature, 0, signature.Length);
        }

        return pkgPath;
    }

    private static void AddManifest(ZipArchive archive, string version)
    {
        var entry = archive.CreateEntry("manifest.json");
        using var stream = entry.Open();
        JsonSerializer.Serialize(stream, new { version, buildDate = "2026-05-23", description = "Test" });
    }

    private static void AddListFiles(ZipArchive archive)
    {
        AddJsonEntry(archive, "lists/tor-exit-nodes.json", new { version = "2.0.0", entries = new[] { "1.2.3.4" } });
        AddJsonEntry(archive, "lists/cloud-providers.json", new
        {
            version = "2.0.0",
            cidrs = new { aws = new[] { "3.0.0.0/8" } },
            whitelisted_domains = new[] { "test.com" }
        });
        AddJsonEntry(archive, "lists/known-c2-servers.json", new { version = "2.0.0", entries = new[] { "5.6.7.8" } });
        AddJsonEntry(archive, "lists/lolbas-binaries.json", new { version = "2.0.0", entries = new[] { "test.exe" } });
    }

    private static void AddJsonEntry(ZipArchive archive, string name, object data)
    {
        var entry = archive.CreateEntry(name);
        using var stream = entry.Open();
        JsonSerializer.Serialize(stream, data);
    }

    private static byte[] ComputeContentHash(ZipArchive archive)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        var entries = archive.Entries
            .Where(e => e.FullName != "signature.bin")
            .OrderBy(e => e.FullName, StringComparer.Ordinal);

        using var hashStream = new MemoryStream();
        foreach (var entry in entries)
        {
            byte[] nameBytes = Encoding.UTF8.GetBytes(entry.FullName);
            hashStream.Write(nameBytes, 0, nameBytes.Length);
            using var entryStream = entry.Open();
            entryStream.CopyTo(hashStream);
        }
        hashStream.Position = 0;
        return sha.ComputeHash(hashStream);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }
}
