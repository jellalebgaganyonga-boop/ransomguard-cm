using Microsoft.Extensions.Logging;
using Moq;
using RansomGuard.Agent.Core.Detection.UsbGuard.Actions;
using RansomGuard.Agent.Core.Detection.UsbGuard.Models;
using RansomGuard.Agent.Core.Detection.UsbGuard.Quarantine;
using RansomGuard.Agent.Core.Detection.UsbGuard.Scanning;
using RansomGuard.Agent.Core.Persistence.Entities;
using RansomGuard.Agent.Core.Persistence.Repositories;
using Shouldly;

namespace RansomGuard.Agent.Tests.Detection.UsbGuard;

/// <summary>
/// The USB actions, real (AGT-USB-001, after V7): read-only, eject, quarantine and the honest
/// BlockAndEject. The device calls are a fake here (a runner has no USB key); what is checked is
/// that the engine asks the system for the right thing, reports exactly what came back, and
/// never claims an action that did not happen.
/// </summary>
public sealed class UsbRealActionsTests
{
    private readonly Mock<IAuditLogRepository> _audit = new();
    private readonly Mock<IQuarantineService> _quarantine = new();
    private readonly FakeUsbDeviceControl _control = new();

    private UsbActionEngine Engine(IUsbDeviceControl? control = null, IQuarantineService? quarantine = null) =>
        new(new Mock<ILogger<UsbActionEngine>>().Object, _audit.Object, ironCladEngine: null,
            control: control, quarantine: quarantine);

    // ── Read-only (Permissive + Critical) ──────────────────────────────────

    [Fact]
    public async Task ReadOnly_asks_the_system_for_the_drive_and_reports_done()
    {
        var engine = Engine(_control, _quarantine.Object);

        var result = await engine.ExecuteAsync(Device("E"), ScanSeverity.Critical, UsbOperatingMode.Permissive, "payload.exe");

        result.ActionType.ShouldBe(UsbActionType.ReadOnlyUsb);
        result.Success.ShouldBeTrue();
        result.ReasonCode.ShouldBeNull();
        _control.ReadOnlyCalls.ShouldBe(["E:"]);
        VerifyAudited("action=ReadOnlyUsb outcome=executed");
    }

    [Fact]
    public async Task ReadOnly_failure_is_reported_with_the_system_error()
    {
        _control.ReadOnlyAnswer = UsbControlResult.Failed("set read-only on PhysicalDrive2: Access is denied (0x5)");
        var engine = Engine(_control, _quarantine.Object);

        var result = await engine.ExecuteAsync(Device("E"), ScanSeverity.Critical, UsbOperatingMode.Permissive, "payload.exe");

        result.Success.ShouldBeFalse();
        result.ReasonCode.ShouldBe(UsbActionReasonCode.DeviceControlFailed);
        result.Description.ShouldContain("Access is denied");
        VerifyAudited("action=ReadOnlyUsb outcome=device_control_failed");
    }

    [Fact]
    public async Task ReadOnly_without_a_drive_letter_is_not_attempted()
    {
        var engine = Engine(_control, _quarantine.Object);

        var result = await engine.ExecuteAsync(Device(null), ScanSeverity.Critical, UsbOperatingMode.Permissive, "payload.exe");

        result.Success.ShouldBeFalse();
        result.ReasonCode.ShouldBe(UsbActionReasonCode.NoDriveLetter);
        _control.ReadOnlyCalls.ShouldBeEmpty();
    }

    [Fact]
    public async Task ReadOnly_without_device_control_wired_says_so()
    {
        var engine = Engine(control: null, _quarantine.Object);

        var result = await engine.ExecuteAsync(Device("E"), ScanSeverity.Critical, UsbOperatingMode.Permissive, "payload.exe");

        result.Success.ShouldBeFalse();
        result.ReasonCode.ShouldBe(UsbActionReasonCode.NoDeviceControl);
        VerifyAudited("action=ReadOnlyUsb outcome=no_device_control");
    }

