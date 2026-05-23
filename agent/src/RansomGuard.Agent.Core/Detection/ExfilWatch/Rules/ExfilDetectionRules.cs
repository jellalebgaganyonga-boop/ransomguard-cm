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

/// <summary>
/// Rule 4: Cloud upload spike — detects rapid uploads to cloud provider IPs.
/// MITRE T1567 — Exfiltration Over Web Service.
/// </summary>
public sealed class CloudUploadSpikeRule : IExfilDetectionRule
{
    /// <inheritdoc />
    public string RuleName => "CloudUploadSpike";

    /// <inheritdoc />
    public ExfilFinding? Evaluate(NetworkEvent evt, IDataVolumeTracker tracker, ExfilWatchOptions options)
    {
        if (evt.EventType != NetworkEventType.TcpSend || evt.DestinationAddress is null)
            return null;

        long cloudBytes = tracker.GetCloudUploadBytes(TimeSpan.FromHours(1));
        long thresholdBytes = (long)(options.CloudAlertThresholdGbPerHour * 1024 * 1024 * 1024);

        if (cloudBytes < thresholdBytes)
            return null;

        return new ExfilFinding
        {
            RuleName = RuleName,
            Severity = ExfilSeverity.High,
            Description = $"Cloud upload spike: {cloudBytes / 1024 / 1024} MB to cloud in 1 hour " +
                          $"(threshold: {options.CloudAlertThresholdGbPerHour} GB/hour)",
            ProcessId = evt.ProcessId,
            ProcessName = evt.ProcessName,
            Destination = evt.DestinationAddress,
            DestinationPort = evt.DestinationPort,
            BytesTransferred = cloudBytes,
            MitreId = "T1567"
        };
    }
}

/// <summary>
/// Rule 5: Beaconing — detects periodic TCP connections to the same destination
/// at regular intervals (C2 heartbeat pattern).
/// MITRE T1071 — Application Layer Protocol.
/// </summary>
public sealed class BeaconingRule : IExfilDetectionRule
{
    /// <inheritdoc />
    public string RuleName => "Beaconing";

    /// <inheritdoc />
    public ExfilFinding? Evaluate(NetworkEvent evt, IDataVolumeTracker tracker, ExfilWatchOptions options)
    {
        if (evt.EventType != NetworkEventType.TcpConnect || evt.DestinationAddress is null)
            return null;

        var connections = tracker.GetActiveConnections(evt.ProcessId);
        var toSameDest = connections
            .Where(c => c.DestinationAddress == evt.DestinationAddress)
            .OrderBy(c => c.Timestamp)
            .ToList();

        if (toSameDest.Count < 5)
            return null;

        // Compute inter-arrival intervals
        var intervals = new List<double>();
        for (int i = 1; i < toSameDest.Count; i++)
        {
            intervals.Add((toSameDest[i].Timestamp - toSameDest[i - 1].Timestamp).TotalSeconds);
        }

        if (intervals.Count < 3)
            return null;

        double mean = intervals.Average();
        double stdDev = Math.Sqrt(intervals.Average(i => Math.Pow(i - mean, 2)));

        // Low jitter = beaconing (coefficient of variation < 0.3)
        if (mean < 1 || stdDev / mean > 0.3)
            return null;

        return new ExfilFinding
        {
            RuleName = RuleName,
            Severity = ExfilSeverity.Medium,
            Description = $"Beaconing pattern: {toSameDest.Count} connections to {evt.DestinationAddress} " +
                          $"at ~{mean:F0}s intervals (jitter: {stdDev / mean:F2})",
            ProcessId = evt.ProcessId,
            ProcessName = evt.ProcessName,
            Destination = evt.DestinationAddress,
            DestinationPort = evt.DestinationPort,
            BytesTransferred = 0,
            MitreId = "T1071"
        };
    }
}

/// <summary>
/// Rule 6: Rare destination — detects connections to IPs/domains never seen
/// during the learning phase.
/// MITRE T1041 — Exfiltration Over C2 Channel.
/// </summary>
public sealed class RareDestinationRule : IExfilDetectionRule
{
    /// <inheritdoc />
    public string RuleName => "RareDestination";

