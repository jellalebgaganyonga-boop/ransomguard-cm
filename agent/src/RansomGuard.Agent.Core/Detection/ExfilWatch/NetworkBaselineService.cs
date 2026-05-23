using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RansomGuard.Agent.Core.Persistence;
using RansomGuard.Agent.Core.Persistence.Entities;

namespace RansomGuard.Agent.Core.Detection.ExfilWatch;

/// <summary>
/// Persisted 3-phase adaptive baseline for network activity.
/// Uses DataVolumeTracker as runtime sliding-window helper for current measurements.
/// </summary>
public sealed class NetworkBaselineService : INetworkBaselineService
{
    private const int LearningDays = 7;
    private const double MaxDailyUpdatePercent = 0.05; // 5% per day max during ActiveDetection
    private static readonly string EmptyHourlyPattern = JsonSerializer.Serialize(new double[24]);
    private static readonly string EmptyWeeklyPattern = JsonSerializer.Serialize(new double[7]);

    private readonly AgentDbContext _context;
    private readonly ILogger<NetworkBaselineService> _logger;

    /// <summary>Initializes the network baseline service.</summary>
    public NetworkBaselineService(AgentDbContext context, ILogger<NetworkBaselineService> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<NetworkBaseline> GetOrCreateBaselineAsync(string scope, CancellationToken ct)
    {
        var baseline = await _context.NetworkBaselines
            .FirstOrDefaultAsync(b => b.Scope == scope, ct);

        if (baseline is not null)
        {
            // Check phase transition: Learning -> ActiveDetection
            if (baseline.Phase == BaselinePhase.Learning &&
                DateTime.UtcNow >= baseline.LearningStartedAt.AddDays(LearningDays))
            {
                baseline.Phase = BaselinePhase.ActiveDetection;
                baseline.LearningCompletedAt = DateTime.UtcNow;
                baseline.LastUpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync(ct);

                _logger.LogInformation(
                    "NetworkBaseline '{Scope}' transitioned to ActiveDetection after {Days} days",
                    scope, LearningDays);
            }

            return baseline;
        }

        baseline = new NetworkBaseline
        {
            Id = Guid.NewGuid(),
            Scope = scope,
            Phase = BaselinePhase.Learning,
            LearningStartedAt = DateTime.UtcNow,
            ObservationCount = 0,
            ConfidenceScore = 0,
            LastUpdatedAt = DateTime.UtcNow
        };

        _context.NetworkBaselines.Add(baseline);
        await _context.SaveChangesAsync(ct);
        return baseline;
    }

    /// <inheritdoc />
    public async Task RecordObservationAsync(string dimension, BaselineMetricType type, long bytes, CancellationToken ct)
    {
        var baseline = await GetOrCreateBaselineAsync("global", ct);

        var metric = await _context.NetworkBaselineMetrics
            .FirstOrDefaultAsync(m => m.Dimension == dimension && m.MetricType == type, ct);

        if (metric is null)
        {
            metric = new NetworkBaselineMetric
            {
                Id = Guid.NewGuid(),
                NetworkBaselineId = baseline.Id,
                MetricType = type,
                Dimension = dimension,
                HourlyAverageBytes = bytes,
                HourlyStdDevBytes = 0,
                DailyAverageBytes = bytes,
                HourlyPatternJson = BuildInitialHourlyPattern(bytes),
                WeeklyPatternJson = BuildInitialWeeklyPattern(bytes),
                FirstSeenAt = DateTime.UtcNow,
                LastUpdatedAt = DateTime.UtcNow,
                ObservationCount = 1,
                ConfidenceScore = 0
            };
            _context.NetworkBaselineMetrics.Add(metric);
        }
        else
        {
            if (baseline.Phase == BaselinePhase.ActiveDetection)
            {
                // Cap updates to 5% per day
                double maxDelta = metric.HourlyAverageBytes * MaxDailyUpdatePercent;
                double rawDelta = bytes - metric.HourlyAverageBytes;
                double clampedDelta = Math.Clamp(rawDelta, -maxDelta, maxDelta);

                metric.HourlyAverageBytes += clampedDelta;
            }
            else if (baseline.Phase == BaselinePhase.Learning)
            {
                // Running average during learning
                double oldAvg = metric.HourlyAverageBytes;
                int n = metric.ObservationCount + 1;
                metric.HourlyAverageBytes = oldAvg + (bytes - oldAvg) / n;

                // Running variance (Welford's)
                double newAvg = metric.HourlyAverageBytes;
                metric.HourlyStdDevBytes = Math.Sqrt(
                    ((metric.HourlyStdDevBytes * metric.HourlyStdDevBytes * (n - 1)) +
                     (bytes - oldAvg) * (bytes - newAvg)) / n);

                // Update daily average
                metric.DailyAverageBytes = metric.HourlyAverageBytes * 24;

                // Update hourly pattern
                UpdateHourlyPattern(metric, bytes);
                UpdateWeeklyPattern(metric, bytes);
            }

            metric.ObservationCount++;
            metric.LastUpdatedAt = DateTime.UtcNow;
            metric.ConfidenceScore = ComputeConfidence(metric.ObservationCount);
        }

        baseline.ObservationCount++;
        baseline.ConfidenceScore = ComputeConfidence(baseline.ObservationCount);
        baseline.LastUpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(ct);
    }

    /// <inheritdoc />
    public async Task<bool> IsLearningPhaseAsync(string scope, CancellationToken ct)
    {
        var baseline = await _context.NetworkBaselines
            .FirstOrDefaultAsync(b => b.Scope == scope, ct);

        if (baseline is null) return true; // No baseline yet = learning

        // Auto-transition check
        if (baseline.Phase == BaselinePhase.Learning &&
            DateTime.UtcNow >= baseline.LearningStartedAt.AddDays(LearningDays))
        {
            baseline.Phase = BaselinePhase.ActiveDetection;
            baseline.LearningCompletedAt = DateTime.UtcNow;
            baseline.LastUpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(ct);
            return false;
        }

        return baseline.Phase == BaselinePhase.Learning;
    }

    /// <inheritdoc />
    public async Task<bool> IsKnownDimensionAsync(string dimension, BaselineMetricType type, CancellationToken ct)
    {
        return await _context.NetworkBaselineMetrics
            .AnyAsync(m => m.Dimension == dimension && m.MetricType == type, ct);
    }

    /// <inheritdoc />
    public async Task<NetworkBaselineMetric?> GetMetricAsync(string dimension, BaselineMetricType type, CancellationToken ct)
    {
        return await _context.NetworkBaselineMetrics
            .FirstOrDefaultAsync(m => m.Dimension == dimension && m.MetricType == type, ct);
    }

    /// <inheritdoc />
    public async Task ResetBaselineAsync(string scope, CancellationToken ct)
    {
        var baseline = await _context.NetworkBaselines
            .FirstOrDefaultAsync(b => b.Scope == scope, ct);

        if (baseline is null) return;

        // Remove all metrics for this baseline
        var metrics = await _context.NetworkBaselineMetrics
            .Where(m => m.NetworkBaselineId == baseline.Id)
            .ToListAsync(ct);

        _context.NetworkBaselineMetrics.RemoveRange(metrics);
        _context.NetworkBaselines.Remove(baseline);
        await _context.SaveChangesAsync(ct);

        _logger.LogWarning("NetworkBaseline '{Scope}' reset to Learning phase by admin", scope);
    }

    private static double ComputeConfidence(int observationCount)
    {
        if (observationCount <= 0) return 0;
        return Math.Clamp(Math.Log10(observationCount), 0, 1);
    }

    private static string BuildInitialHourlyPattern(long bytes)
    {
        var pattern = new double[24];
        pattern[DateTime.UtcNow.Hour] = bytes;
        return JsonSerializer.Serialize(pattern);
    }

    private static string BuildInitialWeeklyPattern(long bytes)
    {
        var pattern = new double[7];
        pattern[(int)DateTime.UtcNow.DayOfWeek] = bytes;
        return JsonSerializer.Serialize(pattern);
    }

    private static void UpdateHourlyPattern(NetworkBaselineMetric metric, long bytes)
    {
        var pattern = JsonSerializer.Deserialize<double[]>(metric.HourlyPatternJson) ?? new double[24];
        int hour = DateTime.UtcNow.Hour;
        int n = metric.ObservationCount + 1;
        pattern[hour] = pattern[hour] + (bytes - pattern[hour]) / n;
        metric.HourlyPatternJson = JsonSerializer.Serialize(pattern);
    }

    private static void UpdateWeeklyPattern(NetworkBaselineMetric metric, long bytes)
    {
        var pattern = JsonSerializer.Deserialize<double[]>(metric.WeeklyPatternJson) ?? new double[7];
        int day = (int)DateTime.UtcNow.DayOfWeek;
        int n = metric.ObservationCount + 1;
        pattern[day] = pattern[day] + (bytes - pattern[day]) / n;
        metric.WeeklyPatternJson = JsonSerializer.Serialize(pattern);
    }
}
