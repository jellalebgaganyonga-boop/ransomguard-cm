using Microsoft.Extensions.Options;
using RansomGuard.Agent.Core.Configuration;

namespace RansomGuard.Agent.Service;

/// <summary>
/// RansomGuard Agent Worker Service — v0.3.0
///
/// Monitors file system events in configured watch paths using FileSystemWatcher.
/// Configuration-driven: all paths and settings come from appsettings.json via Options pattern.
/// </summary>
public sealed class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly AgentConfiguration _config;
    private readonly List<FileSystemWatcher> _watchers = [];
    private long _eventCount;
    private readonly DateTime _startTime = DateTime.UtcNow;

    /// <summary>
    /// Initializes the worker with configuration and logging dependencies.
    /// </summary>
    /// <param name="logger">Structured logger instance.</param>
    /// <param name="config">Agent configuration from Options pattern.</param>
    public Worker(ILogger<Worker> logger, IOptionsMonitor<AgentConfiguration> config)
    {
        _logger = logger;
        _config = config.CurrentValue;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("RansomGuard-CM Agent v{Version} starting", _config.Identity.Version);
        _logger.LogInformation("Environment: {Environment}", _config.Identity.Environment);

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
                "Heartbeat | Uptime: {Uptime:hh\\:mm\\:ss} | Events: {EventCount}",
                uptime, _eventCount);
        }

        foreach (FileSystemWatcher watcher in _watchers)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
        }

        _logger.LogInformation("RansomGuard Agent stopped. Total events: {EventCount}", _eventCount);
    }

    private void OnFileCreated(object sender, FileSystemEventArgs e)
    {
        Interlocked.Increment(ref _eventCount);
        _logger.LogInformation(
            "File event detected: {EventType} on {FilePath} at {Timestamp}",
            "Created", e.FullPath, DateTime.UtcNow);
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        Interlocked.Increment(ref _eventCount);
        _logger.LogInformation(
            "File event detected: {EventType} on {FilePath} at {Timestamp}",
            "Modified", e.FullPath, DateTime.UtcNow);
    }

    private void OnFileDeleted(object sender, FileSystemEventArgs e)
    {
        Interlocked.Increment(ref _eventCount);
        _logger.LogWarning(
            "File event detected: {EventType} on {FilePath} at {Timestamp}",
            "Deleted", e.FullPath, DateTime.UtcNow);
    }

    private void OnFileRenamed(object sender, RenamedEventArgs e)
    {
        Interlocked.Increment(ref _eventCount);
        _logger.LogWarning(
            "File event detected: {EventType} on {FilePath} from {OldPath} at {Timestamp}",
            "Renamed", e.FullPath, e.OldFullPath, DateTime.UtcNow);
    }

    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        _logger.LogError(e.GetException(),
            "FileSystemWatcher error occurred. Buffer may have overflowed");
    }
}
