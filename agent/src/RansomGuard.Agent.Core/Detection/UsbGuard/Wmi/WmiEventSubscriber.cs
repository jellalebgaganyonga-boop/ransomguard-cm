using System.Management;
using System.Runtime.Versioning;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using RansomGuard.Agent.Core.Detection.UsbGuard.Models;

namespace RansomGuard.Agent.Core.Detection.UsbGuard.Wmi;

/// <summary>
/// Subscribes to WMI events for USB PnP device connections/disconnections
/// and removable disk mount events. All ManagementEventWatcher instances
/// are properly disposed on shutdown.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WmiEventSubscriber : IWmiEventSubscriber
{
    private readonly ILogger<WmiEventSubscriber> _logger;
    private readonly List<ManagementEventWatcher> _watchers = [];
    private readonly object _lock = new();

    /// <summary>Initializes the WMI event subscriber.</summary>
    public WmiEventSubscriber(ILogger<WmiEventSubscriber> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public void Start(ChannelWriter<UsbConnectionEvent> writer, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            // PnP device creation (USB plug)
            var pnpCreation = new ManagementEventWatcher(
                new WqlEventQuery("__InstanceCreationEvent", TimeSpan.FromSeconds(1),
                    "TargetInstance ISA 'Win32_PnPEntity'"));
            pnpCreation.EventArrived += (_, e) => HandlePnpEvent(e, UsbConnectionEventType.Connected, writer);
            pnpCreation.Start();
            _watchers.Add(pnpCreation);

            // PnP device deletion (USB unplug)
            var pnpDeletion = new ManagementEventWatcher(
                new WqlEventQuery("__InstanceDeletionEvent", TimeSpan.FromSeconds(1),
                    "TargetInstance ISA 'Win32_PnPEntity'"));
            pnpDeletion.EventArrived += (_, e) => HandlePnpEvent(e, UsbConnectionEventType.Disconnected, writer);
            pnpDeletion.Start();
            _watchers.Add(pnpDeletion);

            // Removable disk mount (drive letter assigned)
            var diskCreation = new ManagementEventWatcher(
                new WqlEventQuery("__InstanceCreationEvent", TimeSpan.FromSeconds(1),
                    "TargetInstance ISA 'Win32_LogicalDisk' AND TargetInstance.DriveType = 2"));
            diskCreation.EventArrived += (_, e) => HandleDiskEvent(e, UsbConnectionEventType.Mounted, writer);
            diskCreation.Start();
            _watchers.Add(diskCreation);

            _logger.LogInformation("USB GUARD WMI subscribers started ({Count} watchers)", _watchers.Count);
        }
    }

    /// <inheritdoc />
    public async Task StopAsync()
    {
        List<ManagementEventWatcher> toDispose;
        lock (_lock)
        {
            toDispose = [.. _watchers];
            _watchers.Clear();
        }

        foreach (var watcher in toDispose)
        {
            try
            {
                watcher.Stop();
                watcher.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error disposing WMI watcher");
            }
        }

        _logger.LogInformation("USB GUARD WMI subscribers stopped");
        await Task.CompletedTask;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await StopAsync();
    }

    private void HandlePnpEvent(EventArrivedEventArgs e, UsbConnectionEventType eventType, ChannelWriter<UsbConnectionEvent> writer)
    {
        try
        {
            using var targetInstance = (ManagementBaseObject)e.NewEvent["TargetInstance"];
            string? deviceId = targetInstance["DeviceID"]?.ToString();
            string? pnpClass = targetInstance["PNPClass"]?.ToString();
            string? name = targetInstance["Name"]?.ToString();
            string? manufacturer = targetInstance["Manufacturer"]?.ToString();

            // Filter to USB devices only
            if (deviceId is null || !deviceId.Contains("USB", StringComparison.OrdinalIgnoreCase))
                return;

            var device = BuildDeviceFromWmi(deviceId, pnpClass, name, manufacturer);
            var connectionEvent = new UsbConnectionEvent
            {
                Id = Guid.NewGuid(),
                EventType = eventType,
                Device = device,
                OccurredAt = DateTime.UtcNow
            };

            writer.TryWrite(connectionEvent);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error processing WMI PnP event");
        }
    }

    private void HandleDiskEvent(EventArrivedEventArgs e, UsbConnectionEventType eventType, ChannelWriter<UsbConnectionEvent> writer)
    {
        try
        {
            using var targetInstance = (ManagementBaseObject)e.NewEvent["TargetInstance"];
            string? driveLetter = targetInstance["DeviceID"]?.ToString();
            long? size = targetInstance["Size"] is not null ? Convert.ToInt64(targetInstance["Size"]) : null;

            var device = new UsbDevice
            {
                Id = Guid.NewGuid(),
                DeviceInstanceId = driveLetter ?? "UNKNOWN",
                SerialNumber = "PENDING_LOOKUP",
                VendorId = "",
                ProductId = "",
                Manufacturer = "",
                ProductDescription = $"Removable Disk {driveLetter}",
                Model = "",
                DriveLetter = driveLetter,
                CapacityBytes = size,
                DeviceClass = UsbDeviceClass.MassStorage,
                BusType = UsbBusType.Unknown,
                IsRemovable = true,
                IsBootable = false,
                ConnectedAt = DateTime.UtcNow
            };

            writer.TryWrite(new UsbConnectionEvent
            {
                Id = Guid.NewGuid(),
                EventType = eventType,
                Device = device,
                OccurredAt = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error processing WMI disk event");
        }
    }

    private static UsbDevice BuildDeviceFromWmi(string deviceId, string? pnpClass, string? name, string? manufacturer)
    {
        // Parse VID/PID from device instance ID (e.g., USB\VID_0781&PID_5567\...)
        string vid = "", pid = "", serial = "";
        var parts = deviceId.Split('\\');
        if (parts.Length >= 2)
        {
            var idPart = parts[1];
            foreach (var segment in idPart.Split('&'))
            {
                if (segment.StartsWith("VID_", StringComparison.OrdinalIgnoreCase))
                    vid = segment[4..];
                else if (segment.StartsWith("PID_", StringComparison.OrdinalIgnoreCase))
                    pid = segment[4..];
            }
        }
        if (parts.Length >= 3) serial = parts[2];

        return new UsbDevice
        {
            Id = Guid.NewGuid(),
            DeviceInstanceId = deviceId,
            SerialNumber = serial,
            VendorId = vid,
            ProductId = pid,
            Manufacturer = manufacturer ?? "",
            ProductDescription = name ?? "",
            Model = name ?? "",
            DriveLetter = null,
            CapacityBytes = null,
            DeviceClass = ClassifyDevice(pnpClass),
            BusType = UsbBusType.Unknown,
            IsRemovable = true,
            IsBootable = false,
            ConnectedAt = DateTime.UtcNow
        };
    }

    private static UsbDeviceClass ClassifyDevice(string? pnpClass) => pnpClass?.ToLowerInvariant() switch
    {
        "diskdrive" or "usbstor" => UsbDeviceClass.MassStorage,
        "hidclass" or "hid" => UsbDeviceClass.Hid,
        "printer" => UsbDeviceClass.Printer,
        "media" or "audio" => UsbDeviceClass.Audio,
        "camera" or "image" => UsbDeviceClass.Video,
        "net" or "bluetooth" => UsbDeviceClass.Network,
        "smartcardreader" => UsbDeviceClass.SmartCardReader,
        "usb" => UsbDeviceClass.Composite,
        _ => UsbDeviceClass.Unknown
    };
}
