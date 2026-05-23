namespace RansomGuard.Agent.Core.Persistence.Entities;

/// <summary>
/// Per-dimension metric within a <see cref="NetworkBaseline"/>.
/// Tracks hourly/daily averages, standard deviations, and temporal patterns.
/// </summary>
public sealed class NetworkBaselineMetric
{
    /// <summary>Unique metric identifier.</summary>
    public required Guid Id { get; init; }

    /// <summary>Parent baseline ID.</summary>
    public required Guid NetworkBaselineId { get; init; }

    /// <summary>Type of metric being tracked.</summary>
    public required BaselineMetricType MetricType { get; init; }

    /// <summary>Dimension key (process_name, remote_ip, dns_domain).</summary>
    public required string Dimension { get; init; }

    /// <summary>Hourly average bytes transferred.</summary>
    public required double HourlyAverageBytes { get; set; }

    /// <summary>Hourly standard deviation of bytes transferred.</summary>
    public required double HourlyStdDevBytes { get; set; }

    /// <summary>Daily average bytes transferred.</summary>
    public required double DailyAverageBytes { get; set; }

    /// <summary>JSON array of 24 hourly pattern values (bytes per hour-of-day).</summary>
    public required string HourlyPatternJson { get; set; }

    /// <summary>JSON array of 7 weekly pattern values (bytes per day-of-week).</summary>
    public required string WeeklyPatternJson { get; set; }

    /// <summary>UTC timestamp when this dimension was first observed.</summary>
    public required DateTime FirstSeenAt { get; init; }

    /// <summary>UTC timestamp of last update.</summary>
    public required DateTime LastUpdatedAt { get; set; }

    /// <summary>Number of observations for this dimension.</summary>
    public required int ObservationCount { get; set; }

    /// <summary>Confidence score: log10(ObservationCount) clamped 0-1.</summary>
    public required double ConfidenceScore { get; set; }
}

/// <summary>
/// Types of network baseline metrics.
/// </summary>
public enum BaselineMetricType
{
    /// <summary>Per-process network volume.</summary>
    ProcessNetworkVolume,

    /// <summary>Per-remote-destination volume.</summary>
    RemoteDestinationVolume,

    /// <summary>DNS query frequency per domain.</summary>
    DnsQueryFrequency,

    /// <summary>Per-interface usage.</summary>
    InterfaceUsage
}
