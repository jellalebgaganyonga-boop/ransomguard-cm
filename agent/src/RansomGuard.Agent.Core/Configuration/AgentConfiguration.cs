using System.ComponentModel.DataAnnotations;

namespace RansomGuard.Agent.Core.Configuration;

/// <summary>
/// Root configuration for the RansomGuard agent.
/// Bound from appsettings.json via the Options pattern.
/// </summary>
public sealed record AgentConfiguration
{
    /// <summary>
    /// The configuration section name in appsettings.json.
    /// </summary>
    public const string SectionName = "Agent";

    /// <summary>
    /// Agent identity settings.
    /// </summary>
    [Required]
    public required AgentIdentityOptions Identity { get; init; }

    /// <summary>
    /// Detection engine settings.
    /// </summary>
    [Required]
    public required DetectionOptions Detection { get; init; }

    /// <summary>
    /// Logging configuration.
    /// </summary>
    [Required]
    public required LoggingOptions Logging { get; init; }

    /// <summary>
    /// Server communication settings.
    /// </summary>
    [Required]
    public required ServerOptions Server { get; init; }

    /// <summary>
    /// Local database settings.
    /// </summary>
    [Required]
    public required DatabaseOptions Database { get; init; }

    /// <summary>
    /// SENTINEL canary file detection settings.
    /// </summary>
    public SentinelOptions? Sentinel { get; init; }

    /// <summary>
    /// ENTROPY detection settings for Shannon entropy-based ransomware detection.
    /// </summary>
    public EntropyOptions? Entropy { get; init; }

    /// <summary>
    /// USB GUARD settings for removable media monitoring and scanning.
    /// </summary>
    public UsbGuardOptions? UsbGuard { get; init; }

    /// <summary>
    /// EXFIL WATCH settings for data exfiltration detection.
    /// </summary>
    public ExfilWatchOptions? ExfilWatch { get; init; }
}

/// <summary>
/// SENTINEL semantic medical canary file configuration.
/// Controls deployment and monitoring of decoy files that detect ransomware.
/// </summary>
public sealed record SentinelOptions
{
    /// <summary>
    /// Whether SENTINEL canary monitoring is enabled.
    /// </summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Number of canary files to deploy per watched directory.
    /// </summary>
    [Range(1, 10)]
    public int CanariesPerDirectory { get; init; } = 3;

    /// <summary>
    /// Interval in milliseconds between periodic canary integrity checks.
    /// </summary>
    [Range(100, 10000)]
    public int CheckIntervalMs { get; init; } = 1000;

    /// <summary>
    /// Directories to deploy and monitor canary files in.
    /// Supports environment variables (e.g., %USERPROFILE%).
    /// </summary>
    [Required]
    [MinLength(1)]
    public required string[] WatchDirectories { get; init; }

    /// <summary>
    /// Template names used to generate realistic medical canary content.
    /// </summary>
    [Required]
    [MinLength(3)]
    public required string[] CanaryTemplates { get; init; }

    /// <summary>
    /// Filename prefix for canary files (ensures alphabetical prominence).
    /// </summary>
    [Required]
    public string CanaryPrefix { get; init; } = "0001_";

    /// <summary>
    /// Whether to automatically regenerate deleted canaries at runtime.
    /// </summary>
    public bool EnableRealTimeRegeneration { get; init; } = true;

    /// <summary>
    /// Cooldown in seconds before a canary can be regenerated after deletion.
    /// </summary>
    [Range(1, 3600)]
    public int RegenerationCooldownSeconds { get; init; } = 60;

    /// <summary>
    /// Maximum number of canary regenerations per hour per directory.
    /// Exceeding this threshold triggers a SustainedAttack alert.
    /// </summary>
    [Range(1, 100)]
    public int MaxRegenerationsPerHourPerDirectory { get; init; } = 10;
}

/// <summary>
/// Agent identity and versioning.
/// </summary>
public sealed record AgentIdentityOptions
{
    /// <summary>
    /// Unique agent identifier (GUID). Auto-generated on first run if set to "auto-generated-on-first-run".
    /// </summary>
    [Required]
    public required string Id { get; init; }

