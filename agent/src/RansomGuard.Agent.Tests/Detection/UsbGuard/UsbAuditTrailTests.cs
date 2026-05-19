using Microsoft.EntityFrameworkCore;
using RansomGuard.Agent.Core.Persistence;
using RansomGuard.Agent.Core.Persistence.Entities;
using Shouldly;

namespace RansomGuard.Agent.Tests.Detection.UsbGuard;

/// <summary>
/// Tests for USB audit trail entities: UsbConnectionLog, UsbScanResult, UsbAlert.
/// Verifies persistence, cross-linking, and 90-day retention.
/// </summary>
public sealed class UsbAuditTrailTests : IDisposable
{
    private readonly AgentDbContext _context;

    public UsbAuditTrailTests()
    {
        var options = new DbContextOptionsBuilder<AgentDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        _context = new AgentDbContext(options);
        _context.Database.OpenConnection();
        _context.Database.EnsureCreated();
    }

    [Fact]
    public async Task Connection_Log_Persisted_On_Plug()
    {
        var log = new UsbConnectionLog
        {
            DeviceInstanceId = "USB\\VID_0781&PID_5567\\ABC123",
            SerialNumberHash = "hashed_serial",
            VendorId = "0781",
            ProductId = "5567",
            DeviceClass = "MassStorage",
            DriveLetter = "E:",
            ConnectedAt = DateTime.UtcNow,
            WasWhitelisted = false,
            RetainUntil = DateTime.UtcNow.AddDays(90)
        };

        _context.UsbConnectionLogs.Add(log);
        await _context.SaveChangesAsync();

        var persisted = await _context.UsbConnectionLogs.FirstAsync();
        persisted.DeviceInstanceId.ShouldBe("USB\\VID_0781&PID_5567\\ABC123");
        persisted.WasWhitelisted.ShouldBeFalse();
    }

