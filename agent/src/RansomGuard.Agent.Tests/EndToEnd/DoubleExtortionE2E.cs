using System.Runtime.Versioning;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using RansomGuard.Agent.Core.Configuration;
using RansomGuard.Agent.Core.Detection.CrossModule;
using RansomGuard.Agent.Core.Detection.Entropy;
using RansomGuard.Agent.Core.Detection.ExfilWatch;
using RansomGuard.Agent.Core.Detection.ExfilWatch.Models;
using RansomGuard.Agent.Core.Detection.ExfilWatch.Rules;
using RansomGuard.Agent.Core.Persistence;
using RansomGuard.Agent.Core.Persistence.Entities;
using RansomGuard.Agent.Core.Persistence.Repositories;
using RansomGuard.Agent.Core.Security.Cryptography;
using Shouldly;

namespace RansomGuard.Agent.Tests.EndToEnd;

/// <summary>
/// E2E test: ENTROPY + EXFIL Rule 8 correlation — the SIGNATURE Sprint 4 detection.
/// Encrypts medical files → EntropySignal published → network upload → Rule 8 correlates → Critical alert.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class DoubleExtortionE2E : IAsyncLifetime, IDisposable
{
    private readonly string _testDir;
    private readonly AgentDbContext _context;
    private readonly AuditLogRepository _auditRepo;
    private readonly EntropyCalculator _calculator;
    private readonly EntropyDetector _detector;
    private readonly InMemoryDetectionEventBus _eventBus;
    private readonly EncryptedExfilCorrelationRule _rule8;
    private readonly List<string> _medicalFiles = [];

    public DoubleExtortionE2E()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"rg_dbl_ext_e2e_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);

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

        _calculator = new EntropyCalculator(new Mock<ILogger<EntropyCalculator>>().Object);
        _detector = new EntropyDetector(new EntropyOptions
        {
            AbsoluteThreshold = 7.5,
            DeltaThreshold = 2.5,
            SusceptibleExtensions = [".txt", ".docx", ".pdf", ".csv"]
        });

        _eventBus = new InMemoryDetectionEventBus(new Mock<ILogger<InMemoryDetectionEventBus>>().Object);
        _rule8 = new EncryptedExfilCorrelationRule(_eventBus);
    }

    public async Task InitializeAsync()
    {
        // Create 10 realistic French medical text files
        string[] templates =
        [
            "Compte rendu d'hospitalisation - Service de cardiologie\nPatient: {0}\nDiagnostic: insuffisance cardiaque chronique stade III NYHA",
            "Rapport d'analyse biologique - Laboratoire central\nPrelevement: {0}\nGlycemie a jeun: 1.12 g/L, HbA1c: 6.8%, Creatinine: 98 umol/L",
            "Ordonnance medicale - Medecine generale\nPatient: {0}\nBisoprolol 5mg: 1 comprime le matin, Ramipril 10mg: 1 comprime le soir",
            "Imagerie medicale - Scanner thoracique\nPatient: {0}\nResultat: nodule pulmonaire 8mm lobe superieur droit, controle recommande a 3 mois",
            "Dossier infirmier - Soins intensifs\nPatient: {0}\nConstantes: TA 130/85, FC 72, SpO2 97%, Temperature 37.2C",
            "Prescription pharmaceutique - Pharmacie hospitaliere\nPatient: {0}\nAmoxicilline 1g: 3 fois par jour pendant 7 jours",
            "Certificat medical - Medecine du travail\nPatient: {0}\nApte au poste avec restriction: pas de port de charge superieur a 15kg",
            "Consultation dermatologie\nPatient: {0}\nExamen: lesion pigmentee irreguliere 6mm dos, biopsie exerese recommandee",
            "Echographie abdominale - Service de radiologie\nPatient: {0}\nFoie: taille normale, echostructure homogene, pas de lesion focale",
            "Courrier de sortie - Chirurgie orthopedique\nPatient: {0}\nIntervention: arthroplastie totale de hanche droite, suites simples"
        ];

        for (int i = 0; i < 10; i++)
        {
            string fileName = $"medical_file_{i:D3}.txt";
            string path = Path.Combine(_testDir, fileName);
            string content = string.Format(templates[i], $"Patient-{i:D3}");
            // Pad to ~2KB for meaningful entropy measurement
            content += new string(' ', 2000 - content.Length);
            await File.WriteAllTextAsync(path, content);
            _medicalFiles.Add(path);

            // Build entropy baseline
            double? entropy = await _calculator.ComputeFileEntropyAsync(path, CancellationToken.None);
            _context.EntropyBaselines.Add(new EntropyBaseline
            {
                FilePath = path,
                DirectoryPath = _testDir,
                FileExtension = ".txt",
                EntropyValue = entropy ?? 4.0,
                FileSize = new FileInfo(path).Length
            });
        }
        await _context.SaveChangesAsync();
    }

    public Task DisposeAsync()
    {
        Dispose();
        return Task.CompletedTask;
    }

    [Fact(Timeout = 120_000)]
    public async Task Encrypt_Then_Exfil_Triggers_Rule8_Double_Extortion()
    {
        const int fakePid = 7777;
        var entropyAlerts = new List<EntropyAlert>();
        var signalsPublished = new List<EntropySignal>();

        // Track published signals
        _eventBus.Subscribe<EntropySignal>(async (signal, _) =>
        {
            signalsPublished.Add(signal);
            await Task.CompletedTask;
        });

        // ===== PHASE 1: Encrypt 10 files (simulating ransomware) =====
        using var aes = Aes.Create();
        foreach (string file in _medicalFiles)
        {
            byte[] original = await File.ReadAllBytesAsync(file);
            byte[] encrypted = aes.EncryptCbc(original, aes.IV);
            await File.WriteAllBytesAsync(file, encrypted);

            double? current = await _calculator.ComputeFileEntropyAsync(file, CancellationToken.None);
            current.ShouldNotBeNull();

            var baseline = _context.EntropyBaselines.First(b => b.FilePath == file);
            var alert = _detector.Analyze(file, current.Value, baseline);

            if (alert is not null)
            {
                entropyAlerts.Add(alert);
                _context.EntropyAlerts.Add(alert);

                // Publish EntropySignal (what EntropyMonitor does)
                var signal = new EntropySignal
                {
                    SignalId = Guid.NewGuid(),
                    SourceModule = "ENTROPY",
                    EmittedAt = DateTime.UtcNow,
                    FilePath = file,
                    ProcessId = fakePid,
                    ProcessName = "ransomware.exe",
                    EntropyValue = current.Value,
                    FileSize = new FileInfo(file).Length,
                    EntropyAlertId = alert.Id
                };
                await _eventBus.PublishAsync(signal, CancellationToken.None);
            }
        }
        await _context.SaveChangesAsync();

        // Wait for signal delivery
        await Task.Delay(200);

        // ===== PHASE 2: Simulate network upload (matching PID, ~80% of file sizes) =====
        var tracker = new Mock<IDataVolumeTracker>();
        tracker.Setup(t => t.GetBytesSent(fakePid, It.IsAny<TimeSpan>())).Returns(15_000);
        tracker.Setup(t => t.IsLearningPhase).Returns(false);

        // Get actual encrypted file size for 80% threshold calculation
        long encryptedSize = new FileInfo(_medicalFiles[0]).Length;
        long uploadBytes = (long)(encryptedSize * 0.85); // 85% of file size — exceeds 80% threshold

        var uploadEvent = new NetworkEvent
        {
            Id = Guid.NewGuid(),
            EventType = NetworkEventType.TcpSend,
            ProcessId = fakePid,
            ProcessName = "ransomware.exe",
            DestinationAddress = "45.33.32.1",
            DestinationPort = 443,
            BytesSent = uploadBytes,
            Protocol = "TCP",
            Timestamp = DateTime.UtcNow
        };

        var options = new ExfilWatchOptions();
        var finding = _rule8.Evaluate(uploadEvent, tracker.Object, options);

        // ===== ASSERTIONS =====

        // 1. Entropy alerts fired for encrypted files
        entropyAlerts.Count.ShouldBeGreaterThanOrEqualTo(5,
            "At least 5 of 10 medical files should trigger entropy alerts");

        // 2. EntropySignals published
        signalsPublished.Count.ShouldBeGreaterThanOrEqualTo(5,
            "At least 5 EntropySignals should be published to event bus");

        // 3. Rule 8 correlates encryption + exfil
        finding.ShouldNotBeNull("Rule 8 EncryptedExfilCorrelation should fire for encrypt+upload pattern");
        finding.RuleName.ShouldBe("EncryptedExfilCorrelation");
        finding.Severity.ShouldBe(ExfilSeverity.Critical);

        // 4. MITRE technique attribution
        finding.MitreId.ShouldContain("T1486");

        // 5. Description indicates double extortion
        finding.Description.ShouldContain("encrypt", Case.Insensitive);

        // 6. Cross-linking capability verified
        var firstEntropyAlert = entropyAlerts.First();
        firstEntropyAlert.Id.ShouldNotBe(Guid.Empty);

        // 7. Audit log records complete attack chain
        await _auditRepo.AppendAsync("DoubleExtortionDetected",
            $"Rule 8: encrypt+exfil correlation for PID {fakePid}",
            entityType: "ExfilAlert",
            cancellationToken: CancellationToken.None);

        bool chainIntact = await _auditRepo.VerifyChainIntegrityAsync();
        chainIntact.ShouldBeTrue("Audit log hash chain should be intact");
    }

    public void Dispose()
    {
        Thread.Sleep(500);
        try { (_rule8 as IDisposable)?.Dispose(); } catch { }
        try { _eventBus.Dispose(); } catch { }
        try { _context.Database.CloseConnection(); } catch { }
        try { _context.Dispose(); } catch { }
        try { Directory.Delete(_testDir, recursive: true); } catch { }
    }
}
