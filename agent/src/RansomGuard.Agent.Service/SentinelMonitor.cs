using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RansomGuard.Agent.Core.Configuration;
using RansomGuard.Agent.Core.Detection;
using RansomGuard.Agent.Core.Detection.Genealogy;
using RansomGuard.Agent.Core.Detection.Sentinel;
using RansomGuard.Agent.Core.Communication;
using RansomGuard.Agent.Core.Persistence.Entities;
using RansomGuard.Agent.Core.Persistence.Repositories;

namespace RansomGuard.Agent.Service;

/// <summary>
/// BackgroundService that monitors SENTINEL canary files for tampering.
/// Uses bounded channels (no async void) for real-time FileSystemWatcher events
/// plus periodic polling. Supports canary regeneration with anti-loop protection.
/// </summary>
public sealed class SentinelMonitor : BackgroundService
{
    private const int EventChannelCapacity = 10_000;

    private readonly ILogger<SentinelMonitor> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IFileEventDeduplicator _deduplicator;
    private readonly RestartManagerHelper _restartManager;
    private readonly IAlertForwardingQueue _alertQueue;
    private readonly AgentConfiguration _config;
    private readonly List<FileSystemWatcher> _watchers = [];
    private readonly Channel<CanaryFileEvent> _eventChannel;
    private readonly ConcurrentDictionary<string, RegenerationStats> _regenTracking = new();

    /// <summary>
    /// Initializes the SENTINEL monitor.
    /// </summary>
    public SentinelMonitor(
        ILogger<SentinelMonitor> logger,
        IOptionsMonitor<AgentConfiguration> config,
        IServiceScopeFactory scopeFactory,
        IFileEventDeduplicator deduplicator,
        RestartManagerHelper restartManager,
        IAlertForwardingQueue alertQueue)
    {
        _logger = logger;
        _config = config.CurrentValue;
        _scopeFactory = scopeFactory;
        _deduplicator = deduplicator;
        _restartManager = restartManager;
        _alertQueue = alertQueue;
        _eventChannel = Channel.CreateBounded<CanaryFileEvent>(
            new BoundedChannelOptions(EventChannelCapacity)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true
            });
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        SentinelOptions? sentinel = _config.Sentinel;
        if (sentinel is null || !sentinel.Enabled)
        {
            return;
        }

        // Wait for deployment service to finish
        await Task.Delay(2000, stoppingToken);

        // Start channel consumer
        Task consumerTask = ConsumeEventsAsync(stoppingToken);

        // Set up real-time FileSystemWatcher per canary directory
        string[] resolvedDirs = EnvironmentVariableResolver.ResolvePaths(sentinel.WatchDirectories);
        foreach (string dir in resolvedDirs)
        {
            if (!Directory.Exists(dir)) continue;

            var watcher = new FileSystemWatcher(dir)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                IncludeSubdirectories = false,
                EnableRaisingEvents = true
            };

            watcher.Changed += (_, e) => EnqueueEvent(e.FullPath, CanaryAlertType.CanaryModified, e.ChangeType);
            watcher.Deleted += (_, e) => EnqueueEvent(e.FullPath, CanaryAlertType.CanaryDeleted, e.ChangeType);
            watcher.Renamed += (_, e) => EnqueueEvent(e.OldFullPath, CanaryAlertType.CanaryRenamed, e.ChangeType);