    [Fact]
    public async Task Disconnect_Timestamp_Updated_On_Unplug()
    {
        var log = new UsbConnectionLog
        {
            DeviceInstanceId = "USB\\TEST",
            SerialNumberHash = "hash",
            VendorId = "0781",
            ProductId = "5567",
            DeviceClass = "MassStorage",
            ConnectedAt = DateTime.UtcNow,
            WasWhitelisted = true,
            RetainUntil = DateTime.UtcNow.AddDays(90)
        };

        _context.UsbConnectionLogs.Add(log);
        await _context.SaveChangesAsync();

        log.DisconnectedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        var updated = await _context.UsbConnectionLogs.FirstAsync();
        updated.DisconnectedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task Scan_Result_Persists_All_Flagged_Files()
    {
        var log = CreateConnectionLog();
        _context.UsbConnectionLogs.Add(log);
        await _context.SaveChangesAsync();

        var scan = new UsbScanResult
        {
            UsbConnectionLogId = log.Id,
            TotalFilesScanned = 500,
            FlaggedFilesCount = 3,
            FlaggedFilesJson = "[{\"file\":\"malware.exe\",\"severity\":\"Critical\"}]",
            ScanDurationMs = 15000,
            ScanCompleted = true,
            HighestSeverity = "Critical",
            ScannedAt = DateTime.UtcNow
        };

        _context.UsbScanResults.Add(scan);
        await _context.SaveChangesAsync();

        var persisted = await _context.UsbScanResults.FirstAsync();
        persisted.UsbConnectionLogId.ShouldBe(log.Id);
        persisted.FlaggedFilesCount.ShouldBe(3);
        persisted.HighestSeverity.ShouldBe("Critical");
    }

    [Fact]
    public async Task Alert_Cross_Linked_With_Scan_And_Genealogy()
    {
        var log = CreateConnectionLog();
        _context.UsbConnectionLogs.Add(log);

        var scan = new UsbScanResult
        {
            UsbConnectionLogId = log.Id,
            TotalFilesScanned = 100,
            FlaggedFilesCount = 1,
            FlaggedFilesJson = "[]",
            ScanDurationMs = 5000,
            ScanCompleted = true,
            HighestSeverity = "High",
            ScannedAt = DateTime.UtcNow
        };
        _context.UsbScanResults.Add(scan);

        var genealogyId = Guid.NewGuid();
        var alert = new UsbAlert
        {
            UsbScanResultId = scan.Id,
            AlertId = Guid.NewGuid(),
            Title = "PE executable disguised as PDF",
            Description = "Extension mismatch: .pdf contains MZ header",
            Severity = "Critical",
            ActionTaken = "QuarantineFile",
            GenealogyId = genealogyId
        };
        _context.UsbAlerts.Add(alert);
        await _context.SaveChangesAsync();

        var persisted = await _context.UsbAlerts.FirstAsync();
        persisted.UsbScanResultId.ShouldBe(scan.Id);
        persisted.GenealogyId.ShouldBe(genealogyId);
        persisted.ActionTaken.ShouldBe("QuarantineFile");
    }

    [Fact]
    public async Task Retention_90_Days_Set_Correctly()
    {
        var log = CreateConnectionLog();
        _context.UsbConnectionLogs.Add(log);
        await _context.SaveChangesAsync();

        var persisted = await _context.UsbConnectionLogs.FirstAsync();
        (persisted.RetainUntil - persisted.ConnectedAt).TotalDays.ShouldBeInRange(89, 91);
    }

    [Fact]
    public async Task Full_Audit_Chain_ConnectionLog_To_ScanResult_To_Alert()
    {
        // Create full chain: Connection -> Scan -> Alert
        var log = CreateConnectionLog();
        _context.UsbConnectionLogs.Add(log);

        var scan = new UsbScanResult
        {
            UsbConnectionLogId = log.Id,
            TotalFilesScanned = 200,
            FlaggedFilesCount = 2,
            FlaggedFilesJson = "[{\"file\":\"autorun.inf\"},{\"file\":\"exploit.lnk\"}]",
            ScanDurationMs = 8000,
            ScanCompleted = true,
            HighestSeverity = "Critical",
            ScannedAt = DateTime.UtcNow
        };
        _context.UsbScanResults.Add(scan);

        var alert = new UsbAlert
        {
            UsbScanResultId = scan.Id,
            Title = "Autorun malware detected",
            Description = "autorun.inf with open= directive",
            Severity = "Critical",
            ActionTaken = "EjectUsb"
        };
        _context.UsbAlerts.Add(alert);
        await _context.SaveChangesAsync();

        // Verify full chain
        int logs = await _context.UsbConnectionLogs.CountAsync();
        int scans = await _context.UsbScanResults.CountAsync();
        int alerts = await _context.UsbAlerts.CountAsync();

        logs.ShouldBe(1);
        scans.ShouldBe(1);
        alerts.ShouldBe(1);

        var loadedAlert = await _context.UsbAlerts.FirstAsync();
        var loadedScan = await _context.UsbScanResults.FirstAsync(s => s.Id == loadedAlert.UsbScanResultId);
        var loadedLog = await _context.UsbConnectionLogs.FirstAsync(l => l.Id == loadedScan.UsbConnectionLogId);

        loadedLog.DeviceClass.ShouldBe("MassStorage");
    }

    private static UsbConnectionLog CreateConnectionLog() => new()
    {
        DeviceInstanceId = $"USB\\VID_0781&PID_5567\\{Guid.NewGuid():N}",
        SerialNumberHash = "hash_" + Guid.NewGuid().ToString("N")[..8],
        VendorId = "0781",
        ProductId = "5567",
        DeviceClass = "MassStorage",
        DriveLetter = "E:",
        ConnectedAt = DateTime.UtcNow,
        WasWhitelisted = false,
        RetainUntil = DateTime.UtcNow.AddDays(90)
    };

    public void Dispose()
    {
        _context.Database.CloseConnection();
        _context.Dispose();
    }
}