    /// <summary>
    /// Machine hostname. Set to "AUTO" to resolve automatically.
    /// </summary>
    [Required]
    public required string Hostname { get; init; }

    /// <summary>
    /// Current agent version following SemVer.
    /// </summary>
    [Required]
    public required string Version { get; init; }

    /// <summary>
    /// Deployment environment (Development, Staging, Production).
    /// </summary>
    [Required]
    public required string Environment { get; init; }
}

/// <summary>
/// File system and behavioral detection settings.
/// </summary>
public sealed record DetectionOptions
{
    /// <summary>
    /// Paths to monitor for suspicious file activity.
    /// Supports environment variables (e.g., %USERPROFILE%).
    /// </summary>
    [Required]
    [MinLength(1)]
    public required string[] WatchPaths { get; init; }

    /// <summary>
    /// Whether to enable FileSystemWatcher-based detection.
    /// </summary>
    public bool EnableFileSystemWatcher { get; init; } = true;

    /// <summary>
    /// Whether to enable ETW (Event Tracing for Windows) kernel-mode tracing.
    /// </summary>
    public bool EnableETW { get; init; }

    /// <summary>
    /// Sliding window in milliseconds for deduplicating FileSystemWatcher events.
    /// Events with the same path and type within this window are treated as duplicates.
    /// </summary>
    [Range(50, 5000)]
    public int DeduplicationWindowMs { get; init; } = 500;
}

/// <summary>
/// Structured logging configuration.
/// </summary>
public sealed record LoggingOptions
{
    /// <summary>
    /// Minimum log level (Verbose, Debug, Information, Warning, Error, Fatal).
    /// </summary>
    [Required]
    public required string MinimumLevel { get; init; }

    /// <summary>
    /// File path for log output. Supports environment variables.
    /// </summary>
    [Required]
    public required string LogFilePath { get; init; }

    /// <summary>
    /// Maximum size of a single log file in megabytes.
    /// </summary>
    [Range(1, 500)]
    public int MaxFileSizeMB { get; init; } = 50;

    /// <summary>
    /// Number of rotated log files to retain.
    /// </summary>
    [Range(1, 365)]
    public int RetainedFileCount { get; init; } = 30;
}

/// <summary>
/// Management server communication settings.
/// </summary>
public sealed record ServerOptions
{
    /// <summary>
    /// Base URL of the RansomGuard management server.
    /// </summary>
    [Required]
    public required string BaseUrl { get; init; }

    /// <summary>
    /// Interval in seconds between heartbeat signals to the server.
    /// </summary>
    [Range(10, 3600)]
    public int HeartbeatIntervalSeconds { get; init; } = 30;

    /// <summary>
    /// Connection timeout in seconds for server communication.
    /// </summary>
    [Range(1, 120)]
    public int ConnectionTimeoutSeconds { get; init; } = 10;
}

/// <summary>
/// Local SQLite database settings.
/// </summary>
public sealed record DatabaseOptions
{
    /// <summary>
    /// SQLite connection string. Supports environment variables in path.
    /// </summary>
    [Required]
    public required string ConnectionString { get; init; }

    /// <summary>
    /// Maximum number of days to retain detection events and audit logs.
    /// </summary>
    [Range(1, 3650)]
    public int MaxRetentionDays { get; init; } = 90;
}

/// <summary>
/// Shannon entropy-based ransomware detection configuration.
/// </summary>
public sealed record EntropyOptions
{
    /// <summary>Whether entropy detection is enabled.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>Absolute entropy threshold (bits/byte) for text-like files.</summary>
    [Range(6.0, 8.0)]
    public double AbsoluteThreshold { get; init; } = 7.5;

    /// <summary>Entropy delta threshold (current - baseline) to trigger alert.</summary>
    [Range(1.0, 5.0)]
    public double DeltaThreshold { get; init; } = 2.5;

    /// <summary>Directory-wide entropy shift threshold for mass encryption detection.</summary>
    [Range(0.5, 5.0)]
    public double DirectoryShiftThreshold { get; init; } = 1.5;