            _watchers.Add(watcher);
        }

        _logger.LogInformation("SENTINEL monitor active — real-time watchers on {DirCount} directories, polling every {IntervalMs}ms",
            _watchers.Count, sentinel.CheckIntervalMs);

        // Periodic polling loop
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(sentinel.CheckIntervalMs, stoppingToken);
            await PeriodicIntegrityCheckAsync(stoppingToken);
        }

        // Cleanup
        _eventChannel.Writer.TryComplete();
        await consumerTask;

        foreach (FileSystemWatcher watcher in _watchers)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
        }
    }

    private void EnqueueEvent(string filePath, CanaryAlertType alertType, WatcherChangeTypes changeType)
    {
        if (!_deduplicator.ShouldProcess(filePath, changeType))
        {
            return;
        }

        if (!_eventChannel.Writer.TryWrite(new CanaryFileEvent(filePath, alertType, DateTime.UtcNow)))
        {
            _logger.LogWarning("SENTINEL event channel saturated, dropped event for {Path}", filePath);
        }
    }

    private async Task ConsumeEventsAsync(CancellationToken cancellationToken)
    {
        await foreach (CanaryFileEvent evt in _eventChannel.Reader.ReadAllAsync(cancellationToken))
        {
            try
            {
                using IServiceScope scope = _scopeFactory.CreateScope();
                var canaryRepo = scope.ServiceProvider.GetRequiredService<ISentinelCanaryRepository>();

                SentinelCanary? canary = await canaryRepo.GetByFilePathAsync(evt.FilePath, cancellationToken);
                if (canary is null || canary.Status != CanaryStatus.Active)
                {
                    continue;
                }

                await RaiseCanaryAlertAsync(canary, evt.AlertType, scope.ServiceProvider, cancellationToken);

                // Real-time regeneration for deleted canaries
                if (evt.AlertType == CanaryAlertType.CanaryDeleted && _config.Sentinel?.EnableRealTimeRegeneration == true)
                {
                    await TryRegenerateCanaryAsync(canary, scope.ServiceProvider, cancellationToken);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing SENTINEL event for {FilePath}", evt.FilePath);
            }
        }
    }

    private async Task TryRegenerateCanaryAsync(SentinelCanary deletedCanary, IServiceProvider services, CancellationToken cancellationToken)
    {
        SentinelOptions sentinel = _config.Sentinel!;

        RegenerationStats stats = _regenTracking.GetOrAdd(deletedCanary.Directory, _ => new RegenerationStats());

        // Anti-loop: check hourly limit
        stats.CleanupOlderThan(TimeSpan.FromHours(1));
        if (stats.Count >= sentinel.MaxRegenerationsPerHourPerDirectory)
        {
            _logger.LogCritical(
                "SENTINEL SUSTAINED ATTACK: {RegenCount} regenerations in 1 hour for {Directory}. Manual intervention required.",
                stats.Count, deletedCanary.Directory);

            var canaryRepo = services.GetRequiredService<ISentinelCanaryRepository>();
            var auditRepo = services.GetRequiredService<IAuditLogRepository>();

            await auditRepo.AppendAsync("SustainedAttack",
                $"Canary regeneration limit exceeded ({stats.Count}/{sentinel.MaxRegenerationsPerHourPerDirectory}) in {deletedCanary.Directory}",
                "SentinelCanary", deletedCanary.Id, cancellationToken);
            return;
        }

        // Regenerate with same template
        var canaryService = services.GetRequiredService<ICanaryFileService>();
        await canaryService.CreateCanaryAsync(
            deletedCanary.Directory, deletedCanary.TemplateUsed, sentinel.CanaryPrefix, cancellationToken);

        stats.RecordRegeneration();

        var audit = services.GetRequiredService<IAuditLogRepository>();
        await audit.AppendAsync("CanaryRegenerated",
            $"Canary regenerated in {deletedCanary.Directory} (template: {deletedCanary.TemplateUsed}) after tampering",
            "SentinelCanary", deletedCanary.Id, cancellationToken);

        _logger.LogInformation("SENTINEL canary regenerated in {Directory} after deletion (template: {Template})",
            deletedCanary.Directory, deletedCanary.TemplateUsed);
    }

    private async Task PeriodicIntegrityCheckAsync(CancellationToken cancellationToken)
    {
        using IServiceScope scope = _scopeFactory.CreateScope();
        var canaryService = scope.ServiceProvider.GetRequiredService<ICanaryFileService>();
        var canaryRepo = scope.ServiceProvider.GetRequiredService<ISentinelCanaryRepository>();

        IReadOnlyList<SentinelCanary> activeCanaries = await canaryService.GetAllCanariesAsync(cancellationToken);

        foreach (SentinelCanary canary in activeCanaries)
        {
            if (cancellationToken.IsCancellationRequested) break;

            if (!File.Exists(canary.FilePath))
            {
                await RaiseCanaryAlertAsync(canary, CanaryAlertType.CanaryDeleted, scope.ServiceProvider, cancellationToken);

                if (_config.Sentinel?.EnableRealTimeRegeneration == true)
                {
                    await TryRegenerateCanaryAsync(canary, scope.ServiceProvider, cancellationToken);
                }
                continue;
            }

            bool isIntact = await canaryService.VerifyCanaryIntegrityAsync(canary, cancellationToken);
            if (!isIntact)
            {
                await RaiseCanaryAlertAsync(canary, CanaryAlertType.CanaryModified, scope.ServiceProvider, cancellationToken);
                continue;
            }

            canary.LastCheckedAt = DateTime.UtcNow;
            await canaryRepo.UpdateAsync(canary, cancellationToken);
        }
    }

    private async Task RaiseCanaryAlertAsync(
        SentinelCanary canary,
        CanaryAlertType alertType,
        IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        var canaryRepo = services.GetRequiredService<ISentinelCanaryRepository>();
        var auditRepo = services.GetRequiredService<IAuditLogRepository>();

        ProcessAttribution? attribution = TryAttributeProcess(canary.FilePath);

        var alert = new CanaryAlert
        {
            CanaryId = canary.Id,
            CanaryPath = canary.FilePath,
            AlertType = alertType,
            Severity = AlertSeverity.Critical,
            OffendingProcessId = attribution?.ProcessId,
            OffendingProcessName = attribution?.ProcessName,
            OffendingProcessPath = attribution?.ExecutablePath
        };

        canary.Status = alertType switch
        {
            CanaryAlertType.CanaryModified => CanaryStatus.Modified,
            CanaryAlertType.CanaryDeleted => CanaryStatus.Deleted,
            CanaryAlertType.CanaryRenamed => CanaryStatus.Renamed,
            _ => CanaryStatus.Compromised
        };
        await canaryRepo.UpdateAsync(canary, cancellationToken);
        await canaryRepo.AddAlertAsync(alert, cancellationToken);

        string details = $"Canary alert: {alertType} on {canary.FilePath}";
        if (attribution is not null)
        {
            details += $" by process {attribution.ProcessName} (PID: {attribution.ProcessId})";
        }
        await auditRepo.AppendAsync("CanaryAlert", details, "CanaryAlert", alert.Id, cancellationToken);

        _logger.LogCritical(
            "SENTINEL ALERT: {AlertType} on canary {CanaryPath} | Process: {ProcessName} (PID: {ProcessId}) | Severity: {Severity}",
            alertType, canary.FilePath, attribution?.ProcessName ?? "unknown",
            attribution?.ProcessId.ToString() ?? "N/A", alert.Severity);

        // Forward to GRID
        _alertQueue.TryEnqueue(AlertMapper.FromCanaryAlert(alert));

        // Fire-and-forget genealogy enrichment for process attribution
        _ = Task.Run(async () =>
        {
            try
            {
                using var enrichScope = _scopeFactory.CreateScope();
                var enricher = enrichScope.ServiceProvider.GetRequiredService<IGenealogyEnricher>();
                await enricher.EnrichAlertAsync(alert.Id, canary.FilePath, default);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Genealogy enrichment failed for canary alert {AlertId}", alert.Id);
            }
        }, CancellationToken.None);
    }

    private ProcessAttribution? TryAttributeProcess(string filePath)
    {
        try
        {
            IReadOnlyList<ProcessAttribution> processes = _restartManager.GetProcessesLockingFile(filePath);
            if (processes.Count == 0)
            {
                return null;
            }

            ProcessAttribution offender = processes
                .OrderByDescending(p => p.StartTime ?? DateTime.MinValue)
                .First();

            _logger.LogInformation(
                "Process attribution: {ProcessName} (PID: {ProcessId}) identified for {FilePath}",
                offender.ProcessName, offender.ProcessId, filePath);

            return offender;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Process attribution failed for {FilePath}", filePath);
            return null;
        }
    }
}

/// <summary>
/// Represents a canary file event from FileSystemWatcher, queued via bounded channel.
/// </summary>
internal sealed record CanaryFileEvent(string FilePath, CanaryAlertType AlertType, DateTime Timestamp);

/// <summary>
/// Tracks canary regeneration attempts per directory for anti-loop protection.
/// </summary>
internal sealed class RegenerationStats
{
    private readonly List<DateTime> _timestamps = [];
    private readonly object _lock = new();

    /// <summary>
    /// Current count of regenerations within the tracking window.
    /// </summary>
    public int Count
    {
        get { lock (_lock) return _timestamps.Count; }
    }

    /// <summary>
    /// Records a new regeneration event.
    /// </summary>
    public void RecordRegeneration()
    {
        lock (_lock) _timestamps.Add(DateTime.UtcNow);
    }

    /// <summary>
    /// Removes entries older than the specified window.
    /// </summary>
    public void CleanupOlderThan(TimeSpan window)
    {
        DateTime cutoff = DateTime.UtcNow - window;
        lock (_lock) _timestamps.RemoveAll(t => t < cutoff);
    }
}
