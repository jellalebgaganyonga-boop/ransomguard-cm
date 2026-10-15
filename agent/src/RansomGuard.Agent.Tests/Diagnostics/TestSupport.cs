using Microsoft.Extensions.Logging;

namespace RansomGuard.Agent.Tests.Diagnostics;

/// <summary>
/// A clock the test moves by hand: monotonic timestamps, wall clock and timers.
/// Advancing it fires the timers that fall due, synchronously.
/// </summary>
internal sealed class ManualTimeProvider : TimeProvider
{
    private readonly object _gate = new();
    private readonly List<ManualTimer> _timers = [];
    private long _timestamp = TimeSpan.FromHours(3).Ticks; // never 0, like a real Stopwatch
    private DateTimeOffset _utcNow = new(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp()
    {
        lock (_gate) return _timestamp;
    }

    public override DateTimeOffset GetUtcNow()
    {
        lock (_gate) return _utcNow;
    }

    /// <summary>Timers currently waiting to fire: a loop is parked on its next tick.</summary>
    public int ActiveTimers
    {
        get { lock (_gate) return _timers.Count(t => t.DueAt is not null); }
    }

    /// <summary>Moves the wall clock only (an NTP correction, a user changing the time).</summary>
    public void SetWallClock(DateTimeOffset utcNow)
    {
        lock (_gate) _utcNow = utcNow;
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(this, callback, state);
        lock (_gate) _timers.Add(timer);
        timer.Change(dueTime, period);
        return timer;
    }

    /// <summary>Moves both clocks forward and fires every timer that falls due.</summary>
    public void Advance(TimeSpan by)
    {
        List<ManualTimer> due;
        lock (_gate)
        {
            _timestamp += by.Ticks;
            _utcNow += by;
            due = _timers.Where(t => t.DueAt is long at && at <= _timestamp).ToList();
            foreach (ManualTimer timer in due)
            {
                timer.DueAt = timer.Period is TimeSpan p ? _timestamp + p.Ticks : null;
            }
        }

        foreach (ManualTimer timer in due)
        {
            timer.Fire();
        }
    }

    private void Remove(ManualTimer timer)
    {
        lock (_gate) _timers.Remove(timer);
    }

    private long Now
    {
        get { lock (_gate) return _timestamp; }
    }

    private sealed class ManualTimer(ManualTimeProvider owner, TimerCallback callback, object? state) : ITimer
    {
        public long? DueAt { get; set; }

        public TimeSpan? Period { get; private set; }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            DueAt = dueTime == Timeout.InfiniteTimeSpan ? null : owner.Now + dueTime.Ticks;
            Period = period == Timeout.InfiniteTimeSpan ? null : period;
            return true;
        }

        public void Fire() => callback(state);

        public void Dispose()
        {
            DueAt = null;
            owner.Remove(this);
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}

/// <summary>Keeps every log entry: level, rendered message, exception.</summary>
internal sealed class RecordingLogger<T> : ILogger<T>
{
    private readonly List<(LogLevel Level, string Message, Exception? Exception)> _entries = [];

    public IReadOnlyList<(LogLevel Level, string Message, Exception? Exception)> Entries
    {
        get { lock (_entries) return _entries.ToList(); }
    }

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        lock (_entries) _entries.Add((logLevel, formatter(state, exception), exception));
    }
}

internal static class Eventually
{
    /// <summary>Waits until the condition holds, or fails the test after the timeout.</summary>
    public static async Task HoldsAsync(Func<bool> condition, string what, int timeoutMs = 15_000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"Timed out waiting for: {what}");
            }

            await Task.Delay(20);
        }
    }
}
