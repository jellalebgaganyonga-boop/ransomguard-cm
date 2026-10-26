using Microsoft.Extensions.DependencyInjection;
using RansomGuard.Agent.Core.Persistence;

namespace RansomGuard.Agent.Service;

/// <summary>
/// One line for a monitor to wait until the database exists.
///
/// <para>
/// Every monitor already takes an <see cref="IServiceScopeFactory"/> to reach its repositories, so
/// the wait is hung off that rather than threaded through eight constructors. The gate itself is a
/// singleton, so which scope asks does not matter.
/// </para>
///
/// <para>
/// <see cref="ServiceProviderServiceExtensions.GetService{T}"/> rather than
/// <c>GetRequiredService</c>: a test that builds a monitor against a database it made itself has
/// nothing to wait for and does not register the gate, and must not be made to fail for it.
/// </para>
/// </summary>
internal static class DatabaseGate
{
    /// <summary>Waits until the schema exists, if this host has a gate to wait on.</summary>
    public static async Task WaitForDatabaseAsync(
        this IServiceScopeFactory scopeFactory, CancellationToken cancellationToken)
    {
        DatabaseReady? ready;
        using (IServiceScope scope = scopeFactory.CreateScope())
        {
            ready = scope.ServiceProvider.GetService<DatabaseReady>();
        }

        if (ready is not null)
        {
            await ready.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
