using RansomGuard.Agent.Core.Configuration;
using RansomGuard.Agent.Core.Detection.ExfilWatch.Models;

namespace RansomGuard.Agent.Core.Detection.ExfilWatch.Rules;

/// <summary>
/// Rule 2: Suspicious destination — detects large outbound transfers to
/// unknown/non-whitelisted destinations that are not known cloud providers.
/// MITRE T1041 — Exfiltration Over C2 Channel.
/// </summary>
public sealed class SuspiciousDestinationRule : IExfilDetectionRule
{
    private const long ByteThreshold = 50_000_000; // 50 MB
    private readonly IThreatIntelProvider _threatIntel;

    /// <summary>Initializes the rule with a threat intel provider.</summary>
    public SuspiciousDestinationRule(IThreatIntelProvider threatIntel)
    {
        _threatIntel = threatIntel;
    }

    /// <inheritdoc />
    public string RuleName => "SuspiciousDestination";

    /// <inheritdoc />
    public ExfilFinding? Evaluate(NetworkEvent evt, IDataVolumeTracker tracker, ExfilWatchOptions options)
    {
        if (evt.EventType != NetworkEventType.TcpSend || evt.DestinationAddress is null)
            return null;

        long sentLastHour = tracker.GetBytesSentToDestination(evt.DestinationAddress, TimeSpan.FromHours(1));
        if (sentLastHour < ByteThreshold)
            return null;

        if (_threatIntel.IsWhitelistedDestination(evt.DestinationAddress))
            return null;

        if (_threatIntel.IsKnownCloudProvider(evt.DestinationAddress))
            return null;

        return new ExfilFinding
        {
            RuleName = RuleName,
            Severity = ExfilSeverity.Medium,
            Description = $"Suspicious destination: {evt.DestinationAddress}, " +
                          $"{sentLastHour / 1024 / 1024} MB sent in 1 hour",
            ProcessId = evt.ProcessId,
            ProcessName = evt.ProcessName,
            Destination = evt.DestinationAddress,
            DestinationPort = evt.DestinationPort,
            BytesTransferred = sentLastHour,
            MitreId = "T1041"
        };
    }
}
