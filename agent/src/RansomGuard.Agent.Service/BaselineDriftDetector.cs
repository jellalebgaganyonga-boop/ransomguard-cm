using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RansomGuard.Agent.Core.Persistence;
using RansomGuard.Agent.Core.Persistence.Entities;

namespace RansomGuard.Agent.Service;

/// <summary>
/// Background service that runs every 24 hours to detect baseline drift.
/// If any metric drifts > 50% from its initial values, marks the baseline
/// as DriftDetected (suspected poisoning) and creates an alert.
/// </summary>
public sealed class BaselineDriftDetector : BackgroundService
{
    private const double DriftThreshold = 0.50; // 50%
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(24);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BaselineDriftDetector> _logger;

    /// <summary>Initializes the baseline drift detector.</summary>
    public BaselineDriftDetector(
        IServiceScopeFactory scopeFactory,
        ILogger<BaselineDriftDetector> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Wait for initial learning phase to complete
        await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckForDriftAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "BaselineDriftDetector check failed");
            }

            await Task.Delay(CheckInterval, stoppingToken);
        }
    }

    private async Task CheckForDriftAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AgentDbContext>();

        var baselines = await context.NetworkBaselines
            .Where(b => b.Phase == BaselinePhase.ActiveDetection)
            .ToListAsync(ct);

        foreach (var baseline in baselines)
        {
            var metrics = await context.NetworkBaselineMetrics
                .Where(m => m.NetworkBaselineId == baseline.Id)
                .ToListAsync(ct);

            foreach (var metric in metrics)
            {
                if (metric.HourlyAverageBytes == 0) continue;

                double coefficientOfVariation = metric.HourlyStdDevBytes / metric.HourlyAverageBytes;
                if (coefficientOfVariation > DriftThreshold && metric.ObservationCount > 100)
                {
                    baseline.Phase = BaselinePhase.DriftDetected;
                    baseline.DriftDetectedAt = DateTime.UtcNow;
                    baseline.LastUpdatedAt = DateTime.UtcNow;

                    _logger.LogCritical(
                        "BASELINE DRIFT: Scope '{Scope}', dimension '{Dimension}' drifted {Drift:P0}. Suspected poisoning.",
                        baseline.Scope, metric.Dimension, coefficientOfVariation);

                    context.ExfilAlerts.Add(new ExfilAlert
                    {
                        RuleName = "BaselineDrift",
                        Severity = "Critical",
                        ProcessId = 0,
                        ProcessName = "SYSTEM",
                        Destination = metric.Dimension,
                        BytesTransferred = (long)metric.HourlyAverageBytes,
                        Description = $"Baseline drift detected for {metric.Dimension}: " +
                                      $"CV={coefficientOfVariation:P0}, phase set to DriftDetected",
                        ActionTaken = "Alert"
                    });

                    break;
                }
            }
        }

        await context.SaveChangesAsync(ct);
    }
}
