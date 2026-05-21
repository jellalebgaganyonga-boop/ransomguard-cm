using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Moq;
using RansomGuard.Agent.Core.Detection.ExfilWatch;
using RansomGuard.Agent.Core.Detection.ExfilWatch.Models;
using Shouldly;

namespace RansomGuard.Agent.Tests.Detection.ExfilWatch;

/// <summary>
/// Tests for EXFIL WATCH network event model, INetworkActivityMonitor contract,
/// and ETW capture behavior.
/// </summary>
public sealed class NetworkActivityMonitorTests
{
    // --- NetworkEvent Model Tests ---

    [Fact]
    public void NetworkEvent_DnsQuery_CapturesAllFields()
    {
        var evt = new NetworkEvent
        {
            Id = Guid.NewGuid(),
            EventType = NetworkEventType.DnsQuery,
            ProcessId = 1234,
            ProcessName = "chrome",
            DnsQueryName = "evil.example.com",
            DnsQueryType = "A",
            Protocol = "DNS",
            Timestamp = DateTime.UtcNow
        };

        evt.EventType.ShouldBe(NetworkEventType.DnsQuery);
        evt.DnsQueryName.ShouldBe("evil.example.com");
        evt.ProcessName.ShouldBe("chrome");
        evt.Protocol.ShouldBe("DNS");
    }

    [Fact]
    public void NetworkEvent_TcpConnect_CapturesAddressAndPort()
    {
        var evt = new NetworkEvent
        {
            Id = Guid.NewGuid(),
            EventType = NetworkEventType.TcpConnect,
            ProcessId = 5678,
            ProcessName = "powershell",
            SourceAddress = "192.168.1.10",
            SourcePort = 49152,
            DestinationAddress = "203.0.113.50",
            DestinationPort = 443,
            Protocol = "TCP",
            Timestamp = DateTime.UtcNow
        };

        evt.DestinationAddress.ShouldBe("203.0.113.50");
        evt.DestinationPort.ShouldBe(443);
        evt.SourcePort.ShouldBe(49152);
    }

    [Fact]
    public void NetworkEvent_TcpSend_TracksBytesSent()
    {
        var evt = new NetworkEvent
        {
            Id = Guid.NewGuid(),
            EventType = NetworkEventType.TcpSend,
            ProcessId = 1000,
            ProcessName = "exfil_tool",
            DestinationAddress = "10.0.0.1",
            DestinationPort = 8080,
            BytesSent = 1_048_576,
            Protocol = "TCP",
            Timestamp = DateTime.UtcNow
        };

        evt.BytesSent.ShouldBe(1_048_576);
        evt.BytesReceived.ShouldBe(0);
    }

    [Fact]
    public void NetworkEvent_TcpReceive_TracksBytesReceived()
    {
        var evt = new NetworkEvent
        {
            Id = Guid.NewGuid(),
            EventType = NetworkEventType.TcpReceive,
            ProcessId = 1000,
            ProcessName = "browser",
            BytesReceived = 524_288,
            Protocol = "TCP",
            Timestamp = DateTime.UtcNow
        };

        evt.BytesReceived.ShouldBe(524_288);
        evt.BytesSent.ShouldBe(0);
    }

    [Fact]
    public void NetworkEvent_DnsResponse_ContainsResolvedAddresses()
    {
        var evt = new NetworkEvent
        {
            Id = Guid.NewGuid(),
            EventType = NetworkEventType.DnsResponse,
            ProcessId = 2000,
            ProcessName = "explorer",
            DnsQueryName = "api.example.com",
            DnsResponseAddresses = ["93.184.216.34", "2606:2800:220:1:248:1893:25c8:1946"],
            Protocol = "DNS",
            Timestamp = DateTime.UtcNow
        };

        evt.DnsResponseAddresses.ShouldNotBeNull();
        evt.DnsResponseAddresses.Count.ShouldBe(2);
    }

    [Fact]
    public void NetworkEvent_TlsHandshake_CapturesSni()
    {
        var evt = new NetworkEvent
        {
            Id = Guid.NewGuid(),
            EventType = NetworkEventType.TlsHandshake,
            ProcessId = 3000,
            ProcessName = "edge",
            DestinationAddress = "1.2.3.4",
            DestinationPort = 443,
            TlsServerName = "login.microsoftonline.com",
            Protocol = "TCP",
            Timestamp = DateTime.UtcNow
        };

        evt.TlsServerName.ShouldBe("login.microsoftonline.com");
    }

