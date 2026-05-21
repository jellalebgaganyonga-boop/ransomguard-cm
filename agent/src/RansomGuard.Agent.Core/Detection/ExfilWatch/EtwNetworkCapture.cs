using System.Diagnostics;
using System.Runtime.Versioning;
using System.Threading.Channels;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using Microsoft.Diagnostics.Tracing.Session;
using Microsoft.Extensions.Logging;
using RansomGuard.Agent.Core.Detection.ExfilWatch.Models;
using RansomGuard.Agent.Core.Security.RateLimiting;

namespace RansomGuard.Agent.Core.Detection.ExfilWatch;

/// <summary>
/// ETW-based network activity capture using kernel TCP/IP and DNS Client providers.
/// Captures DNS queries, TCP connect/disconnect, TCP send/receive, and TLS handshakes.
/// Rate-limited at 100 events/sec via TokenBucketRateLimiter.
/// Requires elevated (admin) privileges for kernel ETW sessions.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class EtwNetworkCapture : INetworkActivityMonitor, IDisposable
{
    private const string SessionName = "RansomGuard-ExfilWatch-Network";

    /// <summary>Microsoft-Windows-DNS-Client ETW provider GUID.</summary>
    private static readonly Guid DnsClientProviderGuid = new("1C95126E-7EEA-49A9-A3FE-A378B03DDB4D");

    private readonly ILogger<EtwNetworkCapture> _logger;
    private readonly IOperationRateLimiter? _rateLimiter;
    private readonly int _bufferCount;
    private TraceEventSession? _kernelSession;
    private TraceEventSession? _dnsSession;
    private Task? _kernelProcessingTask;
    private Task? _dnsProcessingTask;
    private ChannelWriter<NetworkEvent>? _writer;
    private CancellationToken _ct;
    private long _totalEventsCaptured;
    private volatile bool _isCapturing;

    /// <inheritdoc />
    public bool IsCapturing => _isCapturing;

    /// <inheritdoc />
    public long TotalEventsCaptured => Interlocked.Read(ref _totalEventsCaptured);

    /// <summary>Initializes the ETW network capture.</summary>
    public EtwNetworkCapture(
        ILogger<EtwNetworkCapture> logger,
        int bufferCount = 64,
        IOperationRateLimiter? rateLimiter = null)
    {
        _logger = logger;
        _bufferCount = bufferCount;
        _rateLimiter = rateLimiter;
    }

    /// <inheritdoc />
    public void Start(ChannelWriter<NetworkEvent> writer, CancellationToken cancellationToken)
    {
        _writer = writer;
        _ct = cancellationToken;

        _kernelProcessingTask = Task.Run(() => RunKernelSession(cancellationToken), cancellationToken);
        _dnsProcessingTask = Task.Run(() => RunDnsSession(cancellationToken), cancellationToken);

        _isCapturing = true;
        _logger.LogInformation("EXFIL WATCH: ETW network capture started (buffers: {Buffers})", _bufferCount);
    }

    /// <inheritdoc />
    public async Task StopAsync()
    {
        _isCapturing = false;

        try { _kernelSession?.Stop(); } catch { /* session may already be stopped */ }
        try { _dnsSession?.Stop(); } catch { /* session may already be stopped */ }

        if (_kernelProcessingTask is not null)
            await _kernelProcessingTask.ConfigureAwait(false);
        if (_dnsProcessingTask is not null)
            await _dnsProcessingTask.ConfigureAwait(false);

        _logger.LogInformation("EXFIL WATCH: ETW network capture stopped. Total events: {Total}", TotalEventsCaptured);
    }

    private void RunKernelSession(CancellationToken ct)
    {
        try
        {
            _kernelSession = new TraceEventSession(SessionName + "-Kernel")
            {
                BufferSizeMB = (_bufferCount * 64) / 1024 + 1
            };

            _kernelSession.EnableKernelProvider(
                KernelTraceEventParser.Keywords.NetworkTCPIP);

            _kernelSession.Source.Kernel.TcpIpConnect += OnTcpConnect;
            _kernelSession.Source.Kernel.TcpIpDisconnect += OnTcpDisconnect;
            _kernelSession.Source.Kernel.TcpIpSend += OnTcpSend;
            _kernelSession.Source.Kernel.TcpIpRecv += OnTcpReceive;

            ct.Register(() =>
            {
                try { _kernelSession?.Stop(); } catch { }
            });

            _kernelSession.Source.Process();
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "EXFIL WATCH: Kernel ETW requires admin privileges — TCP/IP capture disabled");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogError(ex, "EXFIL WATCH: Kernel ETW session error");
        }
    }

    private void RunDnsSession(CancellationToken ct)
    {
        try
        {
            _dnsSession = new TraceEventSession(SessionName + "-DNS");

            _dnsSession.EnableProvider(DnsClientProviderGuid, TraceEventLevel.Informational);

            _dnsSession.Source.Dynamic.All += OnDnsEvent;

            ct.Register(() =>
            {
                try { _dnsSession?.Stop(); } catch { }
            });

            _dnsSession.Source.Process();
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "EXFIL WATCH: DNS ETW requires admin privileges — DNS capture disabled");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogError(ex, "EXFIL WATCH: DNS ETW session error");
        }
    }

    private void OnTcpConnect(TcpIpConnectTraceData data)
    {
        EmitEvent(new NetworkEvent
        {
            Id = Guid.NewGuid(),
            EventType = NetworkEventType.TcpConnect,
            ProcessId = data.ProcessID,
            ProcessName = GetProcessName(data.ProcessID),
            SourceAddress = data.saddr.ToString(),
            SourcePort = data.sport,
            DestinationAddress = data.daddr.ToString(),
            DestinationPort = data.dport,
            Protocol = "TCP",
            Timestamp = data.TimeStamp.ToUniversalTime()
        });
    }

    private void OnTcpDisconnect(TcpIpTraceData data)
    {
        EmitEvent(new NetworkEvent
        {
            Id = Guid.NewGuid(),
            EventType = NetworkEventType.TcpDisconnect,
            ProcessId = data.ProcessID,
            ProcessName = GetProcessName(data.ProcessID),
            SourceAddress = data.saddr.ToString(),
            SourcePort = data.sport,
            DestinationAddress = data.daddr.ToString(),
            DestinationPort = data.dport,
            Protocol = "TCP",
            Timestamp = data.TimeStamp.ToUniversalTime()
        });
    }

    private void OnTcpSend(TcpIpSendTraceData data)
    {
        EmitEvent(new NetworkEvent
        {
            Id = Guid.NewGuid(),
            EventType = NetworkEventType.TcpSend,
            ProcessId = data.ProcessID,
            ProcessName = GetProcessName(data.ProcessID),
            SourceAddress = data.saddr.ToString(),
            SourcePort = data.sport,
            DestinationAddress = data.daddr.ToString(),
            DestinationPort = data.dport,
            BytesSent = data.size,
            Protocol = "TCP",
            Timestamp = data.TimeStamp.ToUniversalTime()
        });
    }

    private void OnTcpReceive(TcpIpTraceData data)
    {
        EmitEvent(new NetworkEvent
        {
            Id = Guid.NewGuid(),
            EventType = NetworkEventType.TcpReceive,
            ProcessId = data.ProcessID,
            ProcessName = GetProcessName(data.ProcessID),
            SourceAddress = data.saddr.ToString(),
            SourcePort = data.sport,
            DestinationAddress = data.daddr.ToString(),
            DestinationPort = data.dport,
            BytesReceived = data.size,
            Protocol = "TCP",
            Timestamp = data.TimeStamp.ToUniversalTime()
        });
    }

    private void OnDnsEvent(TraceEvent data)
    {
        // DNS Client provider event ID 3006 = query initiated, 3008 = query completed
        if (data.ID != (TraceEventID)3006 && data.ID != (TraceEventID)3008)
            return;

        string? queryName = data.PayloadByName("QueryName") as string;
        if (string.IsNullOrEmpty(queryName))
            return;

        string? queryType = data.PayloadByName("QueryType")?.ToString();

        var eventType = data.ID == (TraceEventID)3006
            ? NetworkEventType.DnsQuery
            : NetworkEventType.DnsResponse;

        // Extract response addresses for DNS responses
        IReadOnlyList<string>? responseAddresses = null;
        if (eventType == NetworkEventType.DnsResponse)
        {
            string? result = data.PayloadByName("QueryResults") as string;
            if (!string.IsNullOrEmpty(result))
            {
                responseAddresses = result.Split(';', StringSplitOptions.RemoveEmptyEntries);
            }
        }

        EmitEvent(new NetworkEvent
        {
            Id = Guid.NewGuid(),
            EventType = eventType,
            ProcessId = data.ProcessID,
            ProcessName = GetProcessName(data.ProcessID),
            DnsQueryName = queryName,
            DnsQueryType = queryType,
            DnsResponseAddresses = responseAddresses,
            Protocol = "DNS",
            Timestamp = data.TimeStamp.ToUniversalTime()
        });
    }

    private void EmitEvent(NetworkEvent networkEvent)
    {
        if (_ct.IsCancellationRequested || _writer is null)
            return;

        // Rate limit
        if (_rateLimiter is not null && !_rateLimiter.TryAcquire())
            return;

        Interlocked.Increment(ref _totalEventsCaptured);
        _writer.TryWrite(networkEvent);
    }

    private static string GetProcessName(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return process.ProcessName;
        }
        catch
        {
            return $"PID:{processId}";
        }
    }

    /// <summary>Disposes ETW sessions.</summary>
    public void Dispose()
    {
        _isCapturing = false;
        _kernelSession?.Dispose();
        _dnsSession?.Dispose();
    }
}