    /// <inheritdoc />
    public ExfilFinding? Evaluate(NetworkEvent evt, IDataVolumeTracker tracker, ExfilWatchOptions options)
    {
        if (evt.EventType != NetworkEventType.TcpConnect || evt.DestinationAddress is null)
            return null;

        if (tracker.IsLearningPhase)
            return null;

        // If destination has no baseline, it was never seen during learning
        long? baseline = tracker.GetBaselineBytesSentPerHour(evt.DestinationAddress);
        if (baseline is not null)
            return null; // Known destination

        // Only flag if there's meaningful data being sent
        long sentLast5Min = tracker.GetBytesSentToDestination(evt.DestinationAddress, TimeSpan.FromMinutes(5));
        if (sentLast5Min < 1024 * 1024) // Less than 1 MB — ignore
            return null;

        return new ExfilFinding
        {
            RuleName = RuleName,
            Severity = ExfilSeverity.Medium,
            Description = $"Rare destination: {evt.ProcessName} connected to {evt.DestinationAddress}:{evt.DestinationPort} " +
                          $"(never seen during learning, {sentLast5Min / 1024} KB transferred)",
            ProcessId = evt.ProcessId,
            ProcessName = evt.ProcessName,
            Destination = evt.DestinationAddress,
            DestinationPort = evt.DestinationPort,
            BytesTransferred = sentLast5Min,
            MitreId = "T1041"
        };
    }
}

/// <summary>
/// Rule 7: Port scan/spray — detects a process connecting to many unique
/// destinations in a short window (reconnaissance or mass exfil).
/// MITRE T1046 — Network Service Discovery.
/// </summary>
public sealed class DestinationSprayRule : IExfilDetectionRule
{
    private const int UniqueDestThreshold = 50;

    /// <inheritdoc />
    public string RuleName => "DestinationSpray";

    /// <inheritdoc />
    public ExfilFinding? Evaluate(NetworkEvent evt, IDataVolumeTracker tracker, ExfilWatchOptions options)
    {
        if (evt.EventType != NetworkEventType.TcpConnect)
            return null;

        int uniqueDests = tracker.GetUniqueDestinationCount(evt.ProcessId, TimeSpan.FromMinutes(5));
        if (uniqueDests < UniqueDestThreshold)
            return null;

        return new ExfilFinding
        {
            RuleName = RuleName,
            Severity = ExfilSeverity.High,
            Description = $"Destination spray: {evt.ProcessName} connected to {uniqueDests} unique destinations in 5 min",
            ProcessId = evt.ProcessId,
            ProcessName = evt.ProcessName,
            Destination = evt.DestinationAddress ?? "multiple",
            DestinationPort = evt.DestinationPort,
            BytesTransferred = 0,
            MitreId = "T1046"
        };
    }
}

/// <summary>
/// Rule 8: Large single transfer — detects a single TCP send exceeding
/// a threshold (indicative of file exfiltration via raw socket).
/// MITRE T1048.001 — Exfiltration Over Symmetric Encrypted Non-C2 Protocol.
/// </summary>
public sealed class LargeSingleTransferRule : IExfilDetectionRule
{
    private const long LargeTransferThreshold = 100 * 1024 * 1024; // 100 MB

    /// <inheritdoc />
    public string RuleName => "LargeSingleTransfer";

    /// <inheritdoc />
    public ExfilFinding? Evaluate(NetworkEvent evt, IDataVolumeTracker tracker, ExfilWatchOptions options)
    {
        if (evt.EventType != NetworkEventType.TcpSend)
            return null;

        if (evt.BytesSent < LargeTransferThreshold)
            return null;

        return new ExfilFinding
        {
            RuleName = RuleName,
            Severity = evt.BytesSent > LargeTransferThreshold * 5 ? ExfilSeverity.Critical : ExfilSeverity.High,
            Description = $"Large single transfer: {evt.BytesSent / 1024 / 1024} MB sent to " +
                          $"{evt.DestinationAddress}:{evt.DestinationPort} by {evt.ProcessName}",
            ProcessId = evt.ProcessId,
            ProcessName = evt.ProcessName,
            Destination = evt.DestinationAddress ?? "unknown",
            DestinationPort = evt.DestinationPort,
            BytesTransferred = evt.BytesSent,
            MitreId = "T1048.001"
        };
    }
}