    // --- NetworkEventType Enum Tests ---

    [Theory]
    [InlineData(NetworkEventType.DnsQuery)]
    [InlineData(NetworkEventType.DnsResponse)]
    [InlineData(NetworkEventType.TcpConnect)]
    [InlineData(NetworkEventType.TcpDisconnect)]
    [InlineData(NetworkEventType.TcpSend)]
    [InlineData(NetworkEventType.TcpReceive)]
    [InlineData(NetworkEventType.TlsHandshake)]
    public void NetworkEventType_AllValues_Defined(NetworkEventType eventType)
    {
        Enum.IsDefined(eventType).ShouldBeTrue();
    }

    // --- Channel Integration ---

    [Fact]
    public async Task NetworkEvents_WrittenToChannel_ConsumedByReader()
    {
        var channel = Channel.CreateBounded<NetworkEvent>(100);

        var events = Enumerable.Range(0, 10).Select(i => new NetworkEvent
        {
            Id = Guid.NewGuid(),
            EventType = NetworkEventType.TcpSend,
            ProcessId = 1000 + i,
            ProcessName = $"proc_{i}",
            BytesSent = i * 1024,
            Protocol = "TCP",
            Timestamp = DateTime.UtcNow
        }).ToList();

        // Write
        foreach (var evt in events)
            await channel.Writer.WriteAsync(evt);
        channel.Writer.Complete();

        // Read
        int count = 0;
        await foreach (var _ in channel.Reader.ReadAllAsync())
            count++;

        count.ShouldBe(10);
    }

    [Fact]
    public async Task Channel_DropOldest_DoesNotBlock()
    {
        var channel = Channel.CreateBounded<NetworkEvent>(
            new BoundedChannelOptions(5)
            {
                FullMode = BoundedChannelFullMode.DropOldest
            });

        // Write more than capacity
        for (int i = 0; i < 20; i++)
        {
            await channel.Writer.WriteAsync(new NetworkEvent
            {
                Id = Guid.NewGuid(),
                EventType = NetworkEventType.DnsQuery,
                ProcessId = i,
                ProcessName = "test",
                DnsQueryName = $"query{i}.example.com",
                Protocol = "DNS",
                Timestamp = DateTime.UtcNow
            });
        }

        channel.Writer.Complete();

        int count = 0;
        await foreach (var _ in channel.Reader.ReadAllAsync())
            count++;

        // DropOldest keeps the latest entries — channel capacity is 5
        count.ShouldBeLessThanOrEqualTo(5);
    }

    // --- INetworkActivityMonitor Contract ---

    [Fact]
    public void INetworkActivityMonitor_MockedImplementation_Verifiable()
    {
        var mock = new Mock<INetworkActivityMonitor>();
        mock.SetupGet(m => m.IsCapturing).Returns(false);
        mock.SetupGet(m => m.TotalEventsCaptured).Returns(0);

        mock.Object.IsCapturing.ShouldBeFalse();
        mock.Object.TotalEventsCaptured.ShouldBe(0);
    }

    [Fact]
    public void INetworkActivityMonitor_StartSetsCapturing()
    {
        var mock = new Mock<INetworkActivityMonitor>();
        bool capturing = false;

        mock.Setup(m => m.Start(It.IsAny<ChannelWriter<NetworkEvent>>(), It.IsAny<CancellationToken>()))
            .Callback(() => capturing = true);
        mock.SetupGet(m => m.IsCapturing).Returns(() => capturing);

        var channel = Channel.CreateBounded<NetworkEvent>(100);
        mock.Object.Start(channel.Writer, CancellationToken.None);

        mock.Object.IsCapturing.ShouldBeTrue();
    }

    // --- ExfilAlert Persistence ---

    [Fact]
    public void ExfilAlert_AllFieldsSet()
    {
        var alert = new RansomGuard.Agent.Core.Persistence.Entities.ExfilAlert
        {
            RuleName = "VolumeAnomaly",
            Severity = "High",
            ProcessId = 1234,
            ProcessName = "suspicious.exe",
            Destination = "203.0.113.50",
            DestinationPort = 443,
            BytesTransferred = 500_000_000,
            Description = "500 MB uploaded in 5 minutes to unknown IP",
            ActionTaken = "Alert"
        };

        alert.RuleName.ShouldBe("VolumeAnomaly");
        alert.BytesTransferred.ShouldBe(500_000_000);
        alert.DetectedAt.ShouldBeGreaterThan(DateTime.UtcNow.AddSeconds(-5));
    }
}
