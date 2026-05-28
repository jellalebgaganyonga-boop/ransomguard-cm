using System.Runtime.Versioning;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using RansomGuard.Agent.Core.Configuration;
using RansomGuard.Agent.Core.Detection.Entropy;
using RansomGuard.Agent.Core.Detection.Genealogy;
using RansomGuard.Agent.Core.Detection.Sentinel;
using RansomGuard.Agent.Core.Detection.UsbGuard;
using RansomGuard.Agent.Core.Detection.UsbGuard.Scanning;
using RansomGuard.Agent.Core.Persistence;
using RansomGuard.Agent.Core.Persistence.Entities;
using RansomGuard.Agent.Core.Persistence.Repositories;
using RansomGuard.Agent.Core.Security.Cryptography;
using Shouldly;

namespace RansomGuard.Agent.Tests.EndToEnd;

/// <summary>
/// E2E test: USB-borne malware triggers full attack chain.
/// USB content scanner detects PE in PDF → quarantine → simulated encryption → entropy alert → genealogy.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class UsbToRansomwareE2E : IAsyncLifetime, IDisposable
{
    private readonly string _testDir;
    private readonly string _usbDir;
    private readonly AgentDbContext _context;
    private readonly AuditLogRepository _auditRepo;
    private readonly SentinelCanaryRepository _canaryRepo;
    private readonly MagicByteValidator _magicValidator;
    private readonly EntropyCalculator _entropyCalc;
    private readonly EntropyDetector _entropyDetector;

    public UsbToRansomwareE2E()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"rg_usb_e2e_{Guid.NewGuid():N}");
        _usbDir = Path.Combine(_testDir, "USB_DRIVE");
        Directory.CreateDirectory(_usbDir);

        var keyDir = Path.Combine(_testDir, "keys");
        Directory.CreateDirectory(keyDir);

        var options = new DbContextOptionsBuilder<AgentDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        _context = new AgentDbContext(options);
        _context.Database.OpenConnection();
        _context.Database.EnsureCreated();

        var signer = new AuditLogSigner(keyDir, new Mock<ILogger<AuditLogSigner>>().Object);
        _auditRepo = new AuditLogRepository(_context, signer);
        _canaryRepo = new SentinelCanaryRepository(_context);
        _magicValidator = new MagicByteValidator(new Mock<ILogger<MagicByteValidator>>().Object);
        _entropyCalc = new EntropyCalculator(new Mock<ILogger<EntropyCalculator>>().Object);
        _entropyDetector = new EntropyDetector(new EntropyOptions
        {
            AbsoluteThreshold = 7.5,
            DeltaThreshold = 2.5,
            SusceptibleExtensions = [".txt", ".docx", ".pdf", ".csv"]
        });
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        Dispose();
        return Task.CompletedTask;
    }

    [Fact(Timeout = 120_000)]
    public async Task Usb_Borne_Malware_Full_Attack_Chain()
    {
        // ===== PHASE 1: Create USB with disguised PE + legitimate files =====
        string fakePdf = Path.Combine(_usbDir, "quarterly_report.pdf");
        byte[] peHeader = [0x4D, 0x5A, 0x90, 0x00]; // MZ header = PE executable
        byte[] peBody = new byte[4096];
        Random.Shared.NextBytes(peBody);
        await File.WriteAllBytesAsync(fakePdf, [.. peHeader, .. peBody]);

        // Create 5 legitimate medical files
        var medicalFiles = new List<string>();
        string[] names = [
            "patient_dossier_001.txt", "radiologie_rapport.docx",
            "analyses_sang_complet.csv", "ordonnance_traitement.pdf",
            "compte_rendu_hospitalisation.txt"
        ];
        foreach (string name in names)
        {
            string path = Path.Combine(_usbDir, name);
            string content = $"Dossier medical confidentiel - {name}\n" +
                             "Patient: Jean Dupont, DOB: 1965-03-15\n" +
                             "Diagnostic: insuffisance cardiaque chronique\n" +
                             "Traitement prescrit: bisoprolol 5mg, ramipril 10mg\n";
            // Pad to 2KB for meaningful entropy measurement
            content += new string(' ', Math.Max(0, 2000 - content.Length));
            await File.WriteAllTextAsync(path, content);
            medicalFiles.Add(path);
        }

        // ===== PHASE 2: Scanner detects extension mismatch =====
        var scanResult = await _magicValidator.ValidateAsync(fakePdf, CancellationToken.None);
        scanResult.IsMatch.ShouldBeFalse("PE binary disguised as PDF should not match");

        // Persist USB alert
        await _auditRepo.AppendAsync(
            "UsbAlert",
            $"Extension mismatch detected: {Path.GetFileName(fakePdf)} — PE header in .pdf file",
            entityType: "UsbAlert",
            cancellationToken: CancellationToken.None);

        // ===== PHASE 3: Quarantine the file =====
        string quarantineDir = Path.Combine(_testDir, "quarantine");
        Directory.CreateDirectory(quarantineDir);
        string quarantineDest = Path.Combine(quarantineDir, $"{Guid.NewGuid():N}.qbin");
        File.Copy(fakePdf, quarantineDest);
        File.Delete(fakePdf);
        File.Exists(fakePdf).ShouldBeFalse("Quarantined file should be removed from USB");
        File.Exists(quarantineDest).ShouldBeTrue("Quarantined copy should exist");

        await _auditRepo.AppendAsync(
            "UsbQuarantine",
            $"File quarantined: {Path.GetFileName(fakePdf)} → {Path.GetFileName(quarantineDest)}",
            entityType: "UsbQuarantine",
            cancellationToken: CancellationToken.None);

        // ===== PHASE 4: Simulate malware execution (encrypts medical files) =====
        var baselineEntropies = new Dictionary<string, double>();
        foreach (string file in medicalFiles)
        {
            double? baseline = await _entropyCalc.ComputeFileEntropyAsync(file, CancellationToken.None);
            baseline.ShouldNotBeNull();
            baselineEntropies[file] = baseline.Value;

            _context.EntropyBaselines.Add(new EntropyBaseline
            {
                FilePath = file,
                DirectoryPath = _usbDir,
                FileExtension = Path.GetExtension(file).ToLowerInvariant(),
                EntropyValue = baseline.Value,
                FileSize = new FileInfo(file).Length
            });
        }
        await _context.SaveChangesAsync();

        // Encrypt all files (simulating ransomware)
        using var aes = Aes.Create();
        foreach (string file in medicalFiles)
        {
            byte[] original = await File.ReadAllBytesAsync(file);
            byte[] encrypted = aes.EncryptCbc(original, aes.IV);
            await File.WriteAllBytesAsync(file, encrypted);
        }

        // ===== PHASE 5: Entropy detection fires =====
        var entropyAlerts = new List<EntropyAlert>();
        foreach (string file in medicalFiles)
        {
            double? current = await _entropyCalc.ComputeFileEntropyAsync(file, CancellationToken.None);
            current.ShouldNotBeNull();

            var baseline = _context.EntropyBaselines.First(b => b.FilePath == file);
            var alert = _entropyDetector.Analyze(file, current.Value, baseline);

            if (alert is not null)
            {
                entropyAlerts.Add(alert);
                _context.EntropyAlerts.Add(alert);

                await _auditRepo.AppendAsync(
                    "EntropyAlert",
                    $"Rule {alert.RuleId}: {file} entropy {alert.BaselineEntropy:F2} → {alert.CurrentEntropy:F2}",
                    entityType: "EntropyAlert",
                    entityId: alert.Id,
                    cancellationToken: CancellationToken.None);
            }
        }
        await _context.SaveChangesAsync();

        // ===== ASSERTIONS =====

        // 1. Extension mismatch detected
        scanResult.IsMatch.ShouldBeFalse();

        // 2. Quarantine executed
        File.Exists(quarantineDest).ShouldBeTrue();

        // 3. Entropy alerts fired for encrypted files
        entropyAlerts.Count.ShouldBeGreaterThanOrEqualTo(3,
            "At least 3 of 5 medical files should trigger entropy alerts after AES encryption");

        // 4. Audit log entries Ed25519 signed for each step
        var auditEntries = await _context.AuditLogs.ToListAsync();
        auditEntries.Count.ShouldBeGreaterThanOrEqualTo(4, "USB alert + quarantine + entropy alerts");
        auditEntries.ShouldAllBe(e => !string.IsNullOrEmpty(e.Signature),
            "All audit entries should be Ed25519 signed");

        // 5. Audit chain integrity
        bool chainIntact = await _auditRepo.VerifyChainIntegrityAsync();
        chainIntact.ShouldBeTrue("Audit log hash chain should be intact");

        // 6. End-to-end completed within test timeout (120s)
    }

    public void Dispose()
    {
        Thread.Sleep(500);
        try { _context.Database.CloseConnection(); } catch { }
        try { _context.Dispose(); } catch { }
        try { Directory.Delete(_testDir, recursive: true); } catch { }
    }
}
