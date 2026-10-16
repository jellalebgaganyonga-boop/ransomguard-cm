using System.Collections.Concurrent;
using System.Data.Common;
using System.Runtime.CompilerServices;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace RansomGuard.Agent.Core.Persistence.Repositories;

/// <summary>
/// Audit entries the agent could not record since it started (AGT-AUD-002). Reported in the
/// heartbeat: the GRID shows the posture as partial / audit_write_failed. Singleton.
/// </summary>
public sealed class AuditHealth
{
    private int _failures;

    /// <summary>Entries lost (recording failed) or chaining attempts that failed, since start.</summary>
    public int Failures => Volatile.Read(ref _failures);

    /// <summary>Counts one failure.</summary>
    public void RecordFailure() => Interlocked.Increment(ref _failures);
}

/// <summary>
/// Asks for the recorded entries to be chained, in the background. Never waits for it.
/// </summary>
public interface IAuditChainWriter
{
    /// <summary>Signals that entries are waiting. Returns at once; repeated signals coalesce.</summary>
    void RequestChaining();
}

/// <summary>
/// One writer per chain, that is per database: chaining reads the tail then inserts the next
/// links, and two writers on the same chain would fork it. Two unrelated databases (two tests,
/// two tools) never wait for each other — the process-wide gate did that (AGT-AUD-002).
/// </summary>
internal static class AuditChainGate
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> ByDatabase = new(StringComparer.OrdinalIgnoreCase);

    // ":memory:" is a different database for every connection: one gate per connection object.
    private static readonly ConditionalWeakTable<DbConnection, SemaphoreSlim> ByPrivateConnection = new();

    public static SemaphoreSlim For(DbContext context)
    {
        DbConnection connection = context.Database.GetDbConnection();
        string dataSource = new SqliteConnectionStringBuilder(connection.ConnectionString).DataSource;

        if (string.IsNullOrEmpty(dataSource) || dataSource == ":memory:")
        {
            return ByPrivateConnection.GetValue(connection, _ => new SemaphoreSlim(1, 1));
        }

        // A named shared in-memory database ("file:name?mode=memory&cache=shared") is the same
        // database for every connection using that name; a file is the same database for every
        // spelling of its path.
        string key = dataSource.StartsWith("file:", StringComparison.OrdinalIgnoreCase)
            ? dataSource
            : Path.GetFullPath(dataSource);
        return ByDatabase.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
    }
}
