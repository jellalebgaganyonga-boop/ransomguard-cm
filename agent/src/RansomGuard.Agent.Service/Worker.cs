using System.Collections.Concurrent;

namespace RansomGuard.Agent.Service;

/// <summary>
/// RansomGuard Agent Worker Service — POC v0.1
///
/// Validates Hypothesis 1: Real-time file system monitoring on Windows 10/11
/// using .NET 8 FileSystemWatcher with acceptable performance characteristics.
///
/// POC Scope:
///   - Detect Created, Modified, Deleted, Renamed events
///   - Watch a predefined hospital test folder on user's desktop
///   - Log events with timestamps in structured format
///   - Demonstrate event-driven architecture viability
///
/// Known POC Limitations (will be addressed in production):
///   - FileSystemWatcher may miss events under extreme load (>10k events/sec)
///   - Will migrate to ETW kernel-mode tracing for production
///   - No deduplication of duplicate events
///   - No persistence — events only logged to console
///
/// Performance Target (Hypothesis 1 validation):
///   - CPU: less than 5% on idle monitoring
///   - RAM: less than 200 MB working set
///   - Latency: event detection less than 100ms after file operation
/// </summary>
public sealed class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly string _watchPath;
    private FileSystemWatcher? _watcher;
    private long _eventCount;
    private readonly DateTime _startTime = DateTime.UtcNow;

    public Worker(ILogger<Worker> logger)
    {
        _logger = logger;
        _watchPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            "RansomGuard-TestZone");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("================================================================");
        _logger.LogInformation("RansomGuard-CM Agent — POC v0.1 Starting");
        _logger.LogInformation("================================================================");
        _logger.LogInformation("Watch path: {Path}", _watchPath);

        // Validate watch path exists
        if (!Directory.Exists(_watchPath))
        {
            _logger.LogError("Watch path does not exist: {Path}", _watchPath);
            _logger.LogError("Please create the folder before running the agent.");
            return;
        }

        // Initialize the file system watcher
        _watcher = new FileSystemWatcher(_watchPath)
        {
            NotifyFilter = NotifyFilters.FileName
                         | NotifyFilters.LastWrite
                         | NotifyFilters.Size
                         | NotifyFilters.Attributes
                         | NotifyFilters.CreationTime,
            IncludeSubdirectories = true,
            EnableRaisingEvents = true
        };

        // Wire up event handlers
        _watcher.Created  += OnFileCreated;
        _watcher.Changed  += OnFileChanged;
        _watcher.Deleted  += OnFileDeleted;
        _watcher.Renamed  += OnFileRenamed;
        _watcher.Error    += OnWatcherError;

        _logger.LogInformation("File system monitoring ACTIVE");
        _logger.LogInformation("================================================================");
        _logger.LogInformation("Try creating, modifying, or deleting files in: {Path}", _watchPath);
        _logger.LogInformation("Press Ctrl+C to stop the agent");
        _logger.LogInformation("================================================================");

        // Keep the service alive — log a heartbeat every 30 seconds
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

            var uptime = DateTime.UtcNow - _startTime;
            _logger.LogInformation(
                "[HEARTBEAT] Uptime: {Uptime:hh\\:mm\\:ss} | Events captured: {Count}",
                uptime, _eventCount);
        }

        // Cleanup on shutdown
        _watcher.EnableRaisingEvents = false;
        _watcher.Dispose();
        _logger.LogInformation("RansomGuard Agent stopped. Total events: {Count}", _eventCount);
    }

    private void OnFileCreated(object sender, FileSystemEventArgs e)
    {
        Interlocked.Increment(ref _eventCount);
        _logger.LogInformation(
            "[CREATED]  #{Count:D5} | {Time:HH:mm:ss.fff} | {Path}",
            _eventCount, DateTime.Now, e.FullPath);
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        Interlocked.Increment(ref _eventCount);
        _logger.LogInformation(
            "[MODIFIED] #{Count:D5} | {Time:HH:mm:ss.fff} | {Path}",
            _eventCount, DateTime.Now, e.FullPath);
    }

    private void OnFileDeleted(object sender, FileSystemEventArgs e)
    {
        Interlocked.Increment(ref _eventCount);
        _logger.LogWarning(
            "[DELETED]  #{Count:D5} | {Time:HH:mm:ss.fff} | {Path}",
            _eventCount, DateTime.Now, e.FullPath);
    }

    private void OnFileRenamed(object sender, RenamedEventArgs e)
    {
        Interlocked.Increment(ref _eventCount);
        _logger.LogWarning(
            "[RENAMED]  #{Count:D5} | {Time:HH:mm:ss.fff} | {Old} -> {New}",
            _eventCount, DateTime.Now, e.OldFullPath, e.FullPath);
    }

    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        _logger.LogError(e.GetException(),
            "FileSystemWatcher error occurred. Buffer may have overflowed.");
    }
}
