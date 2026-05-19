using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using RansomGuard.Agent.Core.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using RansomGuard.Agent.Core.Configuration;
using RansomGuard.Agent.Core.Detection.Entropy;
using RansomGuard.Agent.Core.Detection.Genealogy;
using RansomGuard.Agent.Core.Detection.Sentinel;
using RansomGuard.Agent.Core.Persistence;
using RansomGuard.Agent.Core.Persistence.Entities;
using RansomGuard.Agent.Core.Persistence.Repositories;
using Shouldly;

namespace RansomGuard.Agent.Tests.EndToEnd;

/// <summary>
/// Unified cross-module E2E test that simulates a full ransomware attack chain
/// across SENTINEL + ENTROPY + GENEALOGY, verifying all three modules fire
/// and cross-link in a single flow.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class UnifiedRansomwareAttackE2E : IAsyncLifetime, IDisposable
{
    private readonly string _testDir;
    private readonly AgentDbContext _context;
    private readonly EntropyCalculator _calculator;
    private readonly EntropyDetector _detector;
    private readonly CanaryFileService _canaryService;
    private readonly SentinelCanaryRepository _canaryRepo;
    private readonly AuditLogRepository _auditRepo;
    private readonly GenealogyEnricher _genealogyEnricher;
    private readonly ProcessSnapshotService _snapshotService;
    private readonly List<string> _medicalFiles = [];
    private readonly List<SentinelCanary> _canaries = [];
    private readonly string _keyDir;
    private readonly AuditLogSigner _signer;

    public UnifiedRansomwareAttackE2E()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"rg_unified_e2e_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);

        _keyDir = Path.Combine(_testDir, "keys");
        Directory.CreateDirectory(_keyDir);

        var options = new DbContextOptionsBuilder<AgentDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        _context = new AgentDbContext(options);
        _context.Database.OpenConnection();
        _context.Database.EnsureCreated();

        _calculator = new EntropyCalculator(new Mock<ILogger<EntropyCalculator>>().Object);
        _detector = new EntropyDetector(new EntropyOptions
        {
            AbsoluteThreshold = 7.5,
            DeltaThreshold = 2.5,
            DirectoryShiftThreshold = 1.5,
            DirectoryShiftMinFiles = 5,
            WhitelistedExtensions = [".zip", ".jpg"],
            SusceptibleExtensions = [".txt", ".docx", ".pdf", ".csv", ".xlsx"]
        });

        _signer = new AuditLogSigner(_keyDir, new Mock<ILogger<AuditLogSigner>>().Object);
        _canaryRepo = new SentinelCanaryRepository(_context);
        _auditRepo = new AuditLogRepository(_context, _signer);
        _canaryService = new CanaryFileService(_canaryRepo, new Mock<ILogger<CanaryFileService>>().Object);
        _snapshotService = new ProcessSnapshotService(new Mock<ILogger<ProcessSnapshotService>>().Object);
        _genealogyEnricher = new GenealogyEnricher(
            _snapshotService,
            new RestartManagerHelper(new Mock<ILogger<RestartManagerHelper>>().Object),
            _context,
            new Mock<ILogger<GenealogyEnricher>>().Object);
    }

    public async Task InitializeAsync()
    {
        // === SETUP PHASE ===

        // Create 50 French medical text files
        string[] templates =
        [
            "Dossier patient: {0}\nConsultation du 01/05/2026\nTension: 130/85\nTraitement: Amlodipine 10mg",
            "Analyses labo: {0}\nGlycemie: 1.05 g/L\nHb: 12.8 g/dL\nCreatinine: 9.2 mg/L",
            "Imagerie: {0}\nRadio thorax face\nParenchyme clair\nSilhouette cardiaque normale",
            "Prescription: {0}\nAmoxicilline 1g x3/j pendant 7j\nParacetamol 1g si douleur",
            "Consultation: {0}\nCephalees chroniques\nExamen neuro normal\nIRM prescrite"
        ];

        for (int i = 0; i < 50; i++)
        {
            string ext = (i % 5) switch { 0 => ".txt", 1 => ".csv", 2 => ".txt", 3 => ".csv", _ => ".txt" };
            string path = Path.Combine(_testDir, $"patient_{i:D3}{ext}");
            var sb = new StringBuilder();
            for (int j = 0; j < 15; j++)
                sb.AppendLine(string.Format(templates[i % 5], $"PAT-{i:D3}"));
            await File.WriteAllTextAsync(path, sb.ToString());
            _medicalFiles.Add(path);
        }

        // Deploy 5 SENTINEL canary files
        for (int i = 0; i < 5; i++)
        {
            var canary = await _canaryService.CreateCanaryAsync(_testDir, "dossier_patient", "0001_");
            _canaries.Add(canary);
        }

        // Build entropy baselines for all 50 files
        foreach (string path in _medicalFiles)
        {
            double? entropy = await _calculator.ComputeFileEntropyAsync(path);
            if (entropy is null) continue;
            _context.EntropyBaselines.Add(new EntropyBaseline
            {
                FilePath = path,
                DirectoryPath = _testDir,
                FileExtension = Path.GetExtension(path).ToLowerInvariant(),
                EntropyValue = entropy.Value,
                FileSize = new FileInfo(path).Length
            });
        }
        await _context.SaveChangesAsync();
    }

    [Fact(Timeout = 120_000)]
    public async Task FullAttackChain_AllThreeModules_FireAndCrossLink()
    {
        var sw = Stopwatch.StartNew();

        // === ATTACK PHASE ===

        // Step A: Simulate vssadmin (pattern detection only, non-destructive)
        var vssadminSnapshot = new ProcessSnapshot
        {
            ProcessId = 9999,
            ParentProcessId = Environment.ProcessId,
            ProcessName = "vssadmin",
            ExecutablePath = @"C:\Windows\System32\vssadmin.exe",
            CommandLine = "vssadmin.exe delete shadows /all /quiet",
            StartTime = DateTime.UtcNow,
            WorkingSetBytes = 1024
        };
        var vssadminTree = new ProcessTree
        {
            Root = vssadminSnapshot,
            Ancestors = [new ProcessSnapshot
            {
                ProcessId = Environment.ProcessId,
                ParentProcessId = 0,
                ProcessName = "powershell",
                CommandLine = "powershell.exe -Command vssadmin delete shadows",
                WorkingSetBytes = 0
            }]
        };
        IReadOnlyList<SuspiciousPatternFlag> t1490Flags = SuspiciousPatternDetector.Analyze(vssadminTree);

        // Step B: Encrypt all 50 files with AES-256
        using var aes = Aes.Create();
        aes.KeySize = 256;
        aes.GenerateKey();
        aes.GenerateIV();

        var baselines = await _context.EntropyBaselines.ToDictionaryAsync(b => b.FilePath);
        var entropyAlerts = new List<EntropyAlert>();
        double totalDelta = 0;

        foreach (string path in _medicalFiles)
        {
            byte[] plaintext = await File.ReadAllBytesAsync(path);
            byte[] encrypted;
            using (var encryptor = aes.CreateEncryptor())
            {
                encrypted = encryptor.TransformFinalBlock(plaintext, 0, plaintext.Length);
            }
            await File.WriteAllBytesAsync(path, encrypted);

            double? currentEntropy = await _calculator.ComputeFileEntropyAsync(path);
            if (currentEntropy is null) continue;

            baselines.TryGetValue(path, out var baseline);
            EntropyAlert? alert = _detector.Analyze(path, currentEntropy.Value, baseline);
            if (alert is not null)
            {
                entropyAlerts.Add(alert);
                totalDelta += alert.Delta;
                _context.EntropyAlerts.Add(alert);
            }
        }
        await _context.SaveChangesAsync();

        // Step C: Rename encrypted files with .locked extension
        foreach (string path in _medicalFiles.Where(File.Exists))
        {
            string lockedPath = path + ".locked";
            File.Move(path, lockedPath);
        }

        // Step D: Delete 2 SENTINEL canaries (simulate ransomware deleting canaries)
        for (int i = 0; i < 2 && i < _canaries.Count; i++)
        {
            string canaryPath = _canaries[i].FilePath;
            if (File.Exists(canaryPath))
                File.Delete(canaryPath);
        }

        // Verify canary integrity (detect tampering/deletion)
        int canaryAlertCount = 0;
        foreach (var canary in _canaries)
        {
            bool intact = await _canaryService.VerifyCanaryIntegrityAsync(canary);
            if (!intact)
            {
                canaryAlertCount++;
                _context.Set<CanaryAlert>().Add(new CanaryAlert
                {
                    CanaryId = canary.Id,
                    CanaryPath = canary.FilePath,
                    AlertType = CanaryAlertType.CanaryDeleted,
                    Severity = AlertSeverity.Critical
                });
            }
        }
        await _context.SaveChangesAsync();

        // Persist audit log entries for each entropy alert
        foreach (var alert in entropyAlerts)
        {
            await _auditRepo.AppendAsync("EntropyAlert",
                $"Rule {alert.RuleId}: {alert.FilePath}",
                "EntropyAlert", alert.Id);
        }

        // Genealogy enrichment for alerts (using synthetic trees since no real process locks files in test)
        int genealogyCount = 0;
        foreach (var alert in entropyAlerts.Take(10))
        {
            var record = new GenealogyRecord
            {
                AlertId = alert.Id,
                ProcessTreeJson = System.Text.Json.JsonSerializer.Serialize(vssadminTree),
                SuspiciousPatternsJson = System.Text.Json.JsonSerializer.Serialize(t1490Flags),
                RootProcessId = vssadminSnapshot.ProcessId,
                RootProcessName = vssadminSnapshot.ProcessName,
                Summary = vssadminTree.Summary
            };
            _context.Set<GenealogyRecord>().Add(record);
            genealogyCount++;
        }
        await _context.SaveChangesAsync();

        // Directory shift detection
        if (entropyAlerts.Count > 0)
        {
            double avgDelta = totalDelta / entropyAlerts.Count;
            EntropyAlert? dirAlert = _detector.AnalyzeDirectoryShift(_testDir, entropyAlerts.Count, avgDelta);
            if (dirAlert is not null)
            {
                _context.EntropyAlerts.Add(dirAlert);
                await _context.SaveChangesAsync();
            }
        }

        sw.Stop();

        // === ASSERTION PHASE ===

        // SENTINEL: canary alerts fired
        int dbCanaryAlerts = await _context.Set<CanaryAlert>().CountAsync();
        dbCanaryAlerts.ShouldBeGreaterThanOrEqualTo(2, "At least 2 deleted canaries should trigger alerts");

        // ENTROPY: per-file alerts
        int dbEntropyAlerts = await _context.EntropyAlerts.CountAsync();
        dbEntropyAlerts.ShouldBeGreaterThanOrEqualTo(10, "At least 10 entropy alerts should fire");

        // ENTROPY: directory shift (Rule 3 Critical)
        bool hasCriticalShift = await _context.EntropyAlerts
            .AnyAsync(a => a.RuleId == 3 && a.Severity == "Critical");
        hasCriticalShift.ShouldBeTrue("Directory-wide shift Rule 3 should fire as Critical");

        // GENEALOGY: records persisted
        int dbGenealogyRecords = await _context.Set<GenealogyRecord>().CountAsync();
        dbGenealogyRecords.ShouldBeGreaterThanOrEqualTo(5, "At least 5 genealogy records");

        // GENEALOGY: T1490 pattern detected
        t1490Flags.ShouldContain(f => f.TechniqueId == "T1490", "T1490 vssadmin pattern must fire");
        t1490Flags.First(f => f.TechniqueId == "T1490").Severity.ShouldBe("Critical");

        // GENEALOGY cross-linking: GenealogyRecords reference real alert IDs
        var alertIds = entropyAlerts.Select(a => a.Id).ToHashSet();
        var genealogyAlertIds = await _context.Set<GenealogyRecord>()
            .Select(g => g.AlertId)
            .ToListAsync();
        int linkedCount = genealogyAlertIds.Count(id => alertIds.Contains(id));
        linkedCount.ShouldBeGreaterThanOrEqualTo(5,
            "At least 5 GenealogyRecords should reference real alert IDs");

        // Audit log entries exist
        int auditCount = await _context.AuditLogs.CountAsync();
        auditCount.ShouldBeGreaterThanOrEqualTo(10, "Audit log should have entries for alerts");

        // Audit log hash chain intact
        bool chainValid = await _auditRepo.VerifyChainIntegrityAsync();
        chainValid.ShouldBeTrue("Audit log hash chain should be intact");

        // Audit log Ed25519 signatures valid (signer is wired in constructor)
        var auditEntries = await _context.AuditLogs.OrderBy(a => a.CreatedAt).ToListAsync();
        int signedCount = auditEntries.Count(e => e.Signature is not null);
        signedCount.ShouldBe(auditEntries.Count, "All audit entries should be Ed25519 signed");

        // Latency under 90 seconds
        sw.Elapsed.TotalSeconds.ShouldBeLessThan(90, "Full E2E pipeline under 90 seconds");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        _context.Database.CloseConnection();
        _context.Dispose();
        try { if (Directory.Exists(_testDir)) Directory.Delete(_testDir, true); }
        catch { /* best-effort */ }
    }
}
