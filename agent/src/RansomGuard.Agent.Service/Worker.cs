using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RansomGuard.Agent.Core.Configuration;
using RansomGuard.Agent.Core.Detection;
using RansomGuard.Agent.Core.Persistence.Entities;
using RansomGuard.Agent.Core.Persistence.Repositories;

namespace RansomGuard.Agent.Service;

/// <summary>
/// RansomGuard Agent Worker Service — v0.3.1
///
/// Monitors file system events in configured watch paths using FileSystemWatcher.
/// Events are deduplicated, buffered via bounded channels, and persisted asynchronously to SQLite.
/// </summary>
public sealed class Worker : BackgroundService
{
    private const int EventChannelCapacity = 10_000;

    private readonly ILogger<Worker> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IFileEventDeduplicator _deduplicator;
    private readonly AgentConfiguration _config;
    private readonly List<FileSystemWatcher> _watchers = [];
    private readonly Channel<DetectionEvent> _eventChannel;
    private long _eventCount;
    private long _duplicatesFiltered;
    private readonly DateTime _startTime = DateTime.UtcNow;

    /// <summary>
    /// Initializes the worker with configuration, logging, deduplicator, and DI scope factory.
    /// </summary>
    /// <param name="logger">Structured logger instance.</param>
    /// <param name="config">Agent configuration from Options pattern.</param>
    /// <param name="scopeFactory">Service scope factory for creating scoped repositories.</param>
    /// <param name="deduplicator">File event deduplicator to filter duplicate FSW events.</param>
    public Worker(
        ILogger<Worker> logger,
        IOptionsMonitor<AgentConfiguration> config,
        IServiceScopeFactory scopeFactory,
        IFileEventDeduplicator deduplicator)
    {
        _logger = logger;
        _config = config.CurrentValue;
        _scopeFactory = scopeFactory;
        _deduplicator = deduplicator;
        _eventChannel = Channel.CreateBounded<DetectionEvent>(
            new BoundedChannelOptions(EventChannelCapacity)
            {
                FullMode = BoundedChannelFullMode.DropOldest
            });
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("RansomGuard-CM Agent v{Version} starting", _config.Identity.Version);
        _logger.LogInformation("Environment: {Environment}", _config.Identity.Environment);

        Task persistenceTask = ConsumeEventsAsync(stoppingToken);

        string[] resolvedPaths = EnvironmentVariableResolver.ResolvePaths(_config.Detection.WatchPaths);

        if (!_config.Detection.EnableFileSystemWatcher)
        {
            _logger.LogInformation("FileSystemWatcher detection is disabled by configuration");
            await Task.Delay(Timeout.Infinite, stoppingToken);
            return;
        }

        foreach (string watchPath in resolvedPaths)
        {
            if (!Directory.Exists(watchPath))
            {
                _logger.LogWarning("Watch path does not exist, creating: {Path}", watchPath);
                Directory.CreateDirectory(watchPath);
            }

            var watcher = new FileSystemWatcher(watchPath)
            {
                NotifyFilter = NotifyFilters.FileName
                             | NotifyFilters.LastWrite
                             | NotifyFilters.Size
                             | NotifyFilters.Attributes
                             | NotifyFilters.CreationTime,
                IncludeSubdirectories = true,
                EnableRaisingEvents = true
            };

            watcher.Created += OnFileCreated;
            watcher.Changed += OnFileChanged;
            watcher.Deleted += OnFileDeleted;
            watcher.Renamed += OnFileRenamed;
            watcher.Error += OnWatcherError;

            _watchers.Add(watcher);
            _logger.LogInformation("File system monitoring ACTIVE on: {Path}", watchPath);
        }

        int heartbeatSeconds = _config.Server.HeartbeatIntervalSeconds;

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(heartbeatSeconds), stoppingToken);

            TimeSpan uptime = DateTime.UtcNow - _startTime;
            _logger.LogInformation(
                "Heartbeat | Uptime: {Uptime:hh\\:mm\\:ss} | Events captured: {EventCount} | Duplicates filtered: {DuplicatesFiltered}",
                uptime, _eventCount, _duplicatesFiltered);
        }

        _eventChannel.Writer.Complete();
        await persistenceTask;

        foreach (FileSystemWatcher watcher in _watchers)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
        }

        _logger.LogInformation("RansomGuard Agent stopped. Total events: {EventCount} | Duplicates filtered: {DuplicatesFiltered}",
            _eventCount, _duplicatesFiltered);
    }

    private async Task ConsumeEventsAsync(CancellationToken cancellationToken)
    {
        await foreach (DetectionEvent detectionEvent in _eventChannel.Reader.ReadAllAsync(cancellationToken))
        {
            try
            {
                using IServiceScope scope = _scopeFactory.CreateScope();
                var repository = scope.ServiceProvider.GetRequiredService<IDetectionEventRepository>();
                await repository.AddAsync(detectionEvent, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to persist detection event {EventId}", detectionEvent.Id);
            }
        }
    }

    private void EnqueueEvent(string eventType, string filePath, WatcherChangeTypes changeType, string? oldFilePath = null)
    {
        if (!_deduplicator.ShouldProcess(filePath, changeType))
        {
            Interlocked.Increment(ref _duplicatesFiltered);
            return;
        }

        Interlocked.Increment(ref _eventCount);

        var detectionEvent = new DetectionEvent
        {
            EventType = eventType,
            FilePath = filePath,
            OldFilePath = oldFilePath,
            Timestamp = DateTime.UtcNow
        };

        if (!_eventChannel.Writer.TryWrite(detectionEvent))
        {
            _logger.LogWarning("Event channel full, event dropped: {EventType} on {FilePath}", eventType, filePath);
        }

        _logger.LogInformation(
            "File event detected: {EventType} on {FilePath} at {Timestamp}",
            eventType, filePath, detectionEvent.Timestamp);
    }

    private void OnFileCreated(object sender, FileSystemEventArgs e) =>
        EnqueueEvent("Created", e.FullPath, e.ChangeType);

    private void OnFileChanged(object sender, FileSystemEventArgs e) =>
        EnqueueEvent("Modified", e.FullPath, e.ChangeType);

    private void OnFileDeleted(object sender, FileSystemEventArgs e) =>
        EnqueueEvent("Deleted", e.FullPath, e.ChangeType);

    private void OnFileRenamed(object sender, RenamedEventArgs e) =>
        EnqueueEvent("Renamed", e.FullPath, e.ChangeType, e.OldFullPath);

    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        _logger.LogError(e.GetException(),
            "FileSystemWatcher error occurred. Buffer may have overflowed");
    }
}
