using Microsoft.Extensions.Logging;
using RansomGuard.Agent.Core.Configuration;
using RansomGuard.Agent.Core.Detection.ExfilWatch.Rules;
using RansomGuard.Agent.Core.Persistence.Repositories;

namespace RansomGuard.Agent.Core.Detection.ExfilWatch.Actions;

/// <summary>
/// Selects and executes exfiltration response actions based on severity.
/// Decision matrix: Low/Medium → AlertOnly, High → Alert+Throttle, Critical → Alert+Throttle+Block.
/// Graceful fallback: Block fails → Throttle. Throttle fails → AlertOnly.
/// All actions audit-logged with Ed25519 signatures.
/// </summary>
public sealed class ExfilActionEngine : IExfilActionEngine
{
    private static readonly TimeSpan ActionTimeout = TimeSpan.FromSeconds(10);
    private static readonly long GigabyteInBytes = 1_073_741_824L;

    private readonly AlertOnlyAction _alertAction;
    private readonly ThrottleProcessAction _throttleAction;
    private readonly BlockIpAction _blockAction;
    private readonly IThreatIntelProvider _threatIntel;
    private readonly IDataVolumeTracker _tracker;
    private readonly IAuditLogRepository _auditLog;
    private readonly ExfilWatchOptions _options;
    private readonly ILogger<ExfilActionEngine> _logger;

    /// <summary>Initializes the action engine with all dependencies.</summary>
    public ExfilActionEngine(
        AlertOnlyAction alertAction,
        ThrottleProcessAction throttleAction,
        BlockIpAction blockAction,
        IThreatIntelProvider threatIntel,
        IDataVolumeTracker tracker,
        IAuditLogRepository auditLog,
        ExfilWatchOptions options,
        ILogger<ExfilActionEngine> logger)
    {
        _alertAction = alertAction;
        _throttleAction = throttleAction;
        _blockAction = blockAction;
        _threatIntel = threatIntel;
        _tracker = tracker;
        _auditLog = auditLog;
        _options = options;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task ExecuteAsync(ExfilFinding finding, CancellationToken ct)
    {
        // Cloud whitelist suppression check
        if (ShouldSuppressForCloudWhitelist(finding))
        {
            _logger.LogInformation(
                "EXFIL ACTION: Suppressed alert for cloud-whitelisted destination {Dest} (under threshold)",
                finding.Destination);
            return;
        }

        // Determine actions based on severity decision matrix
        var actions = GetActionsForSeverity(finding.Severity);

        foreach (var action in actions)
        {
            await ExecuteWithTimeoutAndFallback(action, finding, ct);
        }
    }

    /// <summary>
    /// Checks if the finding should be suppressed due to cloud whitelist.
    /// Suppressed if destination is a known cloud provider AND cumulative bytes
    /// from the same process are under the configurable threshold (default 1 GB/hr).
    /// The "OneDrive abuse detection" pattern: alert fires if over threshold.
    /// </summary>
    public bool ShouldSuppressForCloudWhitelist(ExfilFinding finding)
    {
        bool isCloudOrWhitelisted =
            _threatIntel.IsKnownCloudProvider(finding.Destination) ||
            _threatIntel.IsWhitelistedDestination(finding.Destination);

        if (!isCloudOrWhitelisted)
            return false;

        // Check cumulative bytes to this destination in the past hour
        long cumulativeBytes = _tracker.GetBytesSentToDestination(
            finding.Destination, TimeSpan.FromHours(1));

        long thresholdBytes = (long)(_options.CloudAlertThresholdGbPerHour * GigabyteInBytes);

        // Suppress if under threshold; alert fires if over (OneDrive abuse detection)
        return cumulativeBytes < thresholdBytes;
    }

    /// <summary>
    /// Returns the ordered list of actions for the given severity level.
    /// </summary>
    public IReadOnlyList<IExfilAction> GetActionsForSeverity(ExfilSeverity severity)
    {
        return severity switch
        {
            ExfilSeverity.Low => [_alertAction],
            ExfilSeverity.Medium => [_alertAction],
            ExfilSeverity.High => [_alertAction, _throttleAction],
            ExfilSeverity.Critical => [_alertAction, _throttleAction, _blockAction],
            _ => [_alertAction]
        };
    }

    private async Task ExecuteWithTimeoutAndFallback(
        IExfilAction action, ExfilFinding finding, CancellationToken ct)
    {
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(ActionTimeout);

            var result = await action.ExecuteAsync(finding, timeoutCts.Token);

            if (!result.Success)
            {
                _logger.LogWarning("EXFIL ACTION [{Action}] failed, applying fallback", action.ActionName);
                await ApplyFallback(action, finding, ct);
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogError("EXFIL ACTION [{Action}] timed out after {Timeout}s",
                action.ActionName, ActionTimeout.TotalSeconds);

            await _auditLog.AppendAsync(
                "ExfilActionTimeout",
                $"Action={action.ActionName}, Timeout={ActionTimeout.TotalSeconds}s",
                entityType: "ExfilFinding",
                cancellationToken: ct);

            await ApplyFallback(action, finding, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "EXFIL ACTION [{Action}] threw exception", action.ActionName);

            await _auditLog.AppendAsync(
                "ExfilActionError",
                $"Action={action.ActionName}, Error={ex.Message}",
                entityType: "ExfilFinding",
                cancellationToken: ct);

            await ApplyFallback(action, finding, ct);
        }
    }

    /// <summary>
    /// Graceful fallback: BlockIp → ThrottleProcess, ThrottleProcess → AlertOnly.
    /// </summary>
    private async Task ApplyFallback(IExfilAction failedAction, ExfilFinding finding, CancellationToken ct)
    {
        IExfilAction? fallback = failedAction.ActionName switch
        {
            "BlockIp" => _throttleAction,
            "ThrottleProcess" => _alertAction,
            _ => null
        };

        if (fallback is null)
            return;

        _logger.LogWarning("EXFIL ACTION: Falling back from {Failed} to {Fallback}",
            failedAction.ActionName, fallback.ActionName);

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(ActionTimeout);
            await fallback.ExecuteAsync(finding, timeoutCts.Token);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "EXFIL ACTION: Fallback {Fallback} also failed", fallback.ActionName);
        }
    }
}
