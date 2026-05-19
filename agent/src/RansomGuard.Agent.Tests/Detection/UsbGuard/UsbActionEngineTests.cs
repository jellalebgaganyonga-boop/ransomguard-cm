using Microsoft.Extensions.Logging;
using Moq;
using RansomGuard.Agent.Core.Detection.UsbGuard.Actions;
using RansomGuard.Agent.Core.Detection.UsbGuard.Models;
using RansomGuard.Agent.Core.Detection.UsbGuard.Scanning;
using RansomGuard.Agent.Core.Persistence.Entities;
using Shouldly;

namespace RansomGuard.Agent.Tests.Detection.UsbGuard;

/// <summary>
/// Tests for <see cref="UsbActionEngine"/> — decision logic, actions, fallbacks, timeout.
/// </summary>
public sealed class UsbActionEngineTests
{
    private readonly UsbActionEngine _engine;

    public UsbActionEngineTests()
    {
        _engine = new UsbActionEngine(new Mock<ILogger<UsbActionEngine>>().Object);
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
    public async Task AlertOnly_Always_Succeeds()
    {
        var device = CreateDevice();
        var result = await _engine.ExecuteAsync(device, ScanSeverity.Low, UsbOperatingMode.Audit, "Test alert");

        result.ActionType.ShouldBe(UsbActionType.AlertOnly);
        result.Success.ShouldBeTrue();
        result.Description.ShouldContain("Alert");
    }

    [Fact]
    public async Task Eject_Action_Executed_In_Strict_Mode()
    {
        var device = CreateDevice();
        var result = await _engine.ExecuteAsync(device, ScanSeverity.Medium, UsbOperatingMode.Strict, "Suspicious file");

        result.ActionType.ShouldBe(UsbActionType.EjectUsb);
        result.Success.ShouldBeTrue();
    }

    [Fact]
    public async Task ReadOnly_Action_Executed_In_Permissive_Critical()
    {
        var device = CreateDevice();
        var result = await _engine.ExecuteAsync(device, ScanSeverity.Critical, UsbOperatingMode.Permissive, "PE disguised as PDF");

        result.ActionType.ShouldBe(UsbActionType.ReadOnlyUsb);
        result.Success.ShouldBeTrue();
    }

    [Fact]
    public async Task BlockAndEject_In_Strict_Critical()
    {
        var device = CreateDevice();
        var result = await _engine.ExecuteAsync(device, ScanSeverity.Critical, UsbOperatingMode.Strict, "Bootable USB with malware");

        result.ActionType.ShouldBe(UsbActionType.BlockAndEject);
        result.Success.ShouldBeTrue();
    }

    [Fact]
    public async Task Quarantine_Action_In_Permissive_High()
    {
        var device = CreateDevice();
        var result = await _engine.ExecuteAsync(device, ScanSeverity.High, UsbOperatingMode.Permissive, "Exe in archive");

        result.ActionType.ShouldBe(UsbActionType.QuarantineFile);
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
        results.All(r => r.Success).ShouldBeTrue();
    }

    [Fact]
    public void Action_Timeout_10_Seconds_Configured()
    {
        // Verify the action engine has a 10-second timeout per the spec.
        // Since actual actions use Task.CompletedTask (P/Invoke not wired in test),
        // verify the timeout configuration is correct via the constant.
        var engineType = typeof(UsbActionEngine);
        var field = engineType.GetField("ActionTimeout",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        field.ShouldNotBeNull();
        var timeout = (TimeSpan)field.GetValue(null)!;
        timeout.TotalSeconds.ShouldBe(10);
    }

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
