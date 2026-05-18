using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using RansomGuard.Agent.Core.Configuration;
using RansomGuard.Agent.Core.Detection.Entropy;
using RansomGuard.Agent.Core.Persistence;
using RansomGuard.Agent.Core.Persistence.Entities;
using Shouldly;

namespace RansomGuard.Agent.Tests.Entropy;

/// <summary>
/// End-to-end entropy detection tests simulating ransomware encryption.
/// </summary>
public sealed class EntropyDetectionE2ETests : IDisposable
{
    private readonly AgentDbContext _context;
    private readonly EntropyCalculator _calculator;
    private readonly EntropyDetector _detector;
    private readonly string _testDir;

    public EntropyDetectionE2ETests()
    {
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
            SusceptibleExtensions = [".txt", ".docx", ".pdf", ".csv"]
        });

        _testDir = Path.Combine(Path.GetTempPath(), $"ransomguard_entropy_e2e_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
    }

    [Fact]
    public async Task Ransomware_encrypts_text_file_entropy_alert_fires()
    {
        // 1. Create legitimate medical text file
        string filePath = Path.Combine(_testDir, "patient_record.txt");
        StringBuilder sb = new();
        for (int i = 0; i < 50; i++)
            sb.AppendLine("Patient Mballa, consultation 01/05/2026, tension 130/85, Amlodipine 10mg.");
        await File.WriteAllTextAsync(filePath, sb.ToString());

        // 2. Measure baseline
        double? baselineEntropy = await _calculator.ComputeFileEntropyAsync(filePath);
        baselineEntropy.ShouldNotBeNull();
        baselineEntropy!.Value.ShouldBeLessThan(5.5);

        var baseline = new EntropyBaseline
        {
            FilePath = filePath,
            DirectoryPath = _testDir,
            FileExtension = ".txt",
            EntropyValue = baselineEntropy.Value,
            FileSize = new FileInfo(filePath).Length
        };

        // 3. Simulate ransomware: replace content with AES-encrypted bytes
        byte[] encrypted = RandomNumberGenerator.GetBytes((int)new FileInfo(filePath).Length);
        await File.WriteAllBytesAsync(filePath, encrypted);

        // 4. Measure new entropy
        double? currentEntropy = await _calculator.ComputeFileEntropyAsync(filePath);
        currentEntropy.ShouldNotBeNull();
        currentEntropy!.Value.ShouldBeGreaterThan(7.8);

        // 5. Detection
        EntropyAlert? alert = _detector.Analyze(filePath, currentEntropy.Value, baseline);

        alert.ShouldNotBeNull();
        alert.RuleId.ShouldBe(1); // AbsoluteHighEntropy
        alert.Severity.ShouldBe("Critical");
        alert.Delta.ShouldBeGreaterThan(2.5);
    }

    [Fact]
    public async Task Legitimate_edit_no_alert()
    {
        string filePath = Path.Combine(_testDir, "notes.txt");
        await File.WriteAllTextAsync(filePath, "Patient notes from consultation on 01/05/2026.");

        double? baselineEntropy = await _calculator.ComputeFileEntropyAsync(filePath);
        var baseline = new EntropyBaseline
        {
            FilePath = filePath,
            DirectoryPath = _testDir,
            FileExtension = ".txt",
            EntropyValue = baselineEntropy!.Value,
            FileSize = new FileInfo(filePath).Length
        };

        // Legitimate edit: append more text
        await File.AppendAllTextAsync(filePath, "\nSuivi: tension amelioree, continuer traitement.");

        double? currentEntropy = await _calculator.ComputeFileEntropyAsync(filePath);
        EntropyAlert? alert = _detector.Analyze(filePath, currentEntropy!.Value, baseline);

        alert.ShouldBeNull(); // Text → text = no significant entropy change
    }

    [Fact]
    public async Task Mass_encryption_triggers_directory_shift()
    {
        // Create 10 text files
        var baselines = new List<EntropyBaseline>();
        for (int i = 0; i < 10; i++)
        {
            string path = Path.Combine(_testDir, $"record_{i}.csv");
            await File.WriteAllTextAsync(path, $"ID,Nom,Prenom,Diagnostic\n{i},Mballa,Jean,Paludisme\n");

            double? e = await _calculator.ComputeFileEntropyAsync(path);
            baselines.Add(new EntropyBaseline
            {
                FilePath = path,
                DirectoryPath = _testDir,
                FileExtension = ".csv",
                EntropyValue = e!.Value,
                FileSize = new FileInfo(path).Length
            });
        }

        // Encrypt all 10 files
        int alertCount = 0;
        double totalDelta = 0;

        for (int i = 0; i < 10; i++)
        {
            string path = Path.Combine(_testDir, $"record_{i}.csv");
            await File.WriteAllBytesAsync(path, RandomNumberGenerator.GetBytes(256));

            double? current = await _calculator.ComputeFileEntropyAsync(path);
            EntropyAlert? alert = _detector.Analyze(path, current!.Value, baselines[i]);
            if (alert is not null)
            {
                alertCount++;
                totalDelta += alert.Delta;
            }
        }

        alertCount.ShouldBeGreaterThan(5);

        // Check directory-wide rule
        double avgDelta = totalDelta / alertCount;
        EntropyAlert? dirAlert = _detector.AnalyzeDirectoryShift(_testDir, alertCount, avgDelta);
        dirAlert.ShouldNotBeNull();
        dirAlert.RuleId.ShouldBe(3);
        dirAlert.Severity.ShouldBe("Critical");
    }

    public void Dispose()
    {
        _context.Database.CloseConnection();
        _context.Dispose();
        try { if (Directory.Exists(_testDir)) Directory.Delete(_testDir, true); }
        catch { /* best-effort */ }
    }
}
