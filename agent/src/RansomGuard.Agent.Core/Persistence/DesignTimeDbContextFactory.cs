using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace RansomGuard.Agent.Core.Persistence;

/// <summary>
/// Design-time factory for EF Core tooling (migrations, script generation).
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AgentDbContext>
{
    /// <inheritdoc />
    public AgentDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<AgentDbContext>();
        optionsBuilder.UseSqlite("Data Source=design-time.db");
        return new AgentDbContext(optionsBuilder.Options);
    }
}
