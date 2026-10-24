using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace RansomGuard.Agent.Core.Persistence;

/// <summary>
/// The SQLite settings every connection of the agent runs with, set explicitly when the
/// connection opens — no silent default (AGT-AUD-002).
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><c>journal_mode=WAL</c>: it used to come from EF Core, which switches a database to WAL
///   only when it creates the file — a database created otherwise (an older install, a restore)
///   would have stayed in rollback-journal mode without anyone noticing.</item>
/// <item><c>synchronous=FULL</c>: a recorded audit entry is one committed INSERT into
///   PendingAuditEntries; in WAL mode, NORMAL may lose the last commits on a power cut.
///   It was FULL only as the SQLCipher build's default. Never lower it.</item>
/// </list>
/// Both are checked on a real agent connection by AgentDatabaseDurabilityTests.
/// </remarks>
public sealed class AgentConnectionSettings : DbConnectionInterceptor
{
    /// <summary>Statements run on every connection, right after it opens (after the SQLCipher key).</summary>
    public const string Pragmas = "PRAGMA journal_mode=WAL; PRAGMA synchronous=FULL;";

    /// <inheritdoc />
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        if (IsReadOnly(connection))
        {
            return;
        }

        using DbCommand command = connection.CreateCommand();
        command.CommandText = Pragmas;
        command.ExecuteNonQuery();
    }

    /// <inheritdoc />
    public override async Task ConnectionOpenedAsync(
        DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        if (IsReadOnly(connection))
        {
            return;
        }

        DbCommand command = connection.CreateCommand();
        await using (command.ConfigureAwait(false))
        {
            command.CommandText = Pragmas;
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// A read-only connection — EF Core opens one to check whether the database file exists —
    /// writes nothing, and cannot switch the journal mode.
    /// </summary>
    private static bool IsReadOnly(DbConnection connection) =>
        new SqliteConnectionStringBuilder(connection.ConnectionString).Mode == SqliteOpenMode.ReadOnly;
}
