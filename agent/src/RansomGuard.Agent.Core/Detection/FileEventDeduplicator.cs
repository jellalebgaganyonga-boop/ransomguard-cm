using System.Collections.Concurrent;

namespace RansomGuard.Agent.Core.Detection;

/// <summary>
/// Time-windowed deduplication of FileSystemWatcher events using a concurrent dictionary
/// with sliding expiration. Events with the same path and change type within the window
/// are treated as duplicates (Windows FSW fires 2-4 events per file operation).
/// </summary>
public sealed class FileEventDeduplicator : IFileEventDeduplicator, IDisposable
{
    private readonly ConcurrentDictionary<string, DateTime> _recentEvents = new();
    private readonly TimeSpan _window;
    private readonly Timer _cleanupTimer;

    /// <summary>
    /// Initializes a new deduplicator with the specified window in milliseconds.
    /// </summary>
    /// <param name="windowMs">Deduplication window in milliseconds.</param>
    public FileEventDeduplicator(int windowMs)
    {
        _window = TimeSpan.FromMilliseconds(windowMs);
        // Periodic cleanup every 10 seconds to prevent unbounded growth
        _cleanupTimer = new Timer(CleanupExpired, null, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));
    }

    /// <inheritdoc />
    public bool ShouldProcess(string fullPath, WatcherChangeTypes changeType)
    {
        string key = BuildKey(fullPath, changeType);
        DateTime now = DateTime.UtcNow;

        if (_recentEvents.TryGetValue(key, out DateTime lastSeen))
        {
            if (now - lastSeen < _window)
            {
                // Duplicate within window — update timestamp and skip
                _recentEvents[key] = now;
                return false;
            }
        }

        // New event or expired window — record and process
        _recentEvents[key] = now;
        return true;
    }

    /// <inheritdoc cref="IDisposable.Dispose"/>
    public void Dispose()
    {
        _cleanupTimer.Dispose();
    }

    private static string BuildKey(string fullPath, WatcherChangeTypes changeType)
    {
        return $"{changeType}|{fullPath}";
    }

    private void CleanupExpired(object? state)
    {
        DateTime cutoff = DateTime.UtcNow - _window;
        foreach (var kvp in _recentEvents)
        {
            if (kvp.Value < cutoff)
            {
                _recentEvents.TryRemove(kvp.Key, out _);
            }
        }
    }
}
