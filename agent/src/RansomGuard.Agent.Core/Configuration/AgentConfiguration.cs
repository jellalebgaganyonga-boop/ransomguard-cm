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
