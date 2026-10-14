namespace RansomGuard.Agent.Core.Diagnostics;

/// <summary>
/// Raised when one of a module's long-running tasks ends on its own, without an
/// exception, before shutdown was requested. For a detector, a consumer loop
/// that simply returns is as much a failure as one that throws: it stops
/// seeing events either way.
/// </summary>
public sealed class ModuleTaskEndedException : Exception
{
    /// <summary>Creates the exception with a default message.</summary>
    public ModuleTaskEndedException()
        : base("A module task ended before shutdown was requested.")
    {
        TaskName = string.Empty;
    }

    /// <summary>Creates the exception with a message.</summary>
    public ModuleTaskEndedException(string message)
        : base(message)
    {
        TaskName = string.Empty;
    }

    /// <summary>Creates the exception with a message and an inner exception.</summary>
    public ModuleTaskEndedException(string message, Exception innerException)
        : base(message, innerException)
    {
        TaskName = string.Empty;
    }

    private ModuleTaskEndedException(string taskName, string message)
        : base(message)
    {
        TaskName = taskName;
    }

    /// <summary>Name of the task that ended (e.g. "consumer", "watchers"); empty if unknown.</summary>
    public string TaskName { get; }

    /// <summary>Creates the exception for the named task that ended.</summary>
    public static ModuleTaskEndedException ForTask(string taskName) =>
        new(taskName, $"Module task '{taskName}' ended before shutdown was requested.");
}

/// <summary>
/// Watches every long-running task of a detection module, so that a failure
/// anywhere — not only in the loop the module happens to await — is seen.
/// </summary>
/// <remarks>
/// Before this existed each monitor awaited a single main loop and only joined
/// its consumer task at shutdown. A consumer that died, or a file watcher that
/// stopped, left the module reading "active" while it was blind.
/// </remarks>
public static class ModuleTaskObserver
{
    /// <summary>
    /// Completes normally when <paramref name="stoppingToken"/> is cancelled.
    /// If any task ends first, rethrows its exception, or throws
    /// <see cref="ModuleTaskEndedException"/> if it ended without one.
    /// </summary>
    /// <param name="stoppingToken">The host's shutdown token.</param>
    /// <param name="tasks">The module's tasks, each named for diagnostics.</param>
    public static async Task ObserveUntilStoppedAsync(
        CancellationToken stoppingToken,
        params (string Name, Task Task)[] tasks)
    {
        ArgumentNullException.ThrowIfNull(tasks);
        if (tasks.Length == 0)
        {
            throw new ArgumentException("At least one task must be observed.", nameof(tasks));
        }

        // Completes (cancelled) at shutdown; never faults.
        var stopRequested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using CancellationTokenRegistration registration =
            stoppingToken.Register(() => stopRequested.TrySetResult());

        var all = new Task[tasks.Length + 1];
        for (int i = 0; i < tasks.Length; i++)
        {
            all[i] = tasks[i].Task;
        }

        all[^1] = stopRequested.Task;

        Task first = await Task.WhenAny(all).ConfigureAwait(false);
        if (first == stopRequested.Task || stoppingToken.IsCancellationRequested)
        {
            return; // Normal shutdown: the caller cleans up.
        }

        (string name, Task ended) = Array.Find(tasks, t => t.Task == first);
        await ended.ConfigureAwait(false); // Rethrows a fault or a cancellation.
        throw ModuleTaskEndedException.ForTask(name);
    }
}
