using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RansomGuard.Agent.Core.Configuration;
using RansomGuard.Agent.Core.Detection;
using RansomGuard.Agent.Core.Detection.Sentinel;
using RansomGuard.Agent.Core.Persistence.Entities;
using RansomGuard.Agent.Core.Persistence.Repositories;

namespace RansomGuard.Agent.Service;

/// <summary>
/// BackgroundService that monitors SENTINEL canary files for tampering.
/// Uses both periodic polling (configurable interval) and real-time FileSystemWatcher
/// for sub-100ms detection latency.
/// </summary>
public sealed class SentinelMonitor : BackgroundService
{
    private readonly ILogger<SentinelMonitor> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IFileEventDeduplicator _deduplicator;
    private readonly AgentConfiguration _config;
    private readonly List<FileSystemWatcher> _watchers = [];

    /// <summary>
    /// Initializes the SENTINEL monitor.
    /// </summary>
    public SentinelMonitor(
        ILogger<SentinelMonitor> logger,
        IOptionsMonitor<AgentConfiguration> config,
        IServiceScopeFactory scopeFactory,
        IFileEventDeduplicator deduplicator)
    {
        _logger = logger;
        _config = config.CurrentValue;
        _scopeFactory = scopeFactory;
        _deduplicator = deduplicator;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        SentinelOptions? sentinel = _config.Sentinel;

        if (sentinel is null || !sentinel.Enabled)
        {
            return;
        }

        // Small delay to let deployment service finish
        await Task.Delay(2000, stoppingToken);

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

            watcher.Changed += (s, e) => OnCanaryFileEvent(e.FullPath, CanaryAlertType.CanaryModified, e.ChangeType);
            watcher.Deleted += (s, e) => OnCanaryFileEvent(e.FullPath, CanaryAlertType.CanaryDeleted, e.ChangeType);
            watcher.Renamed += (s, e) => OnCanaryFileEvent(e.OldFullPath, CanaryAlertType.CanaryRenamed, e.ChangeType);

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

        foreach (FileSystemWatcher watcher in _watchers)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
        }
    }

    private async void OnCanaryFileEvent(string filePath, CanaryAlertType alertType, WatcherChangeTypes changeType)
    {
        if (!_deduplicator.ShouldProcess(filePath, changeType))
        {
            return;
        }

        try
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            var canaryRepo = scope.ServiceProvider.GetRequiredService<ISentinelCanaryRepository>();

            SentinelCanary? canary = await canaryRepo.GetByFilePathAsync(filePath);
            if (canary is null || canary.Status != CanaryStatus.Active)
            {
                return; // Not a tracked canary
            }

            await RaiseCanaryAlertAsync(canary, alertType, scope.ServiceProvider);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing SENTINEL event for {FilePath}", filePath);
        }
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

        // Attempt process attribution
        (int? pid, string? procName, string? procPath) = TryAttributeProcess(canary.FilePath);

        var alert = new CanaryAlert
        {
            CanaryId = canary.Id,
            CanaryPath = canary.FilePath,
            AlertType = alertType,
            Severity = AlertSeverity.Critical,
            OffendingProcessId = pid,
            OffendingProcessName = procName,
            OffendingProcessPath = procPath
        };

        // Update canary status
        canary.Status = alertType switch
        {
            CanaryAlertType.CanaryModified => CanaryStatus.Modified,
            CanaryAlertType.CanaryDeleted => CanaryStatus.Deleted,
            CanaryAlertType.CanaryRenamed => CanaryStatus.Renamed,
            _ => CanaryStatus.Compromised
        };
        await canaryRepo.UpdateAsync(canary, cancellationToken);

        // Persist alert
        await canaryRepo.AddAlertAsync(alert, cancellationToken);

        // Write to immutable audit log
        string details = $"Canary alert: {alertType} on {canary.FilePath}";
        if (procName is not null)
        {
            details += $" by process {procName} (PID: {pid})";
        }
        await auditRepo.AppendAsync("CanaryAlert", details, "CanaryAlert", alert.Id, cancellationToken);

        _logger.LogCritical(
            "SENTINEL ALERT: {AlertType} on canary {CanaryPath} | Process: {ProcessName} (PID: {ProcessId}) | Severity: {Severity}",
            alertType, canary.FilePath, procName ?? "unknown", pid?.ToString() ?? "N/A", alert.Severity);
    }

    private static (int? Pid, string? Name, string? Path) TryAttributeProcess(string filePath)
    {
        try
        {
            Process[] processes = Process.GetProcesses();
            foreach (Process process in processes)
            {
                try
                {
                    if (process.MainModule?.FileName is not null)
                    {
                        // Basic heuristic: check if any process has a handle to files in the same directory
                        // Full file handle attribution would require kernel-level APIs (ETW, NtQuerySystemInformation)
                        // This is a best-effort approach for the POC
                    }
                }
                catch
                {
                    // Access denied for system processes — expected
                }
            }
        }
        catch
        {
            // Process enumeration failed
        }

        return (null, null, null);
    }
}
