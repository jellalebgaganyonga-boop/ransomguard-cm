using Microsoft.Extensions.Logging;
using RansomGuard.Agent.Core.Detection.ExfilWatch.Rules;
using RansomGuard.Agent.Core.Persistence.Repositories;

namespace RansomGuard.Agent.Core.Detection.ExfilWatch.Actions;

/// <summary>
/// Throttles a suspect process's network bandwidth to 10% of the measured anomaly
/// using Windows QoS API via <see cref="IProcessThrottler"/>.
/// Enabled for severity High and Critical.
/// </summary>
public sealed class ThrottleProcessAction : IExfilAction
{
    private readonly IProcessThrottler _throttler;
    private readonly IAuditLogRepository _auditLog;
    private readonly ILogger<ThrottleProcessAction> _logger;

    /// <inheritdoc />
    public string ActionName => "ThrottleProcess";

    /// <summary>Initializes the throttle action.</summary>
    public ThrottleProcessAction(
        IProcessThrottler throttler,
        IAuditLogRepository auditLog,
        ILogger<ThrottleProcessAction> logger)
    {
        _throttler = throttler;
        _auditLog = auditLog;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ExfilActionResult> ExecuteAsync(ExfilFinding finding, CancellationToken ct)
    {
        // Calculate 10% of measured anomaly bandwidth (bytes/sec → bits/sec)
        // Assume the BytesTransferred occurred over 1 hour (detection window)
        long bytesPerSecond = Math.Max(1, finding.BytesTransferred / 3600);
        long throttleBitsPerSecond = Math.Max(8000, bytesPerSecond * 8 / 10); // 10% floor of 1 KB/s

        bool applied = _throttler.ApplyThrottle(
            finding.ProcessId, finding.ProcessName, throttleBitsPerSecond);

        string details = $"Process={finding.ProcessName} (PID {finding.ProcessId}), " +
                         $"ThrottleBps={throttleBitsPerSecond}, Applied={applied}";

        await _auditLog.AppendAsync(
            applied ? "ExfilThrottle" : "ExfilThrottleFailed",
            details,
            entityType: "ExfilFinding",
            cancellationToken: ct);

        if (applied)
            _logger.LogWarning("EXFIL ACTION [Throttle]: {Details}", details);
        else
            _logger.LogError("EXFIL ACTION [Throttle] FAILED: {Details}", details);

        return new ExfilActionResult
        {
            Success = applied,
            ActionName = ActionName,
            Details = details
        };
    }
}
