namespace RansomGuard.Agent.Core.Detection;

/// <summary>
/// Deduplicates file system events within a configurable time window.
/// FileSystemWatcher on Windows fires 2-4 events per file operation;
/// this filter eliminates duplicates to prevent false alerts.
/// </summary>
public interface IFileEventDeduplicator
{
    /// <summary>
    /// Determines whether a file event should be processed or filtered as a duplicate.
    /// </summary>
    /// <param name="fullPath">Full path of the affected file.</param>
    /// <param name="changeType">Type of change (Created, Changed, Deleted, Renamed).</param>
    /// <returns>True if the event should be processed; false if it is a duplicate.</returns>
    bool ShouldProcess(string fullPath, WatcherChangeTypes changeType);
}
