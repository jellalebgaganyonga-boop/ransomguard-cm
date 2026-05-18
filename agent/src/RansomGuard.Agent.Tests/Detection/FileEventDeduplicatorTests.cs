using RansomGuard.Agent.Core.Detection;
using Shouldly;

namespace RansomGuard.Agent.Tests.Detection;

/// <summary>
/// Tests for <see cref="FileEventDeduplicator"/> time-windowed deduplication.
/// </summary>
public sealed class FileEventDeduplicatorTests : IDisposable
{
    private readonly FileEventDeduplicator _deduplicator;

    public FileEventDeduplicatorTests()
    {
        _deduplicator = new FileEventDeduplicator(windowMs: 500);
    }

    [Fact]
    public void First_event_should_always_be_processed()
    {
        _deduplicator.ShouldProcess(@"C:\test\file.txt", WatcherChangeTypes.Created).ShouldBeTrue();
    }

    [Fact]
    public void Duplicate_event_within_window_should_be_filtered()
    {
        _deduplicator.ShouldProcess(@"C:\test\file.txt", WatcherChangeTypes.Changed);
        _deduplicator.ShouldProcess(@"C:\test\file.txt", WatcherChangeTypes.Changed).ShouldBeFalse();
    }

    [Fact]
    public void Same_event_outside_window_should_be_processed()
    {
        using var shortWindow = new FileEventDeduplicator(windowMs: 50);
        shortWindow.ShouldProcess(@"C:\test\file.txt", WatcherChangeTypes.Changed);
        Thread.Sleep(100);
        shortWindow.ShouldProcess(@"C:\test\file.txt", WatcherChangeTypes.Changed).ShouldBeTrue();
    }

    [Fact]
    public void Different_change_types_on_same_file_should_both_be_processed()
    {
        _deduplicator.ShouldProcess(@"C:\test\file.txt", WatcherChangeTypes.Created);
        _deduplicator.ShouldProcess(@"C:\test\file.txt", WatcherChangeTypes.Changed).ShouldBeTrue();
    }

    [Fact]
    public void Different_files_with_same_change_type_should_both_be_processed()
    {
        _deduplicator.ShouldProcess(@"C:\test\file1.txt", WatcherChangeTypes.Changed);
        _deduplicator.ShouldProcess(@"C:\test\file2.txt", WatcherChangeTypes.Changed).ShouldBeTrue();
    }

    [Fact]
    public void Multiple_duplicates_should_all_be_filtered()
    {
        _deduplicator.ShouldProcess(@"C:\test\file.txt", WatcherChangeTypes.Changed);
        _deduplicator.ShouldProcess(@"C:\test\file.txt", WatcherChangeTypes.Changed).ShouldBeFalse();
        _deduplicator.ShouldProcess(@"C:\test\file.txt", WatcherChangeTypes.Changed).ShouldBeFalse();
        _deduplicator.ShouldProcess(@"C:\test\file.txt", WatcherChangeTypes.Changed).ShouldBeFalse();
    }

    [Fact]
    public void Thread_safety_with_parallel_events()
    {
        int processedCount = 0;
        Parallel.For(0, 1000, _ =>
        {
            if (_deduplicator.ShouldProcess(@"C:\test\concurrent.txt", WatcherChangeTypes.Changed))
                Interlocked.Increment(ref processedCount);
        });
        processedCount.ShouldBe(1);
    }

    [Fact]
    public void Window_boundary_should_be_respected()
    {
        using var shortWindow = new FileEventDeduplicator(windowMs: 100);

        shortWindow.ShouldProcess(@"C:\test\file.txt", WatcherChangeTypes.Changed).ShouldBeTrue();
        Thread.Sleep(30);
        shortWindow.ShouldProcess(@"C:\test\file.txt", WatcherChangeTypes.Changed).ShouldBeFalse();
        Thread.Sleep(150);
        shortWindow.ShouldProcess(@"C:\test\file.txt", WatcherChangeTypes.Changed).ShouldBeTrue();
    }

    [Fact]
    public void Renamed_events_should_be_deduplicated_independently()
    {
        _deduplicator.ShouldProcess(@"C:\test\file.txt", WatcherChangeTypes.Renamed);
        _deduplicator.ShouldProcess(@"C:\test\file.txt", WatcherChangeTypes.Renamed).ShouldBeFalse();
        _deduplicator.ShouldProcess(@"C:\test\file.txt", WatcherChangeTypes.Deleted).ShouldBeTrue();
    }

    public void Dispose()
    {
        _deduplicator.Dispose();
    }
}
