namespace RansomGuard.Agent.Core.Detection.ExfilWatch.Models;

/// <summary>
/// Represents a network activity event captured via ETW or socket tracing.
/// Covers DNS queries, TCP connections, and TLS handshakes.
/// </summary>
public sealed record NetworkEvent
{
    /// <summary>Unique event identifier.</summary>
    public required Guid Id { get; init; }

    /// <summary>Type of network event.</summary>
    public required NetworkEventType EventType { get; init; }

    /// <summary>Process ID that generated this event.</summary>
    public required int ProcessId { get; init; }

    /// <summary>Process name at time of capture.</summary>
    public required string ProcessName { get; init; }

    /// <summary>Source IP address (local), null for DNS queries.</summary>
    public string? SourceAddress { get; init; }

    /// <summary>Source port (local), null for DNS queries.</summary>
    public int? SourcePort { get; init; }

    /// <summary>Destination IP address or resolved address.</summary>
    public string? DestinationAddress { get; init; }

    /// <summary>Destination port.</summary>
    public int? DestinationPort { get; init; }

    /// <summary>DNS query domain name (for DNS events).</summary>
    public string? DnsQueryName { get; init; }

    /// <summary>DNS query type (A, AAAA, TXT, MX, CNAME, etc.).</summary>
    public string? DnsQueryType { get; init; }

    /// <summary>DNS response addresses (resolved IPs), null for non-DNS.</summary>
    public IReadOnlyList<string>? DnsResponseAddresses { get; init; }

    /// <summary>Bytes sent in this event (for TCP send events).</summary>
    public long BytesSent { get; init; }

    /// <summary>Bytes received in this event (for TCP receive events).</summary>
    public long BytesReceived { get; init; }

    /// <summary>TLS/SSL server name indication (SNI), null for non-TLS.</summary>
    public string? TlsServerName { get; init; }

    /// <summary>Protocol (TCP, UDP, DNS).</summary>
    public required string Protocol { get; init; }

    /// <summary>UTC timestamp when the event occurred.</summary>
    public required DateTime Timestamp { get; init; }
}

/// <summary>
/// Type of network event captured by the monitor.
/// </summary>
public enum NetworkEventType
{
    /// <summary>DNS name resolution query.</summary>
    DnsQuery,

    /// <summary>DNS query response received.</summary>
    DnsResponse,

    /// <summary>TCP connection established (outbound).</summary>
    TcpConnect,

    /// <summary>TCP connection closed.</summary>
    TcpDisconnect,

    /// <summary>TCP data sent (outbound bytes).</summary>
    TcpSend,

    /// <summary>TCP data received (inbound bytes).</summary>
    TcpReceive,

    /// <summary>TLS/SSL handshake completed.</summary>
    TlsHandshake
}