    [Fact]
    public async Task A_device_call_that_hangs_is_reported_failed_after_the_timeout()
    {
        _control.Hang = true;
        var engine = Engine(_control, _quarantine.Object);
        var watch = System.Diagnostics.Stopwatch.StartNew();

        var result = await engine.ExecuteAsync(Device("E"), ScanSeverity.Critical, UsbOperatingMode.Permissive, "payload.exe");

        watch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(20));
        result.Success.ShouldBeFalse();
        result.ReasonCode.ShouldBe(UsbActionReasonCode.DeviceControlFailed);
        result.Description.ShouldContain("no answer");
    }

    // ── Eject (Strict + High / Medium) ─────────────────────────────────────

    [Theory]
    [InlineData(ScanSeverity.High)]
    [InlineData(ScanSeverity.Medium)]
    public async Task Eject_asks_the_system_for_the_device_instance_and_reports_done(ScanSeverity severity)
    {
        var engine = Engine(_control, _quarantine.Object);

        var result = await engine.ExecuteAsync(Device("F"), severity, UsbOperatingMode.Strict, "autorun.inf");

        result.ActionType.ShouldBe(UsbActionType.EjectUsb);
        result.Success.ShouldBeTrue();
        _control.EjectCalls.ShouldBe([@"USBSTOR\DISK&VEN_TEST\1234"]);
        VerifyAudited("action=EjectUsb outcome=executed");
    }

    [Fact]
    public async Task Eject_vetoed_by_the_system_is_reported_as_such()
    {
        _control.EjectAnswer = UsbControlResult.Failed("CM_Request_Device_Eject: CR 0x17, veto type 6 explorer.exe");
        var engine = Engine(_control, _quarantine.Object);

        var result = await engine.ExecuteAsync(Device("F"), ScanSeverity.High, UsbOperatingMode.Strict, "autorun.inf");

        result.Success.ShouldBeFalse();
        result.ReasonCode.ShouldBe(UsbActionReasonCode.DeviceControlFailed);
        result.Description.ShouldContain("veto");
    }

    // ── BlockAndEject (Strict + Critical, no IRONCLAD) ─────────────────────

    [Fact]
    public async Task BlockAndEject_ejects_for_real_and_says_the_block_does_not_exist()
    {
        var engine = Engine(_control, _quarantine.Object);

        var result = await engine.ExecuteAsync(Device("E"), ScanSeverity.Critical, UsbOperatingMode.Strict, "ransomware.exe");

        result.ActionType.ShouldBe(UsbActionType.BlockAndEject);
        result.Success.ShouldBeFalse(); // the block did not happen: never "done" as a whole
        result.ReasonCode.ShouldBe(UsbActionReasonCode.BlockNotImplemented);
        result.FallbackAction.ShouldBe(UsbActionType.EjectUsb);
        _control.EjectCalls.Count.ShouldBe(1);
        VerifyAudited("action=BlockAndEject outcome=block_not_implemented");
    }

    [Fact]
    public async Task BlockAndEject_whose_eject_fails_reports_the_eject_failure()
    {
        _control.EjectAnswer = UsbControlResult.Failed("CM_Locate_DevNode: CR 0xD");
        var engine = Engine(_control, _quarantine.Object);

        var result = await engine.ExecuteAsync(Device("E"), ScanSeverity.Critical, UsbOperatingMode.Strict, "ransomware.exe");

        result.ActionType.ShouldBe(UsbActionType.BlockAndEject);
        result.ReasonCode.ShouldBe(UsbActionReasonCode.DeviceControlFailed);
    }

    // ── Quarantine (Permissive + High) ─────────────────────────────────────

    [Fact]
    public async Task Quarantine_takes_the_files_flagged_High_or_above_off_the_key()
    {
        var engine = Engine(_control, _quarantine.Object);
        _quarantine.Setup(q => q.QuarantineAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<QuarantineSeverity>(), "1234", It.IsAny<CancellationToken>()))
            .ReturnsAsync((string path, string _, QuarantineSeverity _, string _, CancellationToken _) =>
                Quarantined(path));
        var findings = new[]
        {
            Finding(@"E:\invoice.pdf.exe", ScanSeverity.High),
            Finding(@"E:\notes.txt", ScanSeverity.Low),
            Finding(@"E:\INVOICE.PDF.EXE", ScanSeverity.High), // the same file, flagged twice
            Finding(@"E:\autorun.inf", ScanSeverity.Critical),
        };

        var result = await engine.ExecuteAsync(Device("E"), ScanSeverity.High, UsbOperatingMode.Permissive, "2 suspicious items", findings);

        result.ActionType.ShouldBe(UsbActionType.QuarantineFile);
        result.Success.ShouldBeTrue();
        _quarantine.Verify(q => q.QuarantineAsync(@"E:\invoice.pdf.exe", It.IsAny<string>(), QuarantineSeverity.High, "1234", It.IsAny<CancellationToken>()), Times.Once);
        _quarantine.Verify(q => q.QuarantineAsync(@"E:\autorun.inf", It.IsAny<string>(), QuarantineSeverity.Critical, "1234", It.IsAny<CancellationToken>()), Times.Once);
        _quarantine.Verify(q => q.QuarantineAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<QuarantineSeverity>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        VerifyAudited("action=QuarantineFile outcome=executed");
    }

    [Fact]
    public async Task Quarantine_with_no_flagged_file_does_nothing_and_says_so()
    {
        var engine = Engine(_control, _quarantine.Object);

        var result = await engine.ExecuteAsync(Device("E"), ScanSeverity.High, UsbOperatingMode.Permissive, "2 suspicious items", [Finding(@"E:\notes.txt", ScanSeverity.Low)]);

        result.Success.ShouldBeFalse();
        result.ReasonCode.ShouldBe(UsbActionReasonCode.NoFileToQuarantine);
        _quarantine.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Quarantine_reports_partial_failure_file_by_file()
    {
        var engine = Engine(_control, _quarantine.Object);
        _quarantine.Setup(q => q.QuarantineAsync(@"E:\a.exe", It.IsAny<string>(), It.IsAny<QuarantineSeverity>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Quarantined(@"E:\a.exe"));
        _quarantine.Setup(q => q.QuarantineAsync(@"E:\b.exe", It.IsAny<string>(), It.IsAny<QuarantineSeverity>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("The device is not ready"));

        var result = await engine.ExecuteAsync(Device("E"), ScanSeverity.High, UsbOperatingMode.Permissive, "x",
            [Finding(@"E:\a.exe", ScanSeverity.High), Finding(@"E:\b.exe", ScanSeverity.High)]);

        result.Success.ShouldBeFalse();
        result.ReasonCode.ShouldBe(UsbActionReasonCode.QuarantineFailed);
        result.Description.ShouldContain("1 quarantined, 1 not");
        result.Description.ShouldContain(@"E:\b.exe");
    }

    [Fact]
    public async Task Quarantine_without_the_service_wired_says_so()
    {
        var engine = Engine(_control, quarantine: null);

        var result = await engine.ExecuteAsync(Device("E"), ScanSeverity.High, UsbOperatingMode.Permissive, "x", [Finding(@"E:\a.exe", ScanSeverity.High)]);

        result.ReasonCode.ShouldBe(UsbActionReasonCode.NoQuarantineService);
    }

    [Fact]
    public async Task The_overload_without_findings_cannot_quarantine_and_says_so()
    {
        var engine = Engine(_control, _quarantine.Object);

        var result = await engine.ExecuteAsync(Device("E"), ScanSeverity.High, UsbOperatingMode.Permissive, "x");

        result.ActionType.ShouldBe(UsbActionType.QuarantineFile);
        result.ReasonCode.ShouldBe(UsbActionReasonCode.NoFileToQuarantine);
    }

    // ── Nothing is claimed that did not happen ─────────────────────────────

    [Fact]
    public async Task Alert_only_still_touches_nothing()
    {
        var engine = Engine(_control, _quarantine.Object);

        var result = await engine.ExecuteAsync(Device("E"), ScanSeverity.Critical, UsbOperatingMode.Audit, "x");

        result.ActionType.ShouldBe(UsbActionType.AlertOnly);
        result.Success.ShouldBeTrue();
        _control.ReadOnlyCalls.ShouldBeEmpty();
        _control.EjectCalls.ShouldBeEmpty();
        _quarantine.VerifyNoOtherCalls();
    }

    // ── helpers ────────────────────────────────────────────────────────────

    private void VerifyAudited(string fragment) =>
        _audit.Verify(a => a.AppendAsync("UsbAction", It.Is<string>(d => d.Contains(fragment)), "UsbDevice",
            It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Once);

    private static QuarantinedFile Quarantined(string path) => new()
    {
        Id = Guid.NewGuid(),
        OriginalPath = path,
        QuarantinePath = "q",
        OriginalSha256 = "h",
        QuarantineSha256 = "h",
        OriginalSize = 1,
        QuarantineReason = "r",
        Severity = QuarantineSeverity.High,
        SourceUsbSerial = "1234",
        QuarantinedAt = DateTime.UtcNow,
        QuarantinedByUser = "SYSTEM",
        RetainUntil = DateTime.UtcNow.AddDays(30),
        IsEncrypted = true,
    };

    private static UsbFileFinding Finding(string path, ScanSeverity severity) => new()
    {
        FilePath = path,
        DetectorName = "DoubleExtensionDetector",
        FindingType = "DoubleExtension",
        Description = "looks like an executable disguised as a document",
        Severity = severity,
    };

    private static UsbDevice Device(string? driveLetter) => new()
    {
        Id = Guid.NewGuid(),
        DeviceInstanceId = @"USBSTOR\DISK&VEN_TEST\1234",
        SerialNumber = "1234",
        VendorId = "0781",
        ProductId = "5567",
        Manufacturer = "Test",
        ProductDescription = "Test key",
        Model = "Cruzer",
        DriveLetter = driveLetter is null ? null : driveLetter + ":",
        CapacityBytes = 16L * 1024 * 1024 * 1024,
        DeviceClass = UsbDeviceClass.MassStorage,
        BusType = UsbBusType.Usb30,
        IsRemovable = true,
        IsBootable = false,
        ConnectedAt = DateTime.UtcNow,
    };

    /// <summary>A device control that records what it was asked and answers what the test set.</summary>
    private sealed class FakeUsbDeviceControl : IUsbDeviceControl
    {
        public List<string> ReadOnlyCalls { get; } = [];
        public List<string> EjectCalls { get; } = [];
        public UsbControlResult ReadOnlyAnswer { get; set; } = UsbControlResult.Done;
        public UsbControlResult EjectAnswer { get; set; } = UsbControlResult.Done;
        public bool Hang { get; set; }

        public UsbControlResult SetReadOnly(string driveLetter)
        {
            ReadOnlyCalls.Add(driveLetter);
            if (Hang) Thread.Sleep(TimeSpan.FromSeconds(30));
            return ReadOnlyAnswer;
        }

        public UsbControlResult Eject(string deviceInstanceId)
        {
            EjectCalls.Add(deviceInstanceId);
            if (Hang) Thread.Sleep(TimeSpan.FromSeconds(30));
            return EjectAnswer;
        }
    }
}
