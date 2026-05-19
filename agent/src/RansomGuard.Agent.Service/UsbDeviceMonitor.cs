using System.Collections.Concurrent;
using System.Runtime.Versioning;
using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RansomGuard.Agent.Core.Configuration;
using RansomGuard.Agent.Core.Detection;
using RansomGuard.Agent.Core.Detection.UsbGuard;
using RansomGuard.Agent.Core.Detection.UsbGuard.Models;
using RansomGuard.Agent.Core.Detection.UsbGuard.Wmi;
using RansomGuard.Agent.Core.Security.RateLimiting;

namespace RansomGuard.Agent.Service;

/// <summary>
/// BackgroundService that monitors USB device connections in real-time via WMI.
/// Architecture: WMI Events -> Channel -> Worker -> Scanner -> ActionEngine.
/// Handles rapid plug-unplug via CancellationTokenSource per device.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class UsbDeviceMonitor : BackgroundService, IUsbDeviceMonitor
{
    private const int EventChannelCapacity = 1_000;

    private readonly ILogger<UsbDeviceMonitor> _logger;
    private readonly IWmiEventSubscriber _wmiSubscriber;
    private readonly IFileEventDeduplicator _deduplicator;
    private readonly BootableUsbDetector _bootableDetector;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly AgentConfiguration _config;
    private readonly Channel<UsbConnectionEvent> _eventChannel;
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _activeScanCts = new();
    private readonly ConcurrentDictionary<string, UsbDevice> _connectedDevices = new();
    private readonly IOperationRateLimiter? _scanRateLimiter;
    private long _totalEventsProcessed;

    public int ConnectedDeviceCount => _connectedDevices.Count;
    public long TotalEventsProcessed => Interlocked.Read(ref _totalEventsProcessed);

    public UsbDeviceMonitor(
        ILogger<UsbDeviceMonitor> logger,
        IWmiEventSubscriber wmiSubscriber,
        IFileEventDeduplicator deduplicator,
        BootableUsbDetector bootableDetector,
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<AgentConfiguration> config,
        RateLimiterFactory? rateLimiterFactory = null)
    {
        _logger = logger;
        _wmiSubscriber = wmiSubscriber;
        _deduplicator = deduplicator;
        _bootableDetector = bootableDetector;
        _scopeFactory = scopeFactory;
        _config = config.CurrentValue;
        _scanRateLimiter = rateLimiterFactory?.GetLimiter("usb-scan");
        _eventChannel = Channel.CreateBounded<UsbConnectionEvent>(
            new BoundedChannelOptions(EventChannelCapacity)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true
            });
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_config.UsbGuard is null || !_config.UsbGuard.Enabled)
        {
            _logger.LogInformation("USB GUARD module is disabled");
            return;
        }

        // Start WMI subscribers
        _wmiSubscriber.Start(_eventChannel.Writer, stoppingToken);

        // Start consumer task
        Task consumerTask = ConsumeEventsAsync(stoppingToken);

        _logger.LogInformation("USB GUARD monitor active");

        // Heartbeat
        using var heartbeat = new PeriodicTimer(TimeSpan.FromSeconds(60));
        try
        {
            while (await heartbeat.WaitForNextTickAsync(stoppingToken))
            {
                _logger.LogInformation(
                    "USB GUARD heartbeat | Connected: {Connected} | Events processed: {Events}",
                    ConnectedDeviceCount, TotalEventsProcessed);
            }
        }
        catch (OperationCanceledException) { }

        _eventChannel.Writer.TryComplete();
        await consumerTask;
        await _wmiSubscriber.StopAsync();
    }

    private async Task ConsumeEventsAsync(CancellationToken ct)
    {
        await foreach (var evt in _eventChannel.Reader.ReadAllAsync(ct))
        {
            try
            {
                Interlocked.Increment(ref _totalEventsProcessed);

                // Dedup burst events
                string dedupeKey = $"USB:{evt.Device.DeviceInstanceId}:{evt.EventType}";
                if (!_deduplicator.ShouldProcess(dedupeKey, System.IO.WatcherChangeTypes.Changed))
                    continue;

                switch (evt.EventType)
                {
                    case UsbConnectionEventType.Connected:
                    case UsbConnectionEventType.Mounted:
                        await HandleConnectionAsync(evt, ct);
                        break;

                    case UsbConnectionEventType.Disconnected:
                    case UsbConnectionEventType.Unmounted:
                        HandleDisconnection(evt);
                        break;
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing USB event for {DeviceId}", evt.Device.DeviceInstanceId);
            }
        }
    }

    private async Task HandleConnectionAsync(UsbConnectionEvent evt, CancellationToken ct)
    {
        var device = evt.Device;
        _connectedDevices[device.DeviceInstanceId] = device;

        _logger.LogWarning(
            "USB GUARD: Device connected — {Class} {Manufacturer} {Product} (VID:{VID} PID:{PID}) Serial:{Serial}",
            device.DeviceClass, device.Manufacturer, device.ProductDescription,
            device.VendorId, device.ProductId, device.SerialNumber);

        // Check bootable signature for mass storage with drive letter
        if (device.DeviceClass == UsbDeviceClass.MassStorage && device.DriveLetter is not null)
        {
            bool bootable = await _bootableDetector.IsBootableAsync(device.DriveLetter, ct);
            if (bootable)
            {
                _logger.LogCritical("USB GUARD: BOOTABLE USB detected on {Drive}", device.DriveLetter);
            }
        }

        // Create CTS for scan cancellation on rapid unplug
        var scanCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _activeScanCts[device.DeviceInstanceId] = scanCts;

        // TODO: Wire to UsbContentScanner and UsbActionEngine (A.3/A.6)
    }

    private void HandleDisconnection(UsbConnectionEvent evt)
    {
        string deviceId = evt.Device.DeviceInstanceId;

        // Cancel any active scan for this device
        if (_activeScanCts.TryRemove(deviceId, out var cts))
        {
            _logger.LogInformation("USB GUARD: Device removed during scan, cancelling — {DeviceId}", deviceId);
            cts.Cancel();
            cts.Dispose();
        }

        if (_connectedDevices.TryRemove(deviceId, out var device))
        {
            device.DisconnectedAt = DateTime.UtcNow;
            _logger.LogInformation("USB GUARD: Device disconnected — {Product} after {Duration}",
                device.ProductDescription,
                device.DisconnectedAt.Value - device.ConnectedAt);
        }
    }
}
