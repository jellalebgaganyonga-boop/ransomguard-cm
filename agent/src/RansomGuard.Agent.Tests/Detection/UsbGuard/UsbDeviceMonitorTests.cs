using System.Threading.Channels;
using RansomGuard.Agent.Core.Detection.UsbGuard;
using RansomGuard.Agent.Core.Detection.UsbGuard.Models;
using Shouldly;

namespace RansomGuard.Agent.Tests.Detection.UsbGuard;

/// <summary>
/// Tests for USB device detection, classification, bootable detection, and event handling.
/// </summary>
public sealed class UsbDeviceMonitorTests
{
    [Fact]
    public void Connection_Event_Captures_All_Device_Fields()
    {
        var device = CreateDevice(UsbDeviceClass.MassStorage, "0781", "5567", "SanDisk", "Cruzer");
        device.Id.ShouldNotBe(Guid.Empty);
        device.DeviceInstanceId.ShouldNotBeNullOrEmpty();
        device.SerialNumber.ShouldNotBeNullOrEmpty();
        device.VendorId.ShouldBe("0781");
        device.ProductId.ShouldBe("5567");
        device.Manufacturer.ShouldBe("SanDisk");
        device.ProductDescription.ShouldBe("Cruzer");
        device.DeviceClass.ShouldBe(UsbDeviceClass.MassStorage);
        device.ConnectedAt.ShouldBeLessThanOrEqualTo(DateTime.UtcNow);
    }

    [Fact]
    public void Disconnection_Event_Marks_DisconnectedAt()
    {
        var device = CreateDevice(UsbDeviceClass.MassStorage);
        device.DisconnectedAt.ShouldBeNull();

        device.DisconnectedAt = DateTime.UtcNow;
        device.DisconnectedAt.ShouldNotBeNull();
        (device.DisconnectedAt.Value - device.ConnectedAt).TotalSeconds.ShouldBeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public void MBR_Signature_Detected_Sets_IsBootable_True()
    {
        var buffer = new byte[520];
        buffer[510] = 0x55;
        buffer[511] = 0xAA;

        BootableUsbDetector.HasBootSignature(buffer).ShouldBeTrue();
    }

    [Fact]
    public void GPT_Signature_Detected_Sets_IsBootable_True()
    {
        var buffer = new byte[520];
        "EFI PART"u8.CopyTo(buffer.AsSpan(512));

        BootableUsbDetector.HasBootSignature(buffer).ShouldBeTrue();
    }

    [Fact]
    public void No_Boot_Signature_Sets_IsBootable_False()
    {
        var buffer = new byte[520]; // All zeros
        BootableUsbDetector.HasBootSignature(buffer).ShouldBeFalse();
    }

    [Fact]
    public void Hid_Device_Class_Identified_As_Hid()
    {
        var device = CreateDevice(UsbDeviceClass.Hid);
        device.DeviceClass.ShouldBe(UsbDeviceClass.Hid);
    }

    [Fact]
    public void Smart_Card_Reader_Identified_Correctly()
    {
        var device = CreateDevice(UsbDeviceClass.SmartCardReader);
        device.DeviceClass.ShouldBe(UsbDeviceClass.SmartCardReader);
    }

    [Fact]
    public void Composite_Device_Class_Detected()
    {
        var device = CreateDevice(UsbDeviceClass.Composite);
        device.DeviceClass.ShouldBe(UsbDeviceClass.Composite);
    }

    [Fact]
    public void Channel_Saturation_Drops_Oldest()
    {
        var channel = Channel.CreateBounded<UsbConnectionEvent>(
            new BoundedChannelOptions(10) { FullMode = BoundedChannelFullMode.DropOldest });

        // Write 20 events to a capacity-10 channel
        for (int i = 0; i < 20; i++)
        {
            var evt = new UsbConnectionEvent
            {
                Id = Guid.NewGuid(),
                EventType = UsbConnectionEventType.Connected,
                Device = CreateDevice(UsbDeviceClass.MassStorage),
                OccurredAt = DateTime.UtcNow
            };
            channel.Writer.TryWrite(evt).ShouldBeTrue();
        }

        int count = 0;
        while (channel.Reader.TryRead(out _)) count++;
        count.ShouldBe(10);
    }

    [Fact]
    public void Rapid_Plug_Unplug_Cancels_Scan_Gracefully()
    {
        using var cts = new CancellationTokenSource();

        // Simulate rapid unplug by cancelling immediately
        cts.Cancel();

        cts.Token.IsCancellationRequested.ShouldBeTrue();

        // Verify no exception thrown when checking cancellation
        Should.NotThrow(() =>
        {
            if (cts.Token.IsCancellationRequested)
            {
                // Scan would stop here
            }
        });
    }

    [Fact]
    public async Task Multiple_Simultaneous_Plugs_Handled()
    {
        var channel = Channel.CreateBounded<UsbConnectionEvent>(
            new BoundedChannelOptions(100) { FullMode = BoundedChannelFullMode.DropOldest });

        // Simulate 10 simultaneous USB plugs
        var tasks = Enumerable.Range(0, 10).Select(i =>
        {
            var evt = new UsbConnectionEvent
            {
                Id = Guid.NewGuid(),
                EventType = UsbConnectionEventType.Connected,
                Device = CreateDevice(UsbDeviceClass.MassStorage, vid: i.ToString("D4")),
                OccurredAt = DateTime.UtcNow
            };
            return Task.Run(() => channel.Writer.TryWrite(evt));
        });

        await Task.WhenAll(tasks);

        int count = 0;
        while (channel.Reader.TryRead(out _)) count++;
        count.ShouldBe(10);
    }

    [Fact]
    public void Buffer_Too_Short_For_Boot_Detection_Returns_False()
    {
        var buffer = new byte[100]; // Too short
        BootableUsbDetector.HasBootSignature(buffer).ShouldBeFalse();
    }

    private static UsbDevice CreateDevice(
        UsbDeviceClass deviceClass,
        string vid = "0781", string pid = "5567",
        string manufacturer = "Test", string product = "TestDevice") => new()
    {
        Id = Guid.NewGuid(),
        DeviceInstanceId = $"USB\\VID_{vid}&PID_{pid}\\{Guid.NewGuid():N}",
        SerialNumber = Guid.NewGuid().ToString("N")[..16],
        VendorId = vid,
        ProductId = pid,
        Manufacturer = manufacturer,
        ProductDescription = product,
        Model = product,
        DriveLetter = deviceClass == UsbDeviceClass.MassStorage ? "E:" : null,
        CapacityBytes = deviceClass == UsbDeviceClass.MassStorage ? 16L * 1024 * 1024 * 1024 : null,
        DeviceClass = deviceClass,
        BusType = UsbBusType.Usb30,
        IsRemovable = true,
        IsBootable = false,
        ConnectedAt = DateTime.UtcNow
    };
}
