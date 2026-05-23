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
    /// Entropy baselines for delta-based detection.
    /// </summary>
    public DbSet<EntropyBaseline> EntropyBaselines => Set<EntropyBaseline>();

    /// <summary>
    /// Entropy-based detection alerts.
    /// </summary>
    public DbSet<EntropyAlert> EntropyAlerts => Set<EntropyAlert>();

    /// <summary>
    /// GENEALOGY process tree forensic records linked to alerts.
    /// </summary>
    public DbSet<GenealogyRecord> GenealogyRecords => Set<GenealogyRecord>();

    /// <summary>
    /// USB device whitelist entries.
    /// </summary>
    public DbSet<UsbWhitelistEntry> UsbWhitelistEntries => Set<UsbWhitelistEntry>();

    /// <summary>
    /// USB policy configuration.
    /// </summary>
    public DbSet<UsbPolicy> UsbPolicies => Set<UsbPolicy>();

    /// <summary>
    /// Quarantined files from USB scanning.
    /// </summary>
    public DbSet<QuarantinedFile> QuarantinedFiles => Set<QuarantinedFile>();

    /// <summary>
    /// USB connection logs for audit trail (90-day retention).
    /// </summary>
    public DbSet<UsbConnectionLog> UsbConnectionLogs => Set<UsbConnectionLog>();

    /// <summary>
    /// USB content scan results.
    /// </summary>
    public DbSet<UsbScanResult> UsbScanResults => Set<UsbScanResult>();

    /// <summary>
    /// USB GUARD alerts cross-linked with scans and genealogy.
    /// </summary>
    public DbSet<UsbAlert> UsbAlerts => Set<UsbAlert>();

    /// <summary>
    /// EXFIL WATCH alerts for suspected data exfiltration.
    /// </summary>
    public DbSet<ExfilAlert> ExfilAlerts => Set<ExfilAlert>();

    /// <summary>
    /// Network activity baselines for adaptive detection.
    /// </summary>
    public DbSet<NetworkBaseline> NetworkBaselines => Set<NetworkBaseline>();

    /// <summary>
    /// Per-dimension metrics within network baselines.
    /// </summary>
    public DbSet<NetworkBaselineMetric> NetworkBaselineMetrics => Set<NetworkBaselineMetric>();

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
            entity.Property(e => e.Signature).HasMaxLength(128);
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

        modelBuilder.Entity<EntropyBaseline>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.FilePath).IsUnique();
            entity.HasIndex(e => e.DirectoryPath);
            entity.HasIndex(e => e.FileExtension);
            entity.Property(e => e.FilePath).IsRequired().HasMaxLength(1024);
            entity.Property(e => e.DirectoryPath).IsRequired().HasMaxLength(1024);
            entity.Property(e => e.FileExtension).IsRequired().HasMaxLength(20);
        });

        modelBuilder.Entity<EntropyAlert>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.DetectedAt);
            entity.Property(e => e.FilePath).IsRequired().HasMaxLength(1024);
            entity.Property(e => e.RuleName).IsRequired().HasMaxLength(50);
            entity.Property(e => e.Severity).IsRequired().HasMaxLength(20);
        });

        modelBuilder.Entity<GenealogyRecord>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.AlertId);
            entity.Property(e => e.ProcessTreeJson).IsRequired();
            entity.Property(e => e.SuspiciousPatternsJson).IsRequired();
            entity.Property(e => e.RootProcessName).IsRequired().HasMaxLength(255);
            entity.Property(e => e.Summary).IsRequired().HasMaxLength(1024);
        });

        modelBuilder.Entity<UsbWhitelistEntry>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.SerialNumberHash);
            entity.HasIndex(e => e.IsActive);
            entity.Property(e => e.SerialNumberHash).IsRequired().HasMaxLength(64);
            entity.Property(e => e.Description).IsRequired().HasMaxLength(500);
            entity.Property(e => e.AddedByUser).IsRequired().HasMaxLength(100);
            entity.Property(e => e.PolicyLevel).HasConversion<string>().HasMaxLength(20);
        });

        modelBuilder.Entity<UsbPolicy>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Mode).HasConversion<string>().HasMaxLength(20);
            entity.Property(e => e.SuspiciousExtensionsJson).IsRequired();
        });

        modelBuilder.Entity<UsbConnectionLog>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.ConnectedAt);
            entity.HasIndex(e => e.SerialNumberHash);
            entity.HasIndex(e => e.RetainUntil);
            entity.Property(e => e.DeviceInstanceId).IsRequired().HasMaxLength(512);
            entity.Property(e => e.SerialNumberHash).IsRequired().HasMaxLength(64);
            entity.Property(e => e.VendorId).IsRequired().HasMaxLength(10);
            entity.Property(e => e.ProductId).IsRequired().HasMaxLength(10);
            entity.Property(e => e.DeviceClass).IsRequired().HasMaxLength(30);
            entity.Property(e => e.DriveLetter).HasMaxLength(5);
        });

        modelBuilder.Entity<UsbScanResult>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.UsbConnectionLogId);
            entity.HasIndex(e => e.ScannedAt);
            entity.Property(e => e.FlaggedFilesJson).IsRequired();
            entity.Property(e => e.HighestSeverity).IsRequired().HasMaxLength(20);
        });

        modelBuilder.Entity<UsbAlert>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.UsbScanResultId);
            entity.HasIndex(e => e.GeneratedAt);
            entity.Property(e => e.Title).IsRequired().HasMaxLength(500);
            entity.Property(e => e.Description).IsRequired();
            entity.Property(e => e.Severity).IsRequired().HasMaxLength(20);
            entity.Property(e => e.ActionTaken).IsRequired().HasMaxLength(50);
        });

        modelBuilder.Entity<ExfilAlert>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.DetectedAt);
            entity.HasIndex(e => e.RuleName);
            entity.HasIndex(e => e.ProcessName);
            entity.Property(e => e.RuleName).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Severity).IsRequired().HasMaxLength(20);
            entity.Property(e => e.ProcessName).IsRequired().HasMaxLength(255);
            entity.Property(e => e.Destination).IsRequired().HasMaxLength(500);
            entity.Property(e => e.Description).IsRequired();
            entity.Property(e => e.ActionTaken).IsRequired().HasMaxLength(50);
        });

        modelBuilder.Entity<NetworkBaseline>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Scope).IsUnique();
            entity.HasIndex(e => e.Phase);
            entity.Property(e => e.Scope).IsRequired().HasMaxLength(255);
            entity.Property(e => e.Phase).HasConversion<string>().HasMaxLength(20);
        });

        modelBuilder.Entity<NetworkBaselineMetric>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.NetworkBaselineId, e.MetricType, e.Dimension }).IsUnique();
            entity.HasIndex(e => e.NetworkBaselineId);
            entity.Property(e => e.Dimension).IsRequired().HasMaxLength(500);
            entity.Property(e => e.MetricType).HasConversion<string>().HasMaxLength(50);
            entity.Property(e => e.HourlyPatternJson).IsRequired();
            entity.Property(e => e.WeeklyPatternJson).IsRequired();
        });

        modelBuilder.Entity<QuarantinedFile>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.QuarantinedAt);
            entity.HasIndex(e => e.RetainUntil);
            entity.Property(e => e.OriginalPath).IsRequired().HasMaxLength(1024);
            entity.Property(e => e.QuarantinePath).IsRequired().HasMaxLength(1024);
            entity.Property(e => e.OriginalSha256).IsRequired().HasMaxLength(64);
            entity.Property(e => e.QuarantineSha256).IsRequired().HasMaxLength(64);
            entity.Property(e => e.QuarantineReason).IsRequired().HasMaxLength(500);
            entity.Property(e => e.Severity).HasConversion<string>().HasMaxLength(20);
            entity.Property(e => e.SourceUsbSerial).IsRequired().HasMaxLength(64);
            entity.Property(e => e.QuarantinedByUser).IsRequired().HasMaxLength(100);
            entity.Property(e => e.RestoredByUser).HasMaxLength(100);
        });
    }
}
