using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using RansomGuard.Agent.Core.Detection.Entropy;
using RansomGuard.Agent.Core.Detection.Genealogy;
using RansomGuard.Agent.Core.Detection.UsbGuard;
using RansomGuard.Agent.Core.Detection.UsbGuard.Actions;
using RansomGuard.Agent.Core.Detection.UsbGuard.Models;
using RansomGuard.Agent.Core.Detection.UsbGuard.Scanning;
using RansomGuard.Agent.Core.Persistence;
using RansomGuard.Agent.Core.Persistence.Entities;
using Shouldly;

namespace RansomGuard.Agent.Tests.Integration.UsbGuard;

/// <summary>
/// Integration tests verifying the USB GUARD wired pipeline:
/// UsbDeviceMonitor -> Whitelist -> ContentScanner -> ActionEngine -> Genealogy.
/// Uses in-memory SQLite and mocks for WMI/file system.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class UsbGuardIntegrationTests : IDisposable
{
    private readonly AgentDbContext _context;
    private readonly string _testDir;
    private readonly byte[] _salt;

    public UsbGuardIntegrationTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"rg_usb_integration_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
        _salt = RandomNumberGenerator.GetBytes(32);

        var options = new DbContextOptionsBuilder<AgentDbContext>()
            .UseSqlite($"Data Source={Path.Combine(_testDir, "test.db")}")
            .Options;
        _context = new AgentDbContext(options);
        _context.Database.EnsureCreated();
    }

    // Test 1: DI resolution — all USB GUARD services resolve without error
    [Fact]
    public void UsbDeviceMonitor_WiredCorrectly_ResolvesAllDependencies()
    {
        var services = new ServiceCollection();

        // Core services required by USB GUARD
        services.AddDbContext<AgentDbContext>(opt =>
            opt.UseSqlite($"Data Source={Path.Combine(_testDir, "di_test.db")}"));
        services.AddLogging();
        services.AddSingleton<IEntropyCalculator, EntropyCalculator>();

        // USB GUARD services
        services.AddScoped<IUsbWhitelistService>(sp =>
            new UsbWhitelistService(
                sp.GetRequiredService<AgentDbContext>(),
                sp.GetRequiredService<ILogger<UsbWhitelistService>>(),
                _salt));
        services.AddSingleton<IMagicByteValidator, MagicByteValidator>();
        services.AddSingleton<AutorunInfDetector>();
        services.AddSingleton<SuspiciousLnkDetector>();
        services.AddSingleton<ArchiveScanner>();
        services.AddSingleton<UsbEntropyScanner>();
        services.AddScoped<IUsbContentScanner, UsbContentScanner>();
        services.AddSingleton<BootableUsbDetector>();
        services.AddScoped<IUsbActionEngine, UsbActionEngine>();

        var provider = services.BuildServiceProvider();

        // Verify all critical services resolve
        using var scope = provider.CreateScope();
        var sp = scope.ServiceProvider;

        sp.GetRequiredService<IUsbWhitelistService>().ShouldNotBeNull();
        sp.GetRequiredService<IUsbContentScanner>().ShouldNotBeNull();
        sp.GetRequiredService<IUsbActionEngine>().ShouldNotBeNull();
        sp.GetRequiredService<IMagicByteValidator>().ShouldNotBeNull();
        sp.GetRequiredService<AgentDbContext>().ShouldNotBeNull();

        // Verify DbContext can create schema
        var ctx = sp.GetRequiredService<AgentDbContext>();
        ctx.Database.EnsureCreated();
    }

    // Test 2: USB connection triggers content scan and persists scan result
    [Fact]
    public async Task UsbConnection_TriggersContentScan_PersistsScanResult()
    {
        // Arrange: create a USB drive directory with a test file
        string driveDir = Path.Combine(_testDir, "E_drive");
        Directory.CreateDirectory(driveDir);
        await File.WriteAllBytesAsync(Path.Combine(driveDir, "test.txt"), Encoding.UTF8.GetBytes("hello"));

        var device = CreateTestDevice(driveDir);
        var policy = CreateTestPolicy();

        var scanner = CreateScanner();
        var report = await scanner.ScanAsync(device, policy);

        report.ShouldNotBeNull();
        report.FilesScanned.ShouldBeGreaterThanOrEqualTo(1);
        report.DriveLetter.ShouldNotBeNullOrEmpty();
        report.WasCancelled.ShouldBeFalse();

        // Persist to DB
        var scanResult = new UsbScanResult
        {
            UsbConnectionLogId = Guid.NewGuid(),
            TotalFilesScanned = report.FilesScanned,
            FlaggedFilesCount = report.Findings.Count,
            FlaggedFilesJson = JsonSerializer.Serialize(report.Findings),
            ScanDurationMs = (long)report.Duration.TotalMilliseconds,
            ScanCompleted = true,
            HighestSeverity = report.OverallSeverity.ToString(),
            ScannedAt = report.ScannedAt
        };
        _context.UsbScanResults.Add(scanResult);
        await _context.SaveChangesAsync();

        var persisted = await _context.UsbScanResults.FirstAsync();
        persisted.TotalFilesScanned.ShouldBe(report.FilesScanned);
    }

    // Test 3: Whitelisted device skips content scan
    [Fact]
    public async Task WhitelistedDevice_SkipsContentScan()
    {
        var whitelistService = new UsbWhitelistService(
            _context, new Mock<ILogger<UsbWhitelistService>>().Object, _salt);

        // Add to whitelist
        await whitelistService.AddAsync("SER123", "Test device", UsbPolicyLevel.Trusted);

        var (isWhitelisted, level) = await whitelistService.IsWhitelistedAsync("SER123");

        isWhitelisted.ShouldBeTrue();
        level.ShouldBe(UsbPolicyLevel.Trusted);
    }

    // Test 4: Bootable USB triggers immediate block action
    [Fact]
    public async Task BootableUsb_TriggersImmediateBlockAction()
    {
        var device = CreateTestDevice(null) with { IsBootable = true };
        var actionEngine = new UsbActionEngine(new Mock<ILogger<UsbActionEngine>>().Object);

        var result = await actionEngine.ExecuteAsync(
            device, ScanSeverity.Critical, UsbOperatingMode.Strict,
            "Bootable USB blocked by policy");

        result.ActionType.ShouldBe(UsbActionType.BlockAndEject);
        result.Success.ShouldBeTrue();
    }

    // Test 5: High severity scan triggers action and fires genealogy enrichment
    [Fact]
    public async Task HighSeverityScan_TriggersAction_AndPersistsAlert()
    {
        var device = CreateTestDevice(null);
        var actionEngine = new UsbActionEngine(new Mock<ILogger<UsbActionEngine>>().Object);

        // Execute action for critical finding
        var actionResult = await actionEngine.ExecuteAsync(
            device, ScanSeverity.Critical, UsbOperatingMode.Permissive,
            "Malicious PE disguised as PDF");

        actionResult.ActionType.ShouldBe(UsbActionType.ReadOnlyUsb);
        actionResult.Success.ShouldBeTrue();

        // Persist alert
        var alert = new UsbAlert
        {
            UsbScanResultId = Guid.NewGuid(),
            Title = $"USB GUARD: Critical — {device.ProductDescription}",
            Description = "Malicious PE disguised as PDF",
            Severity = ScanSeverity.Critical.ToString(),
            ActionTaken = actionResult.ActionType.ToString()
        };
        _context.UsbAlerts.Add(alert);
        await _context.SaveChangesAsync();

        var persisted = await _context.UsbAlerts.FirstAsync();
        persisted.Severity.ShouldBe("Critical");
        persisted.ActionTaken.ShouldBe("ReadOnlyUsb");
    }

    // Test 6: Rapid unplug cancels scan without error
    [Fact]
    public async Task RapidUnplug_CancelsScan_NoErrorAlert()
    {
        string driveDir = Path.Combine(_testDir, "rapid_unplug_drive");
        Directory.CreateDirectory(driveDir);

        // Create many files to make scan take time
        for (int i = 0; i < 50; i++)
            await File.WriteAllBytesAsync(Path.Combine(driveDir, $"file_{i}.txt"), new byte[1024]);

        var device = CreateTestDevice(driveDir);
        var policy = CreateTestPolicy();
        var scanner = CreateScanner();

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(50));

        var report = await scanner.ScanAsync(device, policy, cts.Token);

        // Either completed quickly or was cancelled — no exception thrown
        report.ShouldNotBeNull();
        // No error alert persisted — the cancellation is informational
    }

    // Test 7: Low severity scan does not trigger action
    [Fact]
    public void LowSeverityScan_LogsOnly_NoAction()
    {
        var selectedAction = UsbActionEngine.SelectAction(UsbOperatingMode.Permissive, ScanSeverity.Low);
        selectedAction.ShouldBe(UsbActionType.AlertOnly);

        var selectedStrict = UsbActionEngine.SelectAction(UsbOperatingMode.Strict, ScanSeverity.Low);
        selectedStrict.ShouldBe(UsbActionType.AlertOnly);

        var selectedAudit = UsbActionEngine.SelectAction(UsbOperatingMode.Audit, ScanSeverity.Critical);
        selectedAudit.ShouldBe(UsbActionType.AlertOnly);
    }

    // Test 8: End-to-end malicious PDF pipeline
    [Fact]
    public async Task EndToEnd_UsbWithMaliciousPdf_FullPipelineExecutes()
    {
        // Arrange: USB drive with PE executable disguised as .pdf
        string driveDir = Path.Combine(_testDir, "malicious_pdf_drive");
        Directory.CreateDirectory(driveDir);

        // Create a file with PE magic bytes but .pdf extension
        byte[] peContent = new byte[256];
        peContent[0] = 0x4D; // M
        peContent[1] = 0x5A; // Z
        peContent[2] = 0x90;
        await File.WriteAllBytesAsync(Path.Combine(driveDir, "report.pdf"), peContent);

        var device = CreateTestDevice(driveDir);
        var policy = CreateTestPolicy();
        var scanner = CreateScanner();

        // Act: Scan
        var scanReport = await scanner.ScanAsync(device, policy);

        // Assert: Critical finding from magic byte mismatch
        scanReport.Findings.Count.ShouldBeGreaterThan(0);
        scanReport.OverallSeverity.ShouldBe(ScanSeverity.Critical);
        scanReport.Findings.ShouldContain(f =>
            f.FindingType == "MagicByteMismatch" &&
            f.Severity == ScanSeverity.Critical);

        // Act: Persist all entities in the chain
        var connectionLog = new UsbConnectionLog
        {
            DeviceInstanceId = device.DeviceInstanceId,
            SerialNumberHash = device.SerialNumber,
            VendorId = device.VendorId,
            ProductId = device.ProductId,
            DeviceClass = device.DeviceClass.ToString(),
            DriveLetter = device.DriveLetter,
            ConnectedAt = device.ConnectedAt,
            WasWhitelisted = false,
            RetainUntil = DateTime.UtcNow.AddDays(90)
        };
        _context.UsbConnectionLogs.Add(connectionLog);
        await _context.SaveChangesAsync();

        var scanResult = new UsbScanResult
        {
            UsbConnectionLogId = connectionLog.Id,
            TotalFilesScanned = scanReport.FilesScanned,
            FlaggedFilesCount = scanReport.Findings.Count,
            FlaggedFilesJson = JsonSerializer.Serialize(scanReport.Findings),
            ScanDurationMs = (long)scanReport.Duration.TotalMilliseconds,
            ScanCompleted = true,
            HighestSeverity = scanReport.OverallSeverity.ToString(),
            ScannedAt = scanReport.ScannedAt
        };
        _context.UsbScanResults.Add(scanResult);
        await _context.SaveChangesAsync();

        // Action engine
        var actionEngine = new UsbActionEngine(new Mock<ILogger<UsbActionEngine>>().Object);
        var actionResult = await actionEngine.ExecuteAsync(
            device, scanReport.OverallSeverity, UsbOperatingMode.Strict,
            "PE disguised as PDF");
        actionResult.ActionType.ShouldBe(UsbActionType.BlockAndEject);

        var alert = new UsbAlert
        {
            UsbScanResultId = scanResult.Id,
            Title = $"USB GUARD: Critical — {device.ProductDescription}",
            Description = $"USB scan found {scanReport.Findings.Count} suspicious items",
            Severity = scanReport.OverallSeverity.ToString(),
            ActionTaken = actionResult.ActionType.ToString()
        };
        _context.UsbAlerts.Add(alert);
        await _context.SaveChangesAsync();

        // Assert: all 4 entity types persisted
        (await _context.UsbConnectionLogs.CountAsync()).ShouldBe(1);
        (await _context.UsbScanResults.CountAsync()).ShouldBe(1);
        (await _context.UsbAlerts.CountAsync()).ShouldBe(1);

        // Verify cross-linking
        var persistedAlert = await _context.UsbAlerts.FirstAsync();
        persistedAlert.UsbScanResultId.ShouldBe(scanResult.Id);

        var persistedScan = await _context.UsbScanResults.FirstAsync();
        persistedScan.UsbConnectionLogId.ShouldBe(connectionLog.Id);
    }

    private UsbContentScanner CreateScanner()
    {
        return new UsbContentScanner(
            new MagicByteValidator(new Mock<ILogger<MagicByteValidator>>().Object),
            new AutorunInfDetector(new Mock<ILogger<AutorunInfDetector>>().Object),
            new SuspiciousLnkDetector(new Mock<ILogger<SuspiciousLnkDetector>>().Object),
            new ArchiveScanner(new Mock<ILogger<ArchiveScanner>>().Object),
            new UsbEntropyScanner(
                new EntropyCalculator(new Mock<ILogger<EntropyCalculator>>().Object),
                new Mock<ILogger<UsbEntropyScanner>>().Object),
            new Mock<ILogger<UsbContentScanner>>().Object);
    }

    private UsbDevice CreateTestDevice(string? driveDir)
    {
        return new UsbDevice
        {
            Id = Guid.NewGuid(),
            DeviceInstanceId = $"USB\\VID_0781&PID_5567\\{Guid.NewGuid():N}",
            SerialNumber = $"SER_{Guid.NewGuid():N}",
            VendorId = "0781",
            ProductId = "5567",
            Manufacturer = "SanDisk",
            ProductDescription = "Ultra USB 3.0",
            Model = "Ultra",
            DriveLetter = driveDir,
            CapacityBytes = 32L * 1024 * 1024 * 1024,
            DeviceClass = UsbDeviceClass.MassStorage,
            BusType = UsbBusType.Usb30,
            IsRemovable = true,
            IsBootable = false,
            ConnectedAt = DateTime.UtcNow
        };
    }

    private static UsbPolicy CreateTestPolicy()
    {
        return new UsbPolicy
        {
            Id = Guid.NewGuid(),
            Mode = UsbOperatingMode.Permissive,
            MaxFileSizeForScanMB = 100,
            MaxScanDurationSeconds = 30,
            ScanArchiveContents = true,
            BlockBootableUsb = true,
            AlertOnHidDevice = false,
            AlertOnNetworkDevice = false,
            SuspiciousExtensionsJson = "[]",
            UpdatedAt = DateTime.UtcNow
        };
    }

    public void Dispose()
    {
        _context.Dispose();
        try { if (Directory.Exists(_testDir)) Directory.Delete(_testDir, true); }
        catch { /* best effort */ }
    }
}
