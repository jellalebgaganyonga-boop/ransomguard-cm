using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RansomGuard.Agent.Core.Detection.IronClad.Actions;
using RansomGuard.Agent.Core.Detection.IronClad.Communication;
using RansomGuard.Agent.Core.Detection.IronClad.Models;
using RansomGuard.Agent.Core.Detection.UsbGuard.Actions;
using RansomGuard.Agent.Core.Detection.UsbGuard.Models;
using RansomGuard.Agent.Core.Detection.UsbGuard.Scanning;
using RansomGuard.Agent.Core.Persistence.Entities;

namespace RansomGuard.Agent.Tests.Integration.IronClad;

public sealed class UsbGuardIronCladIntegrationTests
{
    private static readonly ILogger<UsbActionEngine> Logger = NullLogger<UsbActionEngine>.Instance;

    private static UsbDevice MakeDevice(string driveLetter = "E") => new()
    {
        Id = Guid.NewGuid(),
        DeviceInstanceId = "USB\\VID_1234&PID_5678\\SN001",
        SerialNumber = "SN001",
        VendorId = "1234",
        ProductId = "5678",
        Manufacturer = "TestCorp",
        Model = "TestDrive",
        DeviceClass = UsbDeviceClass.MassStorage,
        BusType = UsbBusType.Usb20,
        ProductDescription = "Test USB Drive",
        DriveLetter = driveLetter,
        CapacityBytes = 1_000_000_000,
        IsRemovable = true,
        IsBootable = false,
        ConnectedAt = DateTime.UtcNow
    };

    [Fact]
    public async Task UsbCritical_With_IronClad_Available_Triggers_CutUsbPort()
    {
        var ironClad = new FakeIronCladEngine(available: true, cutSuccess: true);
        var engine = new UsbActionEngine(Logger, ironClad);

        var result = await engine.ExecuteAsync(
            MakeDevice("E"), ScanSeverity.Critical, UsbOperatingMode.Strict, "malware detected");

        Assert.True(result.Success);
        Assert.Contains("IronClad", result.Description);
        Assert.Equal(1, ironClad.CutCallCount);
        Assert.Equal(1, ironClad.LastPortCut);
    }

    [Fact]
    public async Task UsbCritical_With_IronClad_Unavailable_Falls_Back_To_Software()
    {
        var ironClad = new FakeIronCladEngine(available: false, cutSuccess: false);
        var engine = new UsbActionEngine(Logger, ironClad);

        var result = await engine.ExecuteAsync(
            MakeDevice("E"), ScanSeverity.Critical, UsbOperatingMode.Strict, "malware detected");

        Assert.True(result.Success);
        Assert.Equal(0, ironClad.CutCallCount); // IronClad not called because !IsAvailable
        Assert.Equal(UsbActionType.BlockAndEject, result.ActionType);
    }

    [Fact]
    public async Task UsbCritical_With_IronClad_Disabled_Uses_Software_Action()
    {
        // No IronClad injected at all
        var engine = new UsbActionEngine(Logger, ironCladEngine: null);

        var result = await engine.ExecuteAsync(
            MakeDevice("E"), ScanSeverity.Critical, UsbOperatingMode.Strict, "malware detected");

        Assert.True(result.Success);
        Assert.Equal(UsbActionType.BlockAndEject, result.ActionType);
    }

    [Fact]
    public async Task UsbHigh_Severity_Does_Not_Trigger_IronClad()
    {
        var ironClad = new FakeIronCladEngine(available: true, cutSuccess: true);
        var engine = new UsbActionEngine(Logger, ironClad);

        var result = await engine.ExecuteAsync(
            MakeDevice("E"), ScanSeverity.High, UsbOperatingMode.Strict, "suspicious file");

        Assert.True(result.Success);
        Assert.Equal(0, ironClad.CutCallCount); // Only Critical triggers IronClad
        Assert.Equal(UsbActionType.EjectUsb, result.ActionType);
    }

    [Fact]
    public async Task IronClad_Failure_Falls_Back_To_Software_Eject()
    {
        var ironClad = new FakeIronCladEngine(available: true, cutSuccess: false);
        var engine = new UsbActionEngine(Logger, ironClad);

        var result = await engine.ExecuteAsync(
            MakeDevice("E"), ScanSeverity.Critical, UsbOperatingMode.Strict, "malware detected");

        Assert.True(result.Success);
        Assert.Equal(1, ironClad.CutCallCount); // IronClad was tried
        Assert.Equal(UsbActionType.BlockAndEject, result.ActionType); // Software fallback
    }

    [Fact]
    public async Task DriveLetterMapping_E_Through_H()
    {
        var driveToPort = new Dictionary<string, int> { { "E", 1 }, { "F", 2 }, { "G", 3 }, { "H", 4 } };

        foreach (var (drive, expectedPort) in driveToPort)
        {
            var ironClad = new FakeIronCladEngine(available: true, cutSuccess: true);
            var engine = new UsbActionEngine(Logger, ironClad);

            await engine.ExecuteAsync(
                MakeDevice(drive), ScanSeverity.Critical, UsbOperatingMode.Strict, "test");

            Assert.Equal(expectedPort, ironClad.LastPortCut);
        }
    }

    private sealed class FakeIronCladEngine : IIronCladActionEngine
    {
        private readonly bool _cutSuccess;
        public bool IsAvailable { get; }
        public int CutCallCount { get; private set; }
        public int LastPortCut { get; private set; }

        public FakeIronCladEngine(bool available, bool cutSuccess)
        {
            IsAvailable = available;
            _cutSuccess = cutSuccess;
        }

        public Task<IronCladActionResult> CutUsbPortAsync(int portNumber, string justification, Guid? sourceAlertId, CancellationToken ct = default)
        {
            CutCallCount++;
            LastPortCut = portNumber;
            return Task.FromResult(new IronCladActionResult
            {
                IsSuccess = _cutSuccess,
                Outcome = _cutSuccess ? IronCladActionOutcome.Success : IronCladActionOutcome.DeviceError,
                Message = _cutSuccess ? $"Port {portNumber} cut" : "Device error"
            });
        }

        public Task<IronCladActionResult> RestoreUsbPortAsync(int portNumber, string justification, CancellationToken ct = default)
            => Task.FromResult(new IronCladActionResult { IsSuccess = true, Outcome = IronCladActionOutcome.Success, Message = "OK" });

        public Task<IronCladActionResult> CutAllPortsAsync(string justification, CancellationToken ct = default)
            => Task.FromResult(new IronCladActionResult { IsSuccess = true, Outcome = IronCladActionOutcome.Success, Message = "OK" });

        public Task<IronCladActionResult> RestoreAllPortsAsync(string justification, CancellationToken ct = default)
            => Task.FromResult(new IronCladActionResult { IsSuccess = true, Outcome = IronCladActionOutcome.Success, Message = "OK" });

        public Task<HealthStatus> GetDeviceStatusAsync(CancellationToken ct = default)
            => Task.FromResult(new HealthStatus
            {
                IsConnected = IsAvailable, DeviceVersion = "1.0.0", PortCount = 4,
                PortStates = new Dictionary<int, PortState>(), LastHeartbeatAt = DateTime.UtcNow,
                AverageLatency = TimeSpan.Zero, MissedHeartbeats = 0
            });
    }
}
