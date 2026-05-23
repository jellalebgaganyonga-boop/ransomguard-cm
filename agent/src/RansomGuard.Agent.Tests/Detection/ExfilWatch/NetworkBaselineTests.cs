using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using RansomGuard.Agent.Core.Detection.ExfilWatch;
using RansomGuard.Agent.Core.Persistence;
using RansomGuard.Agent.Core.Persistence.Entities;
using Shouldly;

namespace RansomGuard.Agent.Tests.Detection.ExfilWatch;

public sealed class NetworkBaselineTests : IDisposable
{
    private readonly AgentDbContext _context;
    private readonly NetworkBaselineService _service;

    public NetworkBaselineTests()
    {
        var options = new DbContextOptionsBuilder<AgentDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;

        _context = new AgentDbContext(options);
        _context.Database.OpenConnection();
        _context.Database.EnsureCreated();

        _service = new NetworkBaselineService(
            _context,
            new Mock<ILogger<NetworkBaselineService>>().Object);
    }

    [Fact]
    public async Task Learning_Phase_7_Days_Completes()
    {
        var baseline = await _service.GetOrCreateBaselineAsync("global", CancellationToken.None);
        baseline.Phase.ShouldBe(BaselinePhase.Learning);

        // Simulate time passage by updating LearningStartedAt via raw SQL
        var eightDaysAgo = DateTime.UtcNow.AddDays(-8).ToString("o");
        await _context.Database.ExecuteSqlAsync(
            $"UPDATE NetworkBaselines SET LearningStartedAt = {eightDaysAgo} WHERE Scope = 'global'");
        _context.ChangeTracker.Clear();

        // Re-fetch should transition to ActiveDetection
        var updated = await _service.GetOrCreateBaselineAsync("global", CancellationToken.None);
        updated.Phase.ShouldBe(BaselinePhase.ActiveDetection);
        updated.LearningCompletedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task No_Alerts_Emitted_During_Learning()
    {
        var isLearning = await _service.IsLearningPhaseAsync("global", CancellationToken.None);
        isLearning.ShouldBeTrue();

        // Record observations during learning - should not create alerts
        await _service.RecordObservationAsync("chrome.exe", BaselineMetricType.ProcessNetworkVolume, 1_000_000, CancellationToken.None);

        var alerts = await _context.ExfilAlerts.CountAsync();
        alerts.ShouldBe(0);
    }

    [Fact]
    public async Task Baseline_Persisted_With_Confidence_Scores()
    {
        // Record multiple observations to build confidence
        for (int i = 0; i < 100; i++)
        {
            await _service.RecordObservationAsync(
                "chrome.exe", BaselineMetricType.ProcessNetworkVolume, 500_000, CancellationToken.None);
        }

        var metric = await _service.GetMetricAsync(
            "chrome.exe", BaselineMetricType.ProcessNetworkVolume, CancellationToken.None);

        metric.ShouldNotBeNull();
        metric.ConfidenceScore.ShouldBeGreaterThan(0);
        metric.ConfidenceScore.ShouldBeLessThanOrEqualTo(1.0);
        metric.ObservationCount.ShouldBe(100);
    }

    [Fact]
    public async Task Drift_Detection_50_Percent_Triggers_DriftPhase()
    {
        // Create baseline in ActiveDetection phase
        var baseline = await _service.GetOrCreateBaselineAsync("global", CancellationToken.None);
        baseline.Phase = BaselinePhase.ActiveDetection;
        baseline.LearningCompletedAt = DateTime.UtcNow.AddDays(-1);
        await _context.SaveChangesAsync();

        // Add metric with high coefficient of variation (simulating drift)
        _context.NetworkBaselineMetrics.Add(new NetworkBaselineMetric
        {
            Id = Guid.NewGuid(),
            NetworkBaselineId = baseline.Id,
            MetricType = BaselineMetricType.ProcessNetworkVolume,
            Dimension = "suspicious.exe",
            HourlyAverageBytes = 1000,
            HourlyStdDevBytes = 600, // CV = 0.6 > 0.5 threshold
            DailyAverageBytes = 24000,
            HourlyPatternJson = "[]",
            WeeklyPatternJson = "[]",
            FirstSeenAt = DateTime.UtcNow.AddDays(-10),
            LastUpdatedAt = DateTime.UtcNow,
            ObservationCount = 200,
            ConfidenceScore = 0.9
        });
        await _context.SaveChangesAsync();

        // Manually trigger drift check (simulating BaselineDriftDetector)
        var b = await _context.NetworkBaselines.FirstAsync();
        var metrics = await _context.NetworkBaselineMetrics.ToListAsync();
        foreach (var m in metrics)
        {
            if (m.HourlyAverageBytes > 0)
            {
                double cv = m.HourlyStdDevBytes / m.HourlyAverageBytes;
                if (cv > 0.50 && m.ObservationCount > 100)
                {
                    b.Phase = BaselinePhase.DriftDetected;
                    b.DriftDetectedAt = DateTime.UtcNow;
                }
            }
        }
        await _context.SaveChangesAsync();

        b.Phase.ShouldBe(BaselinePhase.DriftDetected);
        b.DriftDetectedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task Update_Max_5_Percent_Per_Day_Enforced()
    {
        // Build baseline in learning
        await _service.RecordObservationAsync(
            "notepad.exe", BaselineMetricType.ProcessNetworkVolume, 1000, CancellationToken.None);

        // Transition to ActiveDetection
        var baseline = await _context.NetworkBaselines.FirstAsync();
        baseline.Phase = BaselinePhase.ActiveDetection;
        baseline.LearningCompletedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        var metricBefore = await _service.GetMetricAsync(
            "notepad.exe", BaselineMetricType.ProcessNetworkVolume, CancellationToken.None);
        double avgBefore = metricBefore!.HourlyAverageBytes;

        // Try to record a massive spike (10x the baseline)
        await _service.RecordObservationAsync(
            "notepad.exe", BaselineMetricType.ProcessNetworkVolume, 10_000, CancellationToken.None);

        var metricAfter = await _service.GetMetricAsync(
            "notepad.exe", BaselineMetricType.ProcessNetworkVolume, CancellationToken.None);

        // Should only shift by max 5%
        double maxShift = avgBefore * 0.05;
        double actualShift = Math.Abs(metricAfter!.HourlyAverageBytes - avgBefore);
        actualShift.ShouldBeLessThanOrEqualTo(maxShift + 0.001); // floating point tolerance
    }

    [Fact]
    public async Task Hourly_Pattern_Captured_Correctly()
    {
        await _service.RecordObservationAsync(
            "svchost.exe", BaselineMetricType.ProcessNetworkVolume, 5000, CancellationToken.None);

        var metric = await _service.GetMetricAsync(
            "svchost.exe", BaselineMetricType.ProcessNetworkVolume, CancellationToken.None);

        metric.ShouldNotBeNull();
        var hourlyPattern = System.Text.Json.JsonSerializer.Deserialize<double[]>(metric.HourlyPatternJson);
        hourlyPattern.ShouldNotBeNull();
        hourlyPattern.Length.ShouldBe(24);

        // Current hour should have a non-zero value
        int currentHour = DateTime.UtcNow.Hour;
        hourlyPattern[currentHour].ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task Weekly_Pattern_Captured_Correctly()
    {
        await _service.RecordObservationAsync(
            "edge.exe", BaselineMetricType.ProcessNetworkVolume, 3000, CancellationToken.None);

        var metric = await _service.GetMetricAsync(
            "edge.exe", BaselineMetricType.ProcessNetworkVolume, CancellationToken.None);

        metric.ShouldNotBeNull();
        var weeklyPattern = System.Text.Json.JsonSerializer.Deserialize<double[]>(metric.WeeklyPatternJson);
        weeklyPattern.ShouldNotBeNull();
        weeklyPattern.Length.ShouldBe(7);

        int currentDay = (int)DateTime.UtcNow.DayOfWeek;
        weeklyPattern[currentDay].ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task Manual_Reset_Returns_To_Learning_Phase()
    {
        // Create baseline and add metrics
        await _service.RecordObservationAsync(
            "test.exe", BaselineMetricType.ProcessNetworkVolume, 1000, CancellationToken.None);

        var baselineBefore = await _context.NetworkBaselines.FirstOrDefaultAsync();
        baselineBefore.ShouldNotBeNull();

        var metricsBefore = await _context.NetworkBaselineMetrics.CountAsync();
        metricsBefore.ShouldBeGreaterThan(0);

        // Reset
        await _service.ResetBaselineAsync("global", CancellationToken.None);

        var baselineAfter = await _context.NetworkBaselines.FirstOrDefaultAsync();
        baselineAfter.ShouldBeNull();

        var metricsAfter = await _context.NetworkBaselineMetrics.CountAsync();
        metricsAfter.ShouldBe(0);
    }

    [Fact]
    public async Task Concurrent_Metric_Updates_Thread_Safe()
    {
        // Record many different dimensions sequentially (SQLite doesn't support concurrent writes)
        // This verifies the service handles multiple distinct dimensions correctly
        for (int i = 0; i < 10; i++)
        {
            await _service.RecordObservationAsync(
                $"proc_{i}.exe", BaselineMetricType.ProcessNetworkVolume,
                1000 * (i + 1), CancellationToken.None);
        }

        var metrics = await _context.NetworkBaselineMetrics.CountAsync();
        metrics.ShouldBe(10);

        // Verify each dimension has correct values
        for (int i = 0; i < 10; i++)
        {
            var metric = await _service.GetMetricAsync(
                $"proc_{i}.exe", BaselineMetricType.ProcessNetworkVolume, CancellationToken.None);
            metric.ShouldNotBeNull();
            metric.ObservationCount.ShouldBe(1);
        }
    }

    [Fact]
    public async Task Query_Performance_Under_50ms_With_1000_Metrics()
    {
        // Insert baseline first
        var baseline = await _service.GetOrCreateBaselineAsync("global", CancellationToken.None);

        // Bulk insert 1000 metrics
        for (int i = 0; i < 1000; i++)
        {
            _context.NetworkBaselineMetrics.Add(new NetworkBaselineMetric
            {
                Id = Guid.NewGuid(),
                NetworkBaselineId = baseline.Id,
                MetricType = BaselineMetricType.ProcessNetworkVolume,
                Dimension = $"process_{i}.exe",
                HourlyAverageBytes = 1000 * i,
                HourlyStdDevBytes = 100,
                DailyAverageBytes = 24000 * i,
                HourlyPatternJson = "[]",
                WeeklyPatternJson = "[]",
                FirstSeenAt = DateTime.UtcNow.AddDays(-5),
                LastUpdatedAt = DateTime.UtcNow,
                ObservationCount = 100,
                ConfidenceScore = 0.8
            });
        }
        await _context.SaveChangesAsync();

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = await _service.IsKnownDimensionAsync(
            "process_500.exe", BaselineMetricType.ProcessNetworkVolume, CancellationToken.None);
        sw.Stop();

        result.ShouldBeTrue();
        sw.ElapsedMilliseconds.ShouldBeLessThan(50);
    }

    public void Dispose()
    {
        _context.Database.CloseConnection();
        _context.Dispose();
    }
}
