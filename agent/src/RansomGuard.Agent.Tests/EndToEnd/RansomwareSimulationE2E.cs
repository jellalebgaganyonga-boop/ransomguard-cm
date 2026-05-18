using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;
using RansomGuard.Agent.Core.Detection.Entropy;
using RansomGuard.Agent.Core.Persistence;
using RansomGuard.Agent.Core.Persistence.Entities;
using Shouldly;

namespace RansomGuard.Agent.Tests.EndToEnd;

/// <summary>
/// Full E2E ransomware simulation test using the actual detection pipeline.
/// Validates: file creation -> entropy baseline -> encryption -> alert fires.
/// Uses in-memory database and temp directory to avoid production side effects.
/// </summary>
public sealed class RansomwareSimulationE2E : IDisposable
{
    private readonly string _testDir;
    private readonly AgentDbContext _context;
    private readonly EntropyCalculator _calculator;
    private readonly EntropyDetector _detector;

    public RansomwareSimulationE2E()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"ransomguard_e2e_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);

        var options = new DbContextOptionsBuilder<AgentDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        _context = new AgentDbContext(options);
        _context.Database.OpenConnection();
        _context.Database.EnsureCreated();

        _calculator = new EntropyCalculator(new Mock<ILogger<EntropyCalculator>>().Object);
        _detector = new EntropyDetector(new RansomGuard.Agent.Core.Configuration.EntropyOptions
        {
            AbsoluteThreshold = 7.5,
            DeltaThreshold = 2.5,
            DirectoryShiftThreshold = 1.5,
            DirectoryShiftMinFiles = 5,
            WhitelistedExtensions = [".zip", ".jpg", ".png"],
            SusceptibleExtensions = [".txt", ".docx", ".pdf", ".csv", ".xlsx"]
        });
    }

    [Fact]
    public async Task FullPipeline_Ransomware_Encrypts_Hospital_Records_Detection_Fires()
    {
        var sw = Stopwatch.StartNew();

        // Phase 1: Create 10 realistic medical text files (French hospital context)
        string[] templates =
        [
            "Dossier patient: {0}\nConsultation du 01/05/2026\nTension arterielle: 130/85\nTraitement: Amlodipine 10mg\nProchain RDV: 15/06/2026",
            "Analyses laboratoire: {0}\nGlycemie a jeun: 1.05 g/L\nHb: 12.8 g/dL\nCreatinine: 9.2 mg/L\nConclusion: normal",
            "Imagerie medicale: {0}\nRadio thorax face\nParenchyme pulmonaire clair\nSilhouette cardiaque normale\nConclusion: RAS",
            "Prescription: {0}\nAmoxicilline 1g x 3/jour pendant 7 jours\nParacetamol 1g si douleur\nControle dans 10 jours",
            "Rapport consultation: {0}\nPatient se presente pour cephalees chroniques\nExamen neurologique normal\nIRM cerebrale prescrite"
        ];

        var filePaths = new List<string>();
        var baselines = new Dictionary<string, EntropyBaseline>();

        for (int i = 0; i < 10; i++)
        {
            string fileName = $"patient_record_{i:D3}.txt";
            string filePath = Path.Combine(_testDir, fileName);
            string content = string.Format(templates[i % templates.Length], $"Patient-{i:D3}");

            // Add enough text to make entropy measurement reliable
            var sb = new StringBuilder();
            for (int j = 0; j < 20; j++)
            {
                sb.AppendLine(content);
            }
            await File.WriteAllTextAsync(filePath, sb.ToString());
            filePaths.Add(filePath);
        }

        // Phase 2: Build baselines
        foreach (string path in filePaths)
        {
            double? entropy = await _calculator.ComputeFileEntropyAsync(path);
            entropy.ShouldNotBeNull();
            entropy!.Value.ShouldBeLessThan(6.0, $"Text file {Path.GetFileName(path)} should have low entropy");

            var baseline = new EntropyBaseline
            {
                FilePath = path,
                DirectoryPath = _testDir,
                FileExtension = ".txt",
                EntropyValue = entropy.Value,
                FileSize = new FileInfo(path).Length
            };
            baselines[path] = baseline;
            _context.EntropyBaselines.Add(baseline);
        }
        await _context.SaveChangesAsync();

        // Phase 3: Simulate ransomware — replace all files with encrypted content
        int alertsFired = 0;
        double totalDelta = 0;
        var alerts = new List<EntropyAlert>();

        foreach (string path in filePaths)
        {
            // Ransomware replaces file content with AES-encrypted random bytes
            byte[] encrypted = RandomNumberGenerator.GetBytes((int)new FileInfo(path).Length);
            await File.WriteAllBytesAsync(path, encrypted);

            double? currentEntropy = await _calculator.ComputeFileEntropyAsync(path);
            currentEntropy.ShouldNotBeNull();
            currentEntropy!.Value.ShouldBeGreaterThan(7.5, "Encrypted file should have near-maximum entropy");

            EntropyAlert? alert = _detector.Analyze(path, currentEntropy.Value, baselines[path]);
            if (alert is not null)
            {
                alertsFired++;
                totalDelta += alert.Delta;
                alerts.Add(alert);
                _context.EntropyAlerts.Add(alert);
            }
        }
        await _context.SaveChangesAsync();

        // Phase 4: Verify detection results
        alertsFired.ShouldBeGreaterThanOrEqualTo(8, "At least 8 of 10 files should trigger alerts");

        // All alerts should be Rule 1 (AbsoluteHighEntropy) or Rule 2 (SuddenEntropyDelta)
        foreach (var alert in alerts)
        {
            alert.RuleId.ShouldBeOneOf(1, 2);
            alert.Severity.ShouldBeOneOf("Critical", "High");
        }

        // Phase 5: Verify directory-wide shift detection (Rule 3)
        if (alertsFired > 0)
        {
            double avgDelta = totalDelta / alertsFired;
            EntropyAlert? dirAlert = _detector.AnalyzeDirectoryShift(_testDir, alertsFired, avgDelta);

            dirAlert.ShouldNotBeNull("Directory-wide shift should fire for mass encryption");
            dirAlert.RuleId.ShouldBe(3);
            dirAlert.Severity.ShouldBe("Critical");
        }

        // Phase 6: Verify database persistence
        int dbAlertCount = await _context.EntropyAlerts.CountAsync();
        dbAlertCount.ShouldBeGreaterThanOrEqualTo(8);

        sw.Stop();

        // Phase 7: Verify latency under 90 seconds
        sw.Elapsed.TotalSeconds.ShouldBeLessThan(90, "Full E2E pipeline should complete under 90 seconds");
    }

    [Fact]
    public async Task LegitimateEdits_No_False_Positives()
    {
        // Create a medical record
        string path = Path.Combine(_testDir, "consultation_notes.txt");
        var sb = new StringBuilder();
        for (int i = 0; i < 30; i++)
        {
            sb.AppendLine("Patient Mballa Jean, consultation du 01/05/2026. Tension 130/85. Traitement Amlodipine 10mg.");
        }
        await File.WriteAllTextAsync(path, sb.ToString());

        double? baselineEntropy = await _calculator.ComputeFileEntropyAsync(path);
        var baseline = new EntropyBaseline
        {
            FilePath = path,
            DirectoryPath = _testDir,
            FileExtension = ".txt",
            EntropyValue = baselineEntropy!.Value,
            FileSize = new FileInfo(path).Length
        };

        // Simulate legitimate edits: append more text
        await File.AppendAllTextAsync(path, "\nSuivi: tension amelioree, continuer traitement. Prochain RDV 15/06/2026.");
        await File.AppendAllTextAsync(path, "\nNote infirmiere: patient stable, parametres vitaux normaux.");

        double? currentEntropy = await _calculator.ComputeFileEntropyAsync(path);
        EntropyAlert? alert = _detector.Analyze(path, currentEntropy!.Value, baseline);

        alert.ShouldBeNull("Legitimate text edits should NOT trigger alerts");
    }

    [Fact]
    public async Task WhitelistedFormat_No_Alert_Even_High_Entropy()
    {
        // Create a ZIP file (high entropy by nature)
        string path = Path.Combine(_testDir, "backup.zip");
        await File.WriteAllBytesAsync(path, RandomNumberGenerator.GetBytes(4096));

        double? entropy = await _calculator.ComputeFileEntropyAsync(path);
        entropy.ShouldNotBeNull();
        entropy!.Value.ShouldBeGreaterThan(7.0);

        var baseline = new EntropyBaseline
        {
            FilePath = path,
            DirectoryPath = _testDir,
            FileExtension = ".zip",
            EntropyValue = 3.0, // Even with a low fake baseline
            FileSize = 4096
        };

        EntropyAlert? alert = _detector.Analyze(path, entropy.Value, baseline);
        alert.ShouldBeNull("Whitelisted extensions should never trigger alerts");
    }

    public void Dispose()
    {
        _context.Database.CloseConnection();
        _context.Dispose();
        try { if (Directory.Exists(_testDir)) Directory.Delete(_testDir, true); }
        catch { /* best-effort cleanup */ }
    }
}
