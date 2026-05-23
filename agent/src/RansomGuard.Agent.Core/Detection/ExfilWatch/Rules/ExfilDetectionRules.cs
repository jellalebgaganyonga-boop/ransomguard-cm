using RansomGuard.Agent.Core.Configuration;
using RansomGuard.Agent.Core.Detection.ExfilWatch.Models;

namespace RansomGuard.Agent.Core.Detection.ExfilWatch.Rules;

/// <summary>
/// Rule 1: Volume anomaly — detects when a process sends more than
/// VolumeAnomalyMultiplier * baseline bytes/hour to a destination.
/// MITRE T1041 — Exfiltration Over C2 Channel.
/// </summary>
public sealed class VolumeAnomalyRule : IExfilDetectionRule
{
    /// <inheritdoc />
    public string RuleName => "VolumeAnomaly";

    /// <inheritdoc />
    public ExfilFinding? Evaluate(NetworkEvent evt, IDataVolumeTracker tracker, ExfilWatchOptions options)
    {
        if (evt.EventType != NetworkEventType.TcpSend || evt.DestinationAddress is null)
            return null;

        if (tracker.IsLearningPhase)
            return null;

        long? baseline = tracker.GetBaselineBytesSentPerHour(evt.DestinationAddress);
        if (baseline is null || baseline.Value == 0)
            return null;

        long sentLastHour = tracker.GetBytesSentToDestination(evt.DestinationAddress, TimeSpan.FromHours(1));
        long threshold = baseline.Value * options.VolumeAnomalyMultiplier;

        if (sentLastHour <= threshold)
            return null;

        return new ExfilFinding
        {
            RuleName = RuleName,
            Severity = sentLastHour > threshold * 3 ? ExfilSeverity.Critical : ExfilSeverity.High,
            Description = $"Volume anomaly: {sentLastHour / 1024 / 1024} MB/hour to {evt.DestinationAddress} " +
                          $"(baseline: {baseline.Value / 1024 / 1024} MB/hour, threshold: {options.VolumeAnomalyMultiplier}x)",
            ProcessId = evt.ProcessId,
            ProcessName = evt.ProcessName,
            Destination = evt.DestinationAddress,
            DestinationPort = evt.DestinationPort,
            BytesTransferred = sentLastHour,
            MitreId = "T1041"
        };
    }
}

/// <summary>
/// Rule 4: DNS tunneling — detects high-frequency DNS queries with high-entropy
/// subdomain labels (data encoded in DNS queries).
/// MITRE T1071.004 — Application Layer Protocol: DNS.
/// </summary>
public sealed class DnsTunnelingRule : IExfilDetectionRule
{
    /// <inheritdoc />
    public string RuleName => "DnsTunneling";

    /// <inheritdoc />
    public ExfilFinding? Evaluate(NetworkEvent evt, IDataVolumeTracker tracker, ExfilWatchOptions options)
    {
        if (evt.EventType != NetworkEventType.DnsQuery || evt.DnsQueryName is null)
            return null;

        // Check query frequency
        int queryCount = tracker.GetDnsQueryCount(evt.ProcessId, TimeSpan.FromMinutes(5));
        if (queryCount < options.DnsTunnelingQueryThreshold)
            return null;

        // Check subdomain entropy
        double entropy = ComputeSubdomainEntropy(evt.DnsQueryName);
        if (entropy < options.DnsTunnelingEntropyThreshold)
            return null;

        return new ExfilFinding
        {
            RuleName = RuleName,
            Severity = ExfilSeverity.Critical,
            Description = $"DNS tunneling suspected: {queryCount} queries in 5 min to " +
                          $"{GetBaseDomain(evt.DnsQueryName)} (subdomain entropy: {entropy:F2})",
            ProcessId = evt.ProcessId,
            ProcessName = evt.ProcessName,
            Destination = evt.DnsQueryName,
            BytesTransferred = queryCount * 253, // Max DNS label length estimate
            MitreId = "T1071.004"
        };
    }

    /// <summary>Computes Shannon entropy of the subdomain portion of a DNS name.</summary>
    public static double ComputeSubdomainEntropy(string queryName)
    {
        string subdomain = GetSubdomain(queryName);
        if (subdomain.Length == 0) return 0;

        var freq = new Dictionary<char, int>();
        foreach (char c in subdomain)
        {
            freq.TryGetValue(c, out int count);
            freq[c] = count + 1;
        }

        double entropy = 0;
        int len = subdomain.Length;
        foreach (var count in freq.Values)
        {
            double p = (double)count / len;
            entropy -= p * Math.Log2(p);
        }

        return entropy;
    }

    private static string GetSubdomain(string queryName)
    {
        var parts = queryName.Split('.');
        return parts.Length > 2 ? string.Join(".", parts[..^2]) : "";
    }

    private static string GetBaseDomain(string queryName)
    {
        var parts = queryName.Split('.');
        return parts.Length >= 2 ? string.Join(".", parts[^2..]) : queryName;
    }
}

/// <summary>
/// Rule 6: After-hours exfiltration — detects large uploads outside working hours.
/// MITRE T1041 — Exfiltration Over C2 Channel.
/// </summary>
public sealed class AfterHoursExfilRule : IExfilDetectionRule
{
    /// <inheritdoc />
    public string RuleName => "AfterHoursExfil";

    /// <inheritdoc />
    public ExfilFinding? Evaluate(NetworkEvent evt, IDataVolumeTracker tracker, ExfilWatchOptions options)
    {
        if (evt.EventType != NetworkEventType.TcpSend || evt.DestinationAddress is null)
            return null;

        int hour = DateTime.Now.Hour;
        bool isWorkingHours = hour >= options.WorkingHoursStart && hour < options.WorkingHoursEnd;
        if (isWorkingHours)
            return null;

        long sentLast5Min = tracker.GetBytesSent(evt.ProcessId, TimeSpan.FromMinutes(5));
        const long threshold = 50 * 1024 * 1024; // 50 MB in 5 min

        if (sentLast5Min < threshold)
            return null;

        return new ExfilFinding
        {
            RuleName = RuleName,
            Severity = ExfilSeverity.High,
            Description = $"Off-hours transfer: {sentLast5Min / 1024 / 1024} MB sent by {evt.ProcessName} " +
                          $"at {DateTime.Now:HH:mm} (working hours: {options.WorkingHoursStart}-{options.WorkingHoursEnd})",
            ProcessId = evt.ProcessId,
            ProcessName = evt.ProcessName,
            Destination = evt.DestinationAddress,
            DestinationPort = evt.DestinationPort,
            BytesTransferred = sentLast5Min,
            MitreId = "T1041"
        };
    }
}
