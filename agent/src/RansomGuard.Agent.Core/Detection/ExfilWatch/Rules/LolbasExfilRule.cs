using RansomGuard.Agent.Core.Configuration;
using RansomGuard.Agent.Core.Detection.ExfilWatch.Models;

namespace RansomGuard.Agent.Core.Detection.ExfilWatch.Rules;

/// <summary>
/// Rule 7: LOLBAS exfiltration — detects Living-Off-the-Land binaries
/// performing outbound data transfers. Critical severity for modern ransomware.
/// MITRE T1218 + T1041 — System Binary Proxy Execution + Exfiltration Over C2 Channel.
/// </summary>
public sealed class LolbasExfilRule : IExfilDetectionRule
{
    private const long ByteThreshold = 1_000_000; // 1 MB

    /// <inheritdoc />
    public string RuleName => "LolbasExfil";

    /// <inheritdoc />
    public ExfilFinding? Evaluate(NetworkEvent evt, IDataVolumeTracker tracker, ExfilWatchOptions options)
    {
        if (evt.EventType != NetworkEventType.TcpSend || evt.DestinationAddress is null)
            return null;

        if (evt.BytesSent < ByteThreshold)
            return null;

        if (!LolbasBinaries.Names.Contains(evt.ProcessName))
            return null;

        return new ExfilFinding
        {
            RuleName = RuleName,
            Severity = ExfilSeverity.Critical,
            Description = $"LOLBAS exfiltration: {evt.ProcessName} sent " +
                          $"{evt.BytesSent / 1024 / 1024} MB to {evt.DestinationAddress}",
            ProcessId = evt.ProcessId,
            ProcessName = evt.ProcessName,
            Destination = evt.DestinationAddress,
            DestinationPort = evt.DestinationPort,
            BytesTransferred = evt.BytesSent,
            MitreId = "T1218+T1041"
        };
    }
}
