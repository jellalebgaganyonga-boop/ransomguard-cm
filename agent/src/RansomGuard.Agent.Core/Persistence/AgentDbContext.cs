using Microsoft.EntityFrameworkCore;
using RansomGuard.Agent.Core.Persistence.Entities;

namespace RansomGuard.Agent.Core.Persistence;

/// <summary>
/// Entity Framework Core database context for the RansomGuard agent local SQLite database.
/// </summary>
public sealed class AgentDbContext : DbContext
{
    /// <summary>
    /// File system detection events.
    /// </summary>
    public DbSet<DetectionEvent> DetectionEvents => Set<DetectionEvent>();

    /// <summary>
    /// High-confidence security alerts.
    /// </summary>
    public DbSet<Alert> Alerts => Set<Alert>();

    /// <summary>
    /// Agent lifecycle state records.
    /// </summary>
    public DbSet<AgentState> AgentStates => Set<AgentState>();

    /// <summary>
    /// Immutable, hash-chained audit trail.
    /// </summary>
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    /// <summary>
    /// SENTINEL canary files deployed for ransomware detection.
    /// </summary>
    public DbSet<SentinelCanary> SentinelCanaries => Set<SentinelCanary>();

    /// <summary>
    /// High-confidence alerts from SENTINEL canary tampering.
    /// </summary>
    public DbSet<CanaryAlert> CanaryAlerts => Set<CanaryAlert>();

    /// <summary>
    /// Initializes a new instance of <see cref="AgentDbContext"/>.
    /// </summary>
    /// <param name="options">Database context options.</param>
    public AgentDbContext(DbContextOptions<AgentDbContext> options)
        : base(options)
    {
    }

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<DetectionEvent>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Timestamp);
            entity.HasIndex(e => e.FilePath);
            entity.Property(e => e.EventType).IsRequired().HasMaxLength(50);
            entity.Property(e => e.FilePath).IsRequired().HasMaxLength(1024);
            entity.Property(e => e.OldFilePath).HasMaxLength(1024);
        });

        modelBuilder.Entity<Alert>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Severity);
            entity.HasIndex(e => e.Timestamp);
            entity.Property(e => e.Severity).IsRequired().HasMaxLength(20);
            entity.Property(e => e.Title).IsRequired().HasMaxLength(500);
            entity.Property(e => e.Description).IsRequired();
        });

        modelBuilder.Entity<AgentState>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.State).IsRequired().HasMaxLength(50);
            entity.Property(e => e.AgentVersion).IsRequired().HasMaxLength(20);
            entity.Property(e => e.Hostname).IsRequired().HasMaxLength(255);
        });

        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.CreatedAt);
            entity.Property(e => e.Action).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Details).IsRequired();
            entity.Property(e => e.EntityType).HasMaxLength(100);
            entity.Property(e => e.PreviousHash).HasMaxLength(64);
            entity.Property(e => e.CurrentHash).IsRequired().HasMaxLength(64);
        });

        modelBuilder.Entity<SentinelCanary>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.FilePath).IsUnique();
            entity.HasIndex(e => e.Directory);
            entity.HasIndex(e => e.Status);
            entity.Property(e => e.FilePath).IsRequired().HasMaxLength(1024);
            entity.Property(e => e.FileName).IsRequired().HasMaxLength(255);
            entity.Property(e => e.Directory).IsRequired().HasMaxLength(1024);
            entity.Property(e => e.TemplateUsed).IsRequired().HasMaxLength(100);
            entity.Property(e => e.OriginalContentHash).IsRequired().HasMaxLength(64);
            entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
        });

        modelBuilder.Entity<CanaryAlert>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.DetectedAt);
            entity.HasIndex(e => e.CanaryId);
            entity.Property(e => e.CanaryPath).IsRequired().HasMaxLength(1024);
            entity.Property(e => e.AlertType).HasConversion<string>().HasMaxLength(30);
            entity.Property(e => e.Severity).HasConversion<string>().HasMaxLength(20);
            entity.Property(e => e.OffendingProcessName).HasMaxLength(255);
            entity.Property(e => e.OffendingProcessPath).HasMaxLength(1024);
        });
    }
}
