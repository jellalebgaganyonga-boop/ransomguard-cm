using RansomGuard.Agent.Core.Configuration;
using RansomGuard.Agent.Core.Detection.ExfilWatch.Models;
using RansomGuard.Agent.Core.Persistence.Entities;

namespace RansomGuard.Agent.Core.Detection.ExfilWatch.Rules;

/// <summary>
/// Rule 3: Unknown process exfiltration — detects network activity from
/// processes not seen during the baseline learning phase.
/// MITRE T1041 — Exfiltration Over C2 Channel.
/// </summary>
public sealed class UnknownProcessExfilRule : IExfilDetectionRule
{
    private const long ByteThreshold = 10_000_000; // 10 MB
    private readonly INetworkBaselineService _baseline;

    /// <summary>Initializes the rule with the baseline service.</summary>
    public UnknownProcessExfilRule(INetworkBaselineService baseline)
    {
        _baseline = baseline;
    }

    /// <inheritdoc />
    public string RuleName => "UnknownProcessExfil";

    /// <inheritdoc />
    public ExfilFinding? Evaluate(NetworkEvent evt, IDataVolumeTracker tracker, ExfilWatchOptions options)
    {
        if (evt.EventType != NetworkEventType.TcpSend || evt.DestinationAddress is null)
            return null;

        long sentLastHour = tracker.GetBytesSentToDestination(evt.DestinationAddress, TimeSpan.FromHours(1));
        if (sentLastHour < ByteThreshold)
            return null;

        // Skip during learning phase (sync check via tracker)
        if (tracker.IsLearningPhase)
            return null;

        // Check if process was seen during baseline learning
        bool isKnown = _baseline.IsKnownDimensionAsync(
            evt.ProcessName, BaselineMetricType.ProcessNetworkVolume,
            CancellationToken.None).GetAwaiter().GetResult();

        if (isKnown)
            return null;

        return new ExfilFinding
        {
            RuleName = RuleName,
            Severity = ExfilSeverity.High,
            Description = $"Unknown process performing network exfiltration: {evt.ProcessName}, " +
                          $"{sentLastHour / 1024 / 1024} MB to {evt.DestinationAddress}",
            ProcessId = evt.ProcessId,
            ProcessName = evt.ProcessName,
            Destination = evt.DestinationAddress,
            DestinationPort = evt.DestinationPort,
            BytesTransferred = sentLastHour,
            MitreId = "T1041"
        };
    }
}
