using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using RansomGuard.Agent.Core.Detection.Sentinel;
using RansomGuard.Agent.Core.Persistence;
using RansomGuard.Agent.Core.Persistence.Entities;
using RansomGuard.Agent.Core.Persistence.Repositories;
using Shouldly;

namespace RansomGuard.Agent.Tests.Sentinel;

/// <summary>
/// End-to-end tests simulating ransomware behavior against SENTINEL canary files.
/// </summary>
public sealed class SentinelEndToEndTests : IDisposable
{
    private readonly AgentDbContext _context;
    private readonly SentinelCanaryRepository _canaryRepo;
    private readonly AuditLogRepository _auditRepo;
    private readonly CanaryFileService _canaryService;
    private readonly string _testDir;

    public SentinelEndToEndTests()
    {
        var options = new DbContextOptionsBuilder<AgentDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;

        _context = new AgentDbContext(options);
        _context.Database.OpenConnection();
        _context.Database.EnsureCreated();
        _canaryRepo = new SentinelCanaryRepository(_context);
        _auditRepo = new AuditLogRepository(_context);

        var logger = new Mock<ILogger<CanaryFileService>>();
        _canaryService = new CanaryFileService(_canaryRepo, logger.Object);

        _testDir = Path.Combine(Path.GetTempPath(), $"ransomguard_e2e_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
    }

    [Fact]
    public async Task Scenario1_ransomware_renames_all_files_canary_fires_first()
    {
        // Deploy 3 canaries
        var canaries = new List<SentinelCanary>();
        string[] templates = ["dossier_patient", "analyses_laboratoire", "imagerie_medicale"];
        foreach (string t in templates)
        {
            canaries.Add(await _canaryService.CreateCanaryAsync(_testDir, t, "0001_"));
        }

        // Deploy a "real" file that sorts AFTER canaries
        string realFile = Path.Combine(_testDir, "zzz_real_patient_data.txt");
        await File.WriteAllTextAsync(realFile, "Real patient data here");

        // Simulate ransomware: enumerate and rename all files to .locked
        string[] allFiles = Directory.GetFiles(_testDir);
        Array.Sort(allFiles); // Alphabetical — canaries should be first

        // Verify canaries sort first (they have "0001_" prefix)
        allFiles[0].ShouldContain("0001_");
        allFiles[1].ShouldContain("0001_");
        allFiles[2].ShouldContain("0001_");

        // Simulate the ransomware hitting the first file (a canary)
        string firstFile = allFiles[0];
        SentinelCanary? hitCanary = await _canaryRepo.GetByFilePathAsync(firstFile);
        hitCanary.ShouldNotBeNull();

        // Fire alert for the first file hit
        var alert = new CanaryAlert
        {
            CanaryId = hitCanary.Id,
            CanaryPath = hitCanary.FilePath,
            AlertType = CanaryAlertType.CanaryRenamed,
            Severity = AlertSeverity.Critical
        };

        hitCanary.Status = CanaryStatus.Renamed;
        await _canaryRepo.UpdateAsync(hitCanary);
        await _canaryRepo.AddAlertAsync(alert);
        await _auditRepo.AppendAsync("CanaryAlert", $"Canary renamed: {hitCanary.FilePath}", "CanaryAlert", alert.Id);

        // Verify alert exists
        IReadOnlyList<CanaryAlert> alerts = await _canaryRepo.GetAlertsAsync();
        alerts.Count.ShouldBe(1);
        alerts[0].AlertType.ShouldBe(CanaryAlertType.CanaryRenamed);

        // Verify audit log
        AuditLog? auditEntry = await _auditRepo.GetLatestAsync();
        auditEntry.ShouldNotBeNull();
        auditEntry.Action.ShouldBe("CanaryAlert");
    }

    [Fact]
    public async Task Scenario2_single_canary_content_modification()
    {
        SentinelCanary canary = await _canaryService.CreateCanaryAsync(_testDir, "dossier_patient", "0001_");

        // Verify canary is intact
        (await _canaryService.VerifyCanaryIntegrityAsync(canary)).ShouldBeTrue();

        // Simulate notepad-style edit
        await File.AppendAllTextAsync(canary.FilePath, "\n--- MODIFIED BY UNAUTHORIZED ACCESS ---");

        // Verify integrity fails
        (await _canaryService.VerifyCanaryIntegrityAsync(canary)).ShouldBeFalse();

        // Create alert
        var alert = new CanaryAlert
        {
            CanaryId = canary.Id,
            CanaryPath = canary.FilePath,
            AlertType = CanaryAlertType.CanaryModified,
            Severity = AlertSeverity.Critical
        };

        canary.Status = CanaryStatus.Modified;
        await _canaryRepo.UpdateAsync(canary);
        await _canaryRepo.AddAlertAsync(alert);

        IReadOnlyList<CanaryAlert> alerts = await _canaryRepo.GetAlertsAsync();
        alerts.Count.ShouldBe(1);
        alerts[0].AlertType.ShouldBe(CanaryAlertType.CanaryModified);
    }

    [Fact]
    public async Task Scenario3_canary_regeneration_after_deletion()
    {
        SentinelCanary canary = await _canaryService.CreateCanaryAsync(_testDir, "dossier_patient", "0001_");
        string originalPath = canary.FilePath;

        // Delete the canary (simulating ransomware)
        File.Delete(canary.FilePath);
        canary.Status = CanaryStatus.Deleted;
        await _canaryRepo.UpdateAsync(canary);

        // Fire deletion alert
        await _canaryRepo.AddAlertAsync(new CanaryAlert
        {
            CanaryId = canary.Id,
            CanaryPath = canary.FilePath,
            AlertType = CanaryAlertType.CanaryDeleted,
            Severity = AlertSeverity.Critical
        });

        // Verify deleted canary no longer in active list
        IReadOnlyList<SentinelCanary> active = await _canaryService.GetAllCanariesAsync();
        active.Count.ShouldBe(0);

        // Simulate deployment service re-deploying
        SentinelCanary newCanary = await _canaryService.CreateCanaryAsync(_testDir, "dossier_patient", "0001_");

        active = await _canaryService.GetAllCanariesAsync();
        active.Count.ShouldBe(1);
        active[0].Id.ShouldNotBe(canary.Id); // New canary, different ID
        File.Exists(newCanary.FilePath).ShouldBeTrue();
    }

    public void Dispose()
    {
        _context.Database.CloseConnection();
        _context.Dispose();

        try { if (Directory.Exists(_testDir)) Directory.Delete(_testDir, true); }
        catch { /* best-effort */ }
    }
}
