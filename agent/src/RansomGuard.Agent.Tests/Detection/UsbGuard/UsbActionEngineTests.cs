using Microsoft.Extensions.Logging;
using Moq;
using RansomGuard.Agent.Core.Detection.UsbGuard.Actions;
using RansomGuard.Agent.Core.Detection.UsbGuard.Models;
using RansomGuard.Agent.Core.Detection.UsbGuard.Scanning;
using RansomGuard.Agent.Core.Persistence.Entities;
using RansomGuard.Agent.Core.Persistence.Repositories;
using Shouldly;

namespace RansomGuard.Agent.Tests.Detection.UsbGuard;

/// <summary>
/// Tests for <see cref="UsbActionEngine"/> — decision logic, actions, fallbacks, timeout.
/// </summary>
public sealed class UsbActionEngineTests
{
    private readonly Mock<IAuditLogRepository> _audit = new();
    private readonly UsbActionEngine _engine;

    public UsbActionEngineTests()
    {
        _engine = new UsbActionEngine(new Mock<ILogger<UsbActionEngine>>().Object, _audit.Object);
    }

    [Theory]
    [InlineData(UsbOperatingMode.Audit, ScanSeverity.Critical, UsbActionType.AlertOnly)]
    [InlineData(UsbOperatingMode.Audit, ScanSeverity.High, UsbActionType.AlertOnly)]
    [InlineData(UsbOperatingMode.Permissive, ScanSeverity.Critical, UsbActionType.ReadOnlyUsb)]
    [InlineData(UsbOperatingMode.Permissive, ScanSeverity.High, UsbActionType.QuarantineFile)]
    [InlineData(UsbOperatingMode.Permissive, ScanSeverity.Low, UsbActionType.AlertOnly)]
    [InlineData(UsbOperatingMode.Strict, ScanSeverity.Critical, UsbActionType.BlockAndEject)]
    [InlineData(UsbOperatingMode.Strict, ScanSeverity.Medium, UsbActionType.EjectUsb)]
    [InlineData(UsbOperatingMode.Strict, ScanSeverity.Low, UsbActionType.AlertOnly)]
    public void Action_Selection_Logic_Respects_Mode_And_Severity(
        UsbOperatingMode mode, ScanSeverity severity, UsbActionType expected)
    {
        UsbActionType result = UsbActionEngine.SelectAction(mode, severity);
        result.ShouldBe(expected);
    }

    [Fact]
    public async Task AlertOnly_Succeeds_And_Is_Audited_As_Executed()
    {
        var device = CreateDevice();
        var result = await _engine.ExecuteAsync(device, ScanSeverity.Low, UsbOperatingMode.Audit, "Test alert");

        result.ActionType.ShouldBe(UsbActionType.AlertOnly);
        result.Success.ShouldBeTrue();
        result.ReasonCode.ShouldBeNull();
        result.Description.ShouldContain("Alert");
        VerifyAudited("action=AlertOnly outcome=executed");
    }

    // ReadOnly, Quarantine, Eject and software BlockAndEject do nothing to the device:
    // the result and the signed audit entry must both say so, never claim the action.
    [Theory]
    [InlineData(UsbOperatingMode.Permissive, ScanSeverity.Critical, UsbActionType.ReadOnlyUsb)]
    [InlineData(UsbOperatingMode.Permissive, ScanSeverity.High, UsbActionType.QuarantineFile)]
    [InlineData(UsbOperatingMode.Strict, ScanSeverity.Medium, UsbActionType.EjectUsb)]
    [InlineData(UsbOperatingMode.Strict, ScanSeverity.High, UsbActionType.EjectUsb)]
    [InlineData(UsbOperatingMode.Strict, ScanSeverity.Critical, UsbActionType.BlockAndEject)]
    public async Task Unimplemented_Action_Reports_Not_Implemented_In_Result_And_Audit(
        UsbOperatingMode mode, ScanSeverity severity, UsbActionType expected)
    {
        var result = await _engine.ExecuteAsync(CreateDevice(), severity, mode, "Suspicious content");

        result.ActionType.ShouldBe(expected);
        result.Success.ShouldBeFalse();
        result.ReasonCode.ShouldBe(UsbActionReasonCode.NotImplemented);
        result.Description.ShouldContain("NOT executed");
        VerifyAudited($"action={expected} outcome=not_implemented");
    }

    [Fact]
    public async Task Audit_Failure_Does_Not_Lose_The_Result()
    {
        _audit.Setup(a => a.AppendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(),
                It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("audit store unavailable"));

        var result = await _engine.ExecuteAsync(CreateDevice(), ScanSeverity.Low, UsbOperatingMode.Audit, "Test alert");

        result.ActionType.ShouldBe(UsbActionType.AlertOnly);
        result.Success.ShouldBeTrue();
    }

    [Fact]
    public async Task Concurrent_Usb_Events_Handled_Independently()
    {
        var tasks = Enumerable.Range(0, 10).Select(i =>
        {
            var device = CreateDevice($"Device_{i}");
            return _engine.ExecuteAsync(device, ScanSeverity.High, UsbOperatingMode.Permissive, $"Event {i}");
        });

        var results = await Task.WhenAll(tasks);
        results.Length.ShouldBe(10);
        results.All(r => !r.Success && r.ReasonCode == UsbActionReasonCode.NotImplemented).ShouldBeTrue();
        _audit.Verify(a => a.AppendAsync("UsbAction", It.IsAny<string>(), "UsbDevice",
            It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Exactly(10));
    }

    [Fact]
    public void Action_Timeout_10_Seconds_Configured()
    {
        // Verify the action engine has a 10-second timeout per the spec. It bounds the
        // IronClad port cut, the only USB action that is carried out today.
        var engineType = typeof(UsbActionEngine);
        var field = engineType.GetField("ActionTimeout",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        field.ShouldNotBeNull();
        var timeout = (TimeSpan)field.GetValue(null)!;
        timeout.TotalSeconds.ShouldBe(10);
    }

    private void VerifyAudited(string expectedFragment) =>
        _audit.Verify(a => a.AppendAsync(
                "UsbAction",
                It.Is<string>(d => d.Contains(expectedFragment)),
                "UsbDevice",
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);

    private static UsbDevice CreateDevice(string name = "TestDevice") => new()
    {
        Id = Guid.NewGuid(),
        DeviceInstanceId = $"USB\\VID_0781&PID_5567\\{Guid.NewGuid():N}",
        SerialNumber = "TEST_SERIAL",
        VendorId = "0781",
        ProductId = "5567",
        Manufacturer = "Test",
        ProductDescription = name,
        Model = name,
        DriveLetter = "E:",
        CapacityBytes = 16L * 1024 * 1024 * 1024,
        DeviceClass = UsbDeviceClass.MassStorage,
        BusType = UsbBusType.Usb30,
        IsRemovable = true,
        IsBootable = false,
        ConnectedAt = DateTime.UtcNow
    };
}
