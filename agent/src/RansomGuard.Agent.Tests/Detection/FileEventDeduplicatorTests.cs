using FluentAssertions;
using RansomGuard.Agent.Core.Detection;

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
        bool result = _deduplicator.ShouldProcess(@"C:\test\file.txt", WatcherChangeTypes.Created);

        result.Should().BeTrue();
    }

    [Fact]
    public void Duplicate_event_within_window_should_be_filtered()
    {
        _deduplicator.ShouldProcess(@"C:\test\file.txt", WatcherChangeTypes.Changed);

        bool duplicate = _deduplicator.ShouldProcess(@"C:\test\file.txt", WatcherChangeTypes.Changed);

        duplicate.Should().BeFalse();
    }

    [Fact]
    public void Same_event_outside_window_should_be_processed()
    {
        using var shortWindow = new FileEventDeduplicator(windowMs: 50);

        shortWindow.ShouldProcess(@"C:\test\file.txt", WatcherChangeTypes.Changed);
        Thread.Sleep(100); // Exceed the 50ms window

        bool result = shortWindow.ShouldProcess(@"C:\test\file.txt", WatcherChangeTypes.Changed);

        result.Should().BeTrue();
    }

    [Fact]
    public void Different_change_types_on_same_file_should_both_be_processed()
    {
        _deduplicator.ShouldProcess(@"C:\test\file.txt", WatcherChangeTypes.Created);

        bool changed = _deduplicator.ShouldProcess(@"C:\test\file.txt", WatcherChangeTypes.Changed);

        changed.Should().BeTrue();
    }

    [Fact]
    public void Different_files_with_same_change_type_should_both_be_processed()
    {
        _deduplicator.ShouldProcess(@"C:\test\file1.txt", WatcherChangeTypes.Changed);

        bool result = _deduplicator.ShouldProcess(@"C:\test\file2.txt", WatcherChangeTypes.Changed);

        result.Should().BeTrue();
    }

    [Fact]
    public void Multiple_duplicates_should_all_be_filtered()
    {
        _deduplicator.ShouldProcess(@"C:\test\file.txt", WatcherChangeTypes.Changed);

        bool dup1 = _deduplicator.ShouldProcess(@"C:\test\file.txt", WatcherChangeTypes.Changed);
        bool dup2 = _deduplicator.ShouldProcess(@"C:\test\file.txt", WatcherChangeTypes.Changed);
        bool dup3 = _deduplicator.ShouldProcess(@"C:\test\file.txt", WatcherChangeTypes.Changed);

        dup1.Should().BeFalse();
        dup2.Should().BeFalse();
        dup3.Should().BeFalse();
    }

    [Fact]
    public void Thread_safety_with_parallel_events()
    {
        int processedCount = 0;
        int totalAttempts = 1000;

        Parallel.For(0, totalAttempts, _ =>
        {
            if (_deduplicator.ShouldProcess(@"C:\test\concurrent.txt", WatcherChangeTypes.Changed))
            {
                Interlocked.Increment(ref processedCount);
            }
        });

        // Only the first event should be processed; all others are within the 500ms window
        processedCount.Should().Be(1);
    }

    [Fact]
    public void Window_boundary_should_be_respected()
    {
        using var shortWindow = new FileEventDeduplicator(windowMs: 100);

        bool first = shortWindow.ShouldProcess(@"C:\test\file.txt", WatcherChangeTypes.Changed);
        first.Should().BeTrue();

        // Within window
        Thread.Sleep(30);
        bool withinWindow = shortWindow.ShouldProcess(@"C:\test\file.txt", WatcherChangeTypes.Changed);
        withinWindow.Should().BeFalse();

        // Wait past window
        Thread.Sleep(150);
        bool afterWindow = shortWindow.ShouldProcess(@"C:\test\file.txt", WatcherChangeTypes.Changed);
        afterWindow.Should().BeTrue();
    }

    [Fact]
    public void Renamed_events_should_be_deduplicated_independently()
    {
        _deduplicator.ShouldProcess(@"C:\test\file.txt", WatcherChangeTypes.Renamed);

        bool dup = _deduplicator.ShouldProcess(@"C:\test\file.txt", WatcherChangeTypes.Renamed);
        bool deleted = _deduplicator.ShouldProcess(@"C:\test\file.txt", WatcherChangeTypes.Deleted);

        dup.Should().BeFalse();
        deleted.Should().BeTrue();
    }

    public void Dispose()
    {
        _deduplicator.Dispose();
    }
}
