namespace RansomGuard.Agent.Core.Persistence;

/// <summary>
/// Tells the rest of the agent when the database is usable, so that creating it does not have to
/// happen before Windows is told the service is running.
///
/// <para>
/// A Windows service has to report itself running inside the Service Control Manager's limit of
/// 30 s, which is not ours to set. Applying the migrations used to happen before the host started
/// at all: on a first-ever install that is where the schema is created and Entity Framework is
/// compiled for the first time, and on PC-RECEPTION on 2026-10-26 it took longer than two minutes.
/// `sc start` returned 1053 and only the installer's retry saved the installation.
/// </para>
///
/// <para>
/// The migration now runs as the first hosted service, after the host has started, and everything
/// that reads the database waits here first. The agent is therefore "running" to Windows within a
/// second, and "protecting" a few seconds later -- which is the honest order: it cannot protect
/// anything until its database exists, but it can say it is alive.
/// </para>
///
/// <para>
/// A detector that waited on this and gave up would be a silent hole, so there is no timeout on
/// <see cref="WaitAsync"/>: it finishes when the database is ready, or when the agent is stopping,
/// or it throws what the migration threw. A broken schema stops the agent; it never half-starts it.
/// </para>
/// </summary>
public sealed class DatabaseReady
{
    private readonly TaskCompletionSource _ready =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Whether the database is usable now.</summary>
    public bool IsReady => _ready.Task.IsCompletedSuccessfully;

    /// <summary>The database is created, migrated and safe to read.</summary>
    public void Signal() => _ready.TrySetResult();

    /// <summary>
    /// The database could not be prepared. Everything waiting is failed with the same exception
    /// rather than left hanging: a detector blocked for ever looks identical to one that is
    /// watching, and that is the worst thing this component could do.
    /// </summary>
    public void Fail(Exception error) => _ready.TrySetException(error);

    /// <summary>
    /// Waits until the database is ready. Returns at once when it already is.
    /// </summary>
    public async Task WaitAsync(CancellationToken cancellationToken)
    {
        if (_ready.Task.IsCompleted)
        {
            await _ready.Task.ConfigureAwait(false);
            return;
        }

        await _ready.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }
}
