using RansomGuard.Agent.Core.Configuration;
using RansomGuard.Agent.Core.Detection.ExfilWatch.Models;

namespace RansomGuard.Agent.Core.Detection.ExfilWatch.Rules;

/// <summary>
/// Rule 5: Tor traffic — detects outbound connections to known Tor ports
/// or Tor exit node IPs. Any Tor usage is critical in enterprise environments.
/// MITRE T1090.003 — Proxy: Multi-hop Proxy.
/// </summary>
public sealed class TorTrafficRule : IExfilDetectionRule
{
    private static readonly HashSet<int> TorPorts = new() { 9001, 9030, 9050, 9051, 9150 };
    private readonly IThreatIntelProvider _threatIntel;

    /// <summary>Initializes the rule with a threat intel provider.</summary>
    public TorTrafficRule(IThreatIntelProvider threatIntel)
    {
        _threatIntel = threatIntel;
    }

    /// <inheritdoc />
    public string RuleName => "TorTraffic";

    /// <inheritdoc />
    public ExfilFinding? Evaluate(NetworkEvent evt, IDataVolumeTracker tracker, ExfilWatchOptions options)
    {
        if (evt.EventType != NetworkEventType.TcpConnect || evt.DestinationAddress is null)
            return null;

        bool portMatch = evt.DestinationPort.HasValue && TorPorts.Contains(evt.DestinationPort.Value);
        bool exitNodeMatch = _threatIntel.IsTorExitNode(evt.DestinationAddress);

        if (!portMatch && !exitNodeMatch)
            return null;

        return new ExfilFinding
        {
            RuleName = RuleName,
            Severity = ExfilSeverity.Critical,
            Description = $"Tor traffic detected: {evt.ProcessName} connecting to " +
                          $"{evt.DestinationAddress}:{evt.DestinationPort}" +
                          (portMatch ? " (Tor port)" : "") +
                          (exitNodeMatch ? " (exit node)" : ""),
            ProcessId = evt.ProcessId,
            ProcessName = evt.ProcessName,
            Destination = evt.DestinationAddress,
            DestinationPort = evt.DestinationPort,
            BytesTransferred = 0,
            MitreId = "T1090.003"
        };
    }
}
