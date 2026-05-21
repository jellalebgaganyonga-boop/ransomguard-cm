using System.Collections.Concurrent;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RansomGuard.Agent.Core.Configuration;
using RansomGuard.Agent.Core.Detection;
using RansomGuard.Agent.Core.Detection.Genealogy;
using RansomGuard.Agent.Core.Detection.UsbGuard;
using RansomGuard.Agent.Core.Detection.UsbGuard.Actions;
using RansomGuard.Agent.Core.Detection.UsbGuard.Models;
using RansomGuard.Agent.Core.Detection.UsbGuard.Scanning;
using RansomGuard.Agent.Core.Detection.UsbGuard.Wmi;
using RansomGuard.Agent.Core.Persistence;
using RansomGuard.Agent.Core.Persistence.Entities;
using RansomGuard.Agent.Core.Security.RateLimiting;

namespace RansomGuard.Agent.Service;

/// <summary>
/// BackgroundService that monitors USB device connections in real-time via WMI.
/// Architecture: WMI Events -> Channel -> Worker -> Whitelist -> Scanner -> ActionEngine -> Genealogy.
/// Handles rapid plug-unplug via CancellationTokenSource per device.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class UsbDeviceMonitor : BackgroundService, IUsbDeviceMonitor
{
    private const int EventChannelCapacity = 1_000;
    private const int RetentionDays = 90;

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

    /// <summary>Number of currently connected USB devices.</summary>
    public int ConnectedDeviceCount => _connectedDevices.Count;

    /// <summary>Total events processed since service start.</summary>
    public long TotalEventsProcessed => Interlocked.Read(ref _totalEventsProcessed);

    /// <summary>Initializes the USB device monitor.</summary>
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

    /// <inheritdoc />
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

        // Create CTS for scan cancellation on rapid unplug
        using var scanCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _activeScanCts[device.DeviceInstanceId] = scanCts;

        try
        {
            _logger.LogWarning(
                "USB GUARD: Device connected — {Class} {Manufacturer} {Product} (VID:{VID} PID:{PID}) Serial:{Serial}",
                device.DeviceClass, device.Manufacturer, device.ProductDescription,
                device.VendorId, device.ProductId, device.SerialNumber);

            await using var scope = _scopeFactory.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AgentDbContext>();
            var whitelistService = scope.ServiceProvider.GetRequiredService<IUsbWhitelistService>();
            var contentScanner = scope.ServiceProvider.GetRequiredService<IUsbContentScanner>();
            var actionEngine = scope.ServiceProvider.GetRequiredService<IUsbActionEngine>();

            // Parse operating mode from config
            var operatingMode = Enum.TryParse<UsbOperatingMode>(_config.UsbGuard?.OperatingMode, true, out var mode)
                ? mode
                : UsbOperatingMode.Permissive;

            // Build runtime policy from config
            var policy = new UsbPolicy
            {
                Id = Guid.NewGuid(),
                Mode = operatingMode,
                MaxFileSizeForScanMB = _config.UsbGuard?.MaxFileSizeForScanMB ?? 100,
                MaxScanDurationSeconds = _config.UsbGuard?.MaxScanDurationSeconds ?? 120,
                ScanArchiveContents = _config.UsbGuard?.ScanArchiveContents ?? true,
                BlockBootableUsb = _config.UsbGuard?.BlockBootableUsb ?? true,
                AlertOnHidDevice = false,
                AlertOnNetworkDevice = false,
                SuspiciousExtensionsJson = "[]",
                UpdatedAt = DateTime.UtcNow
            };

            // Persist connection log
            var (isWhitelisted, _) = await whitelistService.IsWhitelistedAsync(device.SerialNumber, scanCts.Token);

            var connectionLog = new UsbConnectionLog
            {
                DeviceInstanceId = device.DeviceInstanceId,
                SerialNumberHash = device.SerialNumber,
                VendorId = device.VendorId,
                ProductId = device.ProductId,
                DeviceClass = device.DeviceClass.ToString(),
                DriveLetter = device.DriveLetter,
                ConnectedAt = device.ConnectedAt,
                WasWhitelisted = isWhitelisted,
                RetainUntil = DateTime.UtcNow.AddDays(RetentionDays)
            };
            dbContext.UsbConnectionLogs.Add(connectionLog);
            await dbContext.SaveChangesAsync(scanCts.Token);

            // Bootable USB check — block immediately if policy dictates
            if (device.IsBootable && policy.BlockBootableUsb)
            {
                _logger.LogCritical("USB GUARD: BOOTABLE USB blocked by policy on {Drive}", device.DriveLetter);

                await PersistAlertAndExecuteActionAsync(
                    dbContext, actionEngine, connectionLog.Id, device,
                    ScanSeverity.Critical, operatingMode,
                    "Bootable USB blocked by policy",
                    scanCts.Token);
                return;
            }

            // Check bootable signature for mass storage (informational if not blocked)
            if (device.DeviceClass == UsbDeviceClass.MassStorage && device.DriveLetter is not null && !device.IsBootable)
            {
                bool bootable = await _bootableDetector.IsBootableAsync(device.DriveLetter, scanCts.Token);
                if (bootable)
                {
                    _logger.LogCritical("USB GUARD: BOOTABLE USB detected on {Drive}", device.DriveLetter);
                    if (policy.BlockBootableUsb)
                    {
                        await PersistAlertAndExecuteActionAsync(
                            dbContext, actionEngine, connectionLog.Id, device,
                            ScanSeverity.Critical, operatingMode,
                            "Boot signature (MBR/GPT) detected — blocked by policy",
                            scanCts.Token);
                        return;
                    }
                }
            }

            // Whitelist check — skip deep scan for trusted devices
            if (isWhitelisted)
            {
                _logger.LogInformation("USB GUARD: Device {Serial} is whitelisted — skipping content scan",
                    device.SerialNumber);
                return;
            }

            // Content scan (only for mass storage with drive letter)
            if (device.DeviceClass != UsbDeviceClass.MassStorage || device.DriveLetter is null)
            {
                _logger.LogInformation("USB GUARD: Non-storage device {Class} — no content scan",
                    device.DeviceClass);
                return;
            }

            var scanReport = await contentScanner.ScanAsync(device, policy, scanCts.Token);

            // Persist scan result
            var scanResult = new UsbScanResult
            {
                UsbConnectionLogId = connectionLog.Id,
                TotalFilesScanned = scanReport.FilesScanned,
                FlaggedFilesCount = scanReport.Findings.Count,
                FlaggedFilesJson = JsonSerializer.Serialize(scanReport.Findings),
                ScanDurationMs = (long)scanReport.Duration.TotalMilliseconds,
                ScanCompleted = !scanReport.WasCancelled,
                HighestSeverity = scanReport.OverallSeverity.ToString(),
                ScannedAt = scanReport.ScannedAt
            };
            dbContext.UsbScanResults.Add(scanResult);
            await dbContext.SaveChangesAsync(scanCts.Token);

            // Act on findings
            if (scanReport.OverallSeverity >= ScanSeverity.Medium)
            {
                var alert = await PersistAlertAndExecuteActionAsync(
                    dbContext, actionEngine, scanResult.Id, device,
                    scanReport.OverallSeverity, operatingMode,
                    $"USB scan found {scanReport.Findings.Count} suspicious items (highest: {scanReport.OverallSeverity})",
                    scanCts.Token);

                // Fire-and-forget genealogy enrichment
                var enrichScope = _scopeFactory.CreateAsyncScope();
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await using (enrichScope)
                        {
                            var enricher = enrichScope.ServiceProvider.GetRequiredService<IGenealogyEnricher>();
                            await enricher.EnrichAlertAsync(alert.Id, device.DriveLetter ?? "USB", default);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Genealogy enrichment failed for USB alert {AlertId}", alert.Id);
                    }
                });
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("USB GUARD: Scan cancelled (rapid unplug) for device {Serial}", device.SerialNumber);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "USB GUARD: Connection handling failed for {DeviceId}", device.DeviceInstanceId);
        }
        finally
        {
            _activeScanCts.TryRemove(device.DeviceInstanceId, out _);
        }
    }

    private async Task<UsbAlert> PersistAlertAndExecuteActionAsync(
        AgentDbContext dbContext,
        IUsbActionEngine actionEngine,
        Guid scanOrConnectionId,
        UsbDevice device,
        ScanSeverity severity,
        UsbOperatingMode mode,
        string description,
        CancellationToken ct)
    {
        var actionResult = await actionEngine.ExecuteAsync(device, severity, mode, description, ct);

        var alert = new UsbAlert
        {
            UsbScanResultId = scanOrConnectionId,
            Title = $"USB GUARD: {severity} — {device.ProductDescription}",
            Description = description,
            Severity = severity.ToString(),
            ActionTaken = actionResult.ActionType.ToString()
        };

        dbContext.UsbAlerts.Add(alert);
        await dbContext.SaveChangesAsync(ct);

        _logger.LogWarning("USB GUARD ALERT: {Title} — Action: {Action}",
            alert.Title, alert.ActionTaken);

        return alert;
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
