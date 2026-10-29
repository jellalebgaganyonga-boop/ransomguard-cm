using System.Net;
using System.Net.Sockets;
using RansomGuard.Agent.Core.Detection.IronClad.Communication;
using RansomGuard.Agent.Core.Detection.IronClad.Models;
using Serilog;

namespace RansomGuard.Agent.Tests.Detection.IronClad;

public sealed class MockArduinoServerTests : IAsyncLifetime
{
    private MockArduinoServer _server = null!;
    private readonly ILogger _logger = new LoggerConfiguration().CreateLogger();

    public async Task InitializeAsync()
    {
        // 30 s client-timeout for the shared server, not 3: the previous value raced the test
        // that reads STATUS after CUT -- a slow CI runner between the two writes let the fail-safe
        // watchdog restore all ports before STATUS was asked (CI run 40193818441,
        // Status_Command_Returns_All_Port_States saw PORT_3 not PORT_3_CUT). The one test that
        // actually exercises the timeout builds its own tighter server.
        _server = new MockArduinoServer(tcpPort: 0, relayCount: 4, relayDelayMs: 10, clientTimeoutSeconds: 30, logger: _logger);
        await _server.StartAsync(CancellationToken.None);
    }

    public async Task DisposeAsync()
    {
        await _server.DisposeAsync();
    }

    private async Task<(StreamReader reader, StreamWriter writer, TcpClient client)> ConnectAsync()
    {
        var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, _server.ListeningPort);
        var stream = client.GetStream();
        var reader = new StreamReader(stream);
        var writer = new StreamWriter(stream) { AutoFlush = true };
        return (reader, writer, client);
    }

    [Fact]
    public void Server_Starts_On_Configured_Port()
    {
        Assert.True(_server.ListeningPort > 0);
    }

    [Fact]
    public async Task CutPort_Command_Updates_State_And_Returns_Ack()
    {
        var (reader, writer, client) = await ConnectAsync();
        using var _ = client;

        await writer.WriteLineAsync("CMD:CUT_PORT:3");
        var response = await reader.ReadLineAsync();

        Assert.Equal("ACK:PORT_3_CUT", response);

        var states = _server.GetPortStates();
        Assert.Equal(PortState.Cut, states[3]);
        Assert.Equal(PortState.Active, states[1]);
    }

    [Fact]
    public async Task CutPort_Invalid_Port_Returns_Error()
    {
        var (reader, writer, client) = await ConnectAsync();
        using var _ = client;

        await writer.WriteLineAsync("CMD:CUT_PORT:99");
        var response = await reader.ReadLineAsync();

        Assert.StartsWith("ERR:E001", response);
    }

    [Fact]
    public async Task CutPort_Already_Cut_Returns_E002()
    {
        var (reader, writer, client) = await ConnectAsync();
        using var _ = client;

        await writer.WriteLineAsync("CMD:CUT_PORT:2");
        await reader.ReadLineAsync(); // ACK first cut

        await writer.WriteLineAsync("CMD:CUT_PORT:2");
        var response = await reader.ReadLineAsync();

        Assert.StartsWith("ERR:E002", response);
    }

    [Fact]
    public async Task Status_Command_Returns_All_Port_States()
    {
        var (reader, writer, client) = await ConnectAsync();
        using var _ = client;

        // Cut port 3 first
        await writer.WriteLineAsync("CMD:CUT_PORT:3");
        await reader.ReadLineAsync();

        await writer.WriteLineAsync("CMD:STATUS");
        var response = await reader.ReadLineAsync();

        Assert.StartsWith("STATUS:", response);
        Assert.Contains("PORT_1_ACTIVE", response);
        Assert.Contains("PORT_3_CUT", response);

        var states = IronCladProtocol.ParsePortStates(response!);
        Assert.Equal(4, states.Count);
        Assert.Equal(PortState.Cut, states[3]);
    }

    [Fact]
    public async Task Version_Command_Returns_Mock_Version()
    {
        var (reader, writer, client) = await ConnectAsync();
        using var _ = client;

        await writer.WriteLineAsync("CMD:VERSION");
        var response = await reader.ReadLineAsync();

        Assert.Equal("VERSION:1.0.0-mock", response);
    }

    [Fact]
    public async Task Heartbeat_Returns_Pong()
    {
        var (reader, writer, client) = await ConnectAsync();
        using var _ = client;

        await writer.WriteLineAsync("CMD:HEARTBEAT");
        var response = await reader.ReadLineAsync();

        Assert.Equal("PONG", response);
    }

    [Fact(Timeout = 15_000)]
    public async Task Client_Timeout_Triggers_FailSafe_RestoreAll()
    {
        // This is the only test that actually needs a tight client-timeout: the shared server
        // runs with 30 s (see InitializeAsync for why), so a dedicated server is built here.
        await using var tightServer = new MockArduinoServer(
            tcpPort: 0, relayCount: 4, relayDelayMs: 10, clientTimeoutSeconds: 3, logger: _logger);
        await tightServer.StartAsync(CancellationToken.None);

        var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, tightServer.ListeningPort);
        using var _ = client;
        var stream = client.GetStream();
        var reader = new StreamReader(stream);
        var writer = new StreamWriter(stream) { AutoFlush = true };

        // Cut port 2
        await writer.WriteLineAsync("CMD:CUT_PORT:2");
        await reader.ReadLineAsync();
        Assert.Equal(PortState.Cut, tightServer.GetPortStates()[2]);

        // Wait for timeout (3 seconds + margin)
        await Task.Delay(5000);

        // Fail-safe should have restored all ports
        var states = tightServer.GetPortStates();
        Assert.Equal(PortState.Active, states[2]);
    }

    [Fact]
    public async Task CutAll_Then_RestoreAll_Cycle()
    {
        var (reader, writer, client) = await ConnectAsync();
        using var _ = client;

        await writer.WriteLineAsync("CMD:CUT_ALL");
        var response = await reader.ReadLineAsync();
        Assert.Equal("ACK:ALL_PORTS_CUT", response);

        var states = _server.GetPortStates();
        Assert.All(states.Values, s => Assert.Equal(PortState.Cut, s));

        await writer.WriteLineAsync("CMD:RESTORE_ALL");
        response = await reader.ReadLineAsync();
        Assert.Equal("ACK:ALL_RESTORED", response);

        states = _server.GetPortStates();
        Assert.All(states.Values, s => Assert.Equal(PortState.Active, s));
    }

    [Fact]
    public async Task Unknown_Command_Returns_E999()
    {
        var (reader, writer, client) = await ConnectAsync();
        using var _ = client;

        await writer.WriteLineAsync("GARBAGE");
        var response = await reader.ReadLineAsync();

        Assert.StartsWith("ERR:E999", response);
    }
}