    /// <summary>Time window in seconds for directory-wide shift detection.</summary>
    [Range(10, 600)]
    public int DirectoryShiftWindowSeconds { get; init; } = 60;

    /// <summary>Minimum number of files affected for directory-wide shift alert.</summary>
    [Range(2, 50)]
    public int DirectoryShiftMinFiles { get; init; } = 5;

    /// <summary>Extensions that are legitimately high-entropy (skip alerts).</summary>
    public string[] WhitelistedExtensions { get; init; } =
        [".zip", ".7z", ".gz", ".rar", ".jpg", ".jpeg", ".png", ".mp3", ".mp4", ".avi", ".mkv"];

    /// <summary>Extensions susceptible to encryption (lower threshold).</summary>
    public string[] SusceptibleExtensions { get; init; } =
        [".txt", ".doc", ".docx", ".pdf", ".xlsx", ".json", ".xml", ".csv", ".rtf", ".odt"];

    /// <summary>Maximum file size in MB for entropy analysis.</summary>
    [Range(1, 1000)]
    public int MaxFileSizeMB { get; init; } = 100;
}

/// <summary>
/// USB GUARD module configuration for removable media monitoring.
/// </summary>
public sealed record UsbGuardOptions
{
    /// <summary>Whether USB monitoring is enabled.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>Operating mode: Audit (log only), Permissive (log+quarantine), Strict (block).</summary>
    public string OperatingMode { get; init; } = "Permissive";

    /// <summary>Maximum file size in MB for content scanning.</summary>
    [Range(1, 1000)]
    public int MaxFileSizeForScanMB { get; init; } = 100;

    /// <summary>Maximum scan duration in seconds per USB device.</summary>
    [Range(10, 600)]
    public int MaxScanDurationSeconds { get; init; } = 120;

    /// <summary>Whether to scan inside archives (ZIP, RAR, 7z).</summary>
    public bool ScanArchiveContents { get; init; } = true;

    /// <summary>Block bootable USB devices by default.</summary>
    public bool BlockBootableUsb { get; init; } = true;

    /// <summary>Alert on HID device connections (USB keyboards, mice).</summary>
    public bool AlertOnHidDevice { get; init; } = true;

    /// <summary>Alert on USB network device connections (WiFi adapters, 4G dongles).</summary>
    public bool AlertOnNetworkDevice { get; init; } = true;

    /// <summary>Scan rate limit in files per second.</summary>
    [Range(10, 500)]
    public int ScanRatePerSecond { get; init; } = 50;
}

/// <summary>
/// EXFIL WATCH module configuration for data exfiltration detection.
/// </summary>
public sealed record ExfilWatchOptions
{
    /// <summary>Whether exfiltration monitoring is enabled.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>Learning phase duration in days before active detection.</summary>
    [Range(1, 30)]
    public int LearningPhaseDays { get; init; } = 7;

    /// <summary>Volume anomaly threshold multiplier over baseline average.</summary>
    [Range(2, 20)]
    public int VolumeAnomalyMultiplier { get; init; } = 5;

    /// <summary>DNS tunneling query threshold per 5 minutes.</summary>
    [Range(100, 10000)]
    public int DnsTunnelingQueryThreshold { get; init; } = 1000;

    /// <summary>DNS tunneling subdomain entropy threshold.</summary>
    public double DnsTunnelingEntropyThreshold { get; init; } = 4.5;

    /// <summary>Working hours start (0-23).</summary>
    [Range(0, 23)]
    public int WorkingHoursStart { get; init; } = 7;

    /// <summary>Working hours end (0-23).</summary>
    [Range(0, 23)]
    public int WorkingHoursEnd { get; init; } = 19;

    /// <summary>Cloud provider upload threshold in GB/hour for alerting.</summary>
    public double CloudAlertThresholdGbPerHour { get; init; } = 1.0;

    /// <summary>Firewall block rule auto-expiry in hours.</summary>
    [Range(1, 168)]
    public int BlockRuleExpiryHours { get; init; } = 24;

    /// <summary>ETW buffer count (each 64 KB).</summary>
    [Range(16, 256)]
    public int EtwBufferCount { get; init; } = 64;
}
