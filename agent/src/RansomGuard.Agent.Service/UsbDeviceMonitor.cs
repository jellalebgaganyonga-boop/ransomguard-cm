using System.Collections.Concurrent;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RansomGuard.Agent.Core.Configuration;
using RansomGuard.Agent.Core.Diagnostics;
using RansomGuard.Agent.Core.Detection;
using RansomGuard.Agent.Core.Detection.Genealogy;
using RansomGuard.Agent.Core.Detection.UsbGuard;
using RansomGuard.Agent.Core.Detection.UsbGuard.Actions;
using RansomGuard.Agent.Core.Detection.UsbGuard.Models;
using RansomGuard.Agent.Core.Detection.UsbGuard.Scanning;
using RansomGuard.Agent.Core.Detection.UsbGuard.Wmi;
using RansomGuard.Agent.Core.Communication;
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
    private readonly IAlertForwardingQueue? _alertQueue;
    private readonly IModuleStateRegistry _moduleState;
    private readonly IModuleFailureReporter _failureReporter;
    private readonly IModuleFaultInjector _faultInjector;
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
        IModuleStateRegistry moduleState,
        IModuleFailureReporter failureReporter,
        RateLimiterFactory? rateLimiterFactory = null,
        IAlertForwardingQueue? alertQueue = null,
        IModuleFaultInjector? faultInjector = null)
    {
        _logger = logger;
        _wmiSubscriber = wmiSubscriber;
        _deduplicator = deduplicator;
        _bootableDetector = bootableDetector;
        _scopeFactory = scopeFactory;
        _config = config.CurrentValue;
        _moduleState = moduleState;
        _failureReporter = failureReporter;
        _faultInjector = faultInjector ?? new NoModuleFaultInjector();
        _scanRateLimiter = rateLimiterFactory?.GetLimiter("usb-scan");
        _alertQueue = alertQueue;
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
        bool reachedActive = false;
        bool subscribersStarted = false;
        Task? consumerTask = null;

        // Every task of the module runs on this token, so that after a failure
        // nothing keeps running unseen.
        using var moduleCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        CancellationToken moduleToken = moduleCts.Token;

        try
        {
            if (_config.UsbGuard is null || !_config.UsbGuard.Enabled)
            {
                _logger.LogInformation("USB GUARD module is disabled");
                _moduleState.Publish(ModuleCode.UsbGuard, ModuleState.DisabledByConfig, ModuleReasonCode.DisabledByConfig);
                return;
            }

            // Strict + empty whitelist: warn at startup, do not block (A5).
            UsbOperatingMode startupMode = ResolveOperatingMode();
            try
            {
                await using var startupScope = _scopeFactory.CreateAsyncScope();
                await UsbGuardStartupCheck.WarnIfStrictWithEmptyWhitelistAsync(
                    startupMode,
                    startupScope.ServiceProvider.GetRequiredService<IUsbWhitelistService>(),
                    _logger,
                    stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The whitelist service itself could not be built (e.g. database unavailable).
                _logger.LogWarning(ex, UsbGuardStartupCheck.UnreadableWhitelistWarning);
            }

            // Start WMI subscribers
            _wmiSubscriber.Start(_eventChannel.Writer, moduleToken);
            subscribersStarted = true;

            consumerTask = ConsumeEventsAsync(moduleToken);

            _logger.LogInformation("USB GUARD monitor active (mode: {Mode})", startupMode);
            var (postureState, postureReason) = UsbGuardStartupCheck.RunningPosture(startupMode);
            _moduleState.Publish(ModuleCode.UsbGuard, postureState, postureReason);
            reachedActive = true;

            await ModuleTaskObserver.ObserveUntilStoppedAsync(
                stoppingToken,
                ("consumer", consumerTask),
                ("status-log", LogStatusPeriodicallyAsync(moduleToken)),
                ("fault-injection", _faultInjector.WhenStopRequestedAsync(ModuleCode.UsbGuard, moduleToken)));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
        catch (Exception ex)
        {
            // Swallowed on purpose: one module going down must not take the host —
            // and every other module — with it. The reporter leaves the three
            // traces: registry state, Critical log, audit entry.
            await _failureReporter.ReportStoppedAsync(ModuleCode.UsbGuard, reachedActive, ex);
        }
        finally
        {
            await StopModuleAsync(moduleCts, consumerTask, subscribersStarted);
        }
    }

    private async Task LogStatusPeriodicallyAsync(CancellationToken ct)
    {
        using var heartbeat = new PeriodicTimer(TimeSpan.FromSeconds(60));
        while (await heartbeat.WaitForNextTickAsync(ct))
        {
            _logger.LogInformation(
                "USB GUARD heartbeat | Connected: {Connected} | Events processed: {Events}",
                ConnectedDeviceCount, TotalEventsProcessed);
        }
    }

    /// <summary>Stops every source and task of the module; best effort, never throws.</summary>
    private async Task StopModuleAsync(CancellationTokenSource moduleCts, Task? consumerTask, bool subscribersStarted)
    {
        _eventChannel.Writer.TryComplete();
        await moduleCts.CancelAsync();

        if (consumerTask is not null)
        {
            try
            {
                await consumerTask;
            }
            catch (Exception ex)
            {
                // Cancelled by the stop above, or already reported as the failure.
                _logger.LogDebug(ex, "USB GUARD consumer ended during stop");
            }
        }

        if (subscribersStarted)
        {
            try
            {
                await _wmiSubscriber.StopAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "USB GUARD: WMI subscribers did not stop cleanly");
            }
        }
    }

    /// <summary>
    /// Operating mode from Agent:UsbGuard:OperatingMode. An unrecognised value still
    /// falls back to Permissive silently: that belongs to the configuration-validation
    /// work on silent defaults, not to this module.
    /// </summary>
    private UsbOperatingMode ResolveOperatingMode() =>
        Enum.TryParse<UsbOperatingMode>(_config.UsbGuard?.OperatingMode, true, out var mode)
            ? mode
            : UsbOperatingMode.Permissive;

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

            var operatingMode = ResolveOperatingMode();

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
                    findings: [],
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
                            findings: [],
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
                    scanReport.Findings,
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
        IReadOnlyList<UsbFileFinding> findings,
        CancellationToken ct)
    {
        // The flagged files travel with the alert: QuarantineFile takes the ones at High or above off the key.
        var actionResult = await actionEngine.ExecuteAsync(device, severity, mode, description, findings, ct);

        var alert = new UsbAlert
        {
            UsbScanResultId = scanOrConnectionId,
            Title = $"USB GUARD: {severity} — {device.ProductDescription}",
            Description = description,
            Severity = severity.ToString(),
            // Never claim an action that did not happen (forwarded to the GRID as action_taken).
            ActionTaken = actionResult.Success
                ? actionResult.ActionType.ToString()
                : $"{actionResult.ActionType} ({actionResult.ReasonCode ?? "failed"})"
        };

        dbContext.UsbAlerts.Add(alert);
        await dbContext.SaveChangesAsync(ct);

        _logger.LogWarning("USB GUARD ALERT: {Title} — Action: {Action}",
            alert.Title, alert.ActionTaken);

        // Forward to GRID
        _alertQueue?.TryEnqueue(AlertMapper.FromUsbAlert(alert));

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
