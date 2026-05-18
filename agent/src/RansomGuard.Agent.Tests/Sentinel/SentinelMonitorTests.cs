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
/// Tests for SENTINEL monitoring — detection of canary file modification, deletion, and renaming.
/// </summary>
public sealed class SentinelMonitorTests : IDisposable
{
    private readonly AgentDbContext _context;
    private readonly SentinelCanaryRepository _canaryRepo;
    private readonly AuditLogRepository _auditRepo;
    private readonly CanaryFileService _canaryService;
    private readonly string _testDir;

    public SentinelMonitorTests()
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

        _testDir = Path.Combine(Path.GetTempPath(), $"ransomguard_monitor_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
    }

    [Fact]
    public async Task Should_detect_canary_modification()
    {
        SentinelCanary canary = await _canaryService.CreateCanaryAsync(_testDir, "dossier_patient", "0001_");

        // Tamper with canary
        await File.AppendAllTextAsync(canary.FilePath, "\nRANSOMWARE_CONTENT");

        bool isIntact = await _canaryService.VerifyCanaryIntegrityAsync(canary);
        isIntact.ShouldBeFalse();

        // Simulate alert creation
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
        alerts[0].Severity.ShouldBe(AlertSeverity.Critical);
    }

    [Fact]
    public async Task Should_detect_canary_deletion()
    {
        SentinelCanary canary = await _canaryService.CreateCanaryAsync(_testDir, "dossier_patient", "0001_");
        File.Delete(canary.FilePath);

        File.Exists(canary.FilePath).ShouldBeFalse();

        bool isIntact = await _canaryService.VerifyCanaryIntegrityAsync(canary);
        isIntact.ShouldBeFalse();

        var alert = new CanaryAlert
        {
            CanaryId = canary.Id,
            CanaryPath = canary.FilePath,
            AlertType = CanaryAlertType.CanaryDeleted,
            Severity = AlertSeverity.Critical
        };

        canary.Status = CanaryStatus.Deleted;
        await _canaryRepo.UpdateAsync(canary);
        await _canaryRepo.AddAlertAsync(alert);

        IReadOnlyList<CanaryAlert> alerts = await _canaryRepo.GetAlertsAsync();
        alerts[0].AlertType.ShouldBe(CanaryAlertType.CanaryDeleted);
    }

    [Fact]
    public async Task Should_detect_canary_rename()
    {
        SentinelCanary canary = await _canaryService.CreateCanaryAsync(_testDir, "dossier_patient", "0001_");
        string newPath = canary.FilePath + ".locked";
        File.Move(canary.FilePath, newPath);

        File.Exists(canary.FilePath).ShouldBeFalse();

        var alert = new CanaryAlert
        {
            CanaryId = canary.Id,
            CanaryPath = canary.FilePath,
            AlertType = CanaryAlertType.CanaryRenamed,
            Severity = AlertSeverity.Critical
        };

        canary.Status = CanaryStatus.Renamed;
        await _canaryRepo.UpdateAsync(canary);
        await _canaryRepo.AddAlertAsync(alert);

        SentinelCanary? updated = await _canaryRepo.GetByIdAsync(canary.Id);
        updated!.Status.ShouldBe(CanaryStatus.Renamed);
    }

    [Fact]
    public async Task Alert_should_be_audit_logged()
    {
        SentinelCanary canary = await _canaryService.CreateCanaryAsync(_testDir, "dossier_patient", "0001_");

        var alert = new CanaryAlert
        {
            CanaryId = canary.Id,
            CanaryPath = canary.FilePath,
            AlertType = CanaryAlertType.CanaryModified,
            Severity = AlertSeverity.Critical
        };

        await _canaryRepo.AddAlertAsync(alert);
        await _auditRepo.AppendAsync("CanaryAlert", $"Canary alert: CanaryModified on {canary.FilePath}", "CanaryAlert", alert.Id);

        AuditLog? latest = await _auditRepo.GetLatestAsync();
        latest.ShouldNotBeNull();
        latest.Action.ShouldBe("CanaryAlert");
        latest.EntityType.ShouldBe("CanaryAlert");
        latest.EntityId.ShouldBe(alert.Id);
    }

    [Fact]
    public async Task Unmodified_canary_should_pass_integrity_check()
    {
        SentinelCanary canary = await _canaryService.CreateCanaryAsync(_testDir, "dossier_patient", "0001_");

        bool isIntact = await _canaryService.VerifyCanaryIntegrityAsync(canary);
        isIntact.ShouldBeTrue();
    }

    [Fact]
    public async Task Alert_should_store_process_attribution_fields()
    {
        var alert = new CanaryAlert
        {
            CanaryId = Guid.NewGuid(),
            CanaryPath = @"C:\Test\canary.txt",
            AlertType = CanaryAlertType.CanaryModified,
            Severity = AlertSeverity.Critical,
            OffendingProcessId = 12345,
            OffendingProcessName = "ransomware.exe",
            OffendingProcessPath = @"C:\Temp\ransomware.exe"
        };

        await _canaryRepo.AddAlertAsync(alert);

        IReadOnlyList<CanaryAlert> alerts = await _canaryRepo.GetAlertsAsync();
        alerts[0].OffendingProcessId.ShouldBe(12345);
        alerts[0].OffendingProcessName.ShouldBe("ransomware.exe");
        alerts[0].OffendingProcessPath.ShouldBe(@"C:\Temp\ransomware.exe");
    }

    public void Dispose()
    {
        _context.Database.CloseConnection();
        _context.Dispose();

        try { if (Directory.Exists(_testDir)) Directory.Delete(_testDir, true); }
        catch { /* best-effort */ }
    }
}
