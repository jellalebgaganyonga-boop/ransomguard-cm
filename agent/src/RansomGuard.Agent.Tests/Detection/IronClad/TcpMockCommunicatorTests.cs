using System.Net;
using System.Net.Sockets;
using RansomGuard.Agent.Core.Detection.IronClad;
using RansomGuard.Agent.Core.Detection.IronClad.Communication;
using RansomGuard.Agent.Core.Detection.IronClad.Models;
using Serilog;

namespace RansomGuard.Agent.Tests.Detection.IronClad;

public sealed class TcpMockCommunicatorTests : IAsyncDisposable
{
    private readonly ILogger _logger = new LoggerConfiguration().CreateLogger();

    public async ValueTask DisposeAsync()
    {
        // Cleanup handled per-test
        await Task.CompletedTask;
    }

    private static (TcpListener listener, int port) CreateEphemeralServer()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        return (listener, port);
    }

    private static IronCladOptions MakeOptions(int port) => new()
    {
        TcpMockEndpoint = $"localhost:{port}",
        RelayCount = 4,
        CommandTimeoutSeconds = 5,
        HeartbeatIntervalSeconds = 60, // high to avoid interference in short tests
        HeartbeatTimeoutSeconds = 30,
        FailSafeRestoreOnDisconnect = true
    };

    [Fact]
    public async Task Connect_Without_Server_Returns_False_After_Timeout()
    {
        // Use a port that nobody is listening on
        var options = new IronCladOptions
        {
            TcpMockEndpoint = "localhost:59999",
            RelayCount = 4,
            CommandTimeoutSeconds = 5,
            HeartbeatIntervalSeconds = 60,
            HeartbeatTimeoutSeconds = 30,
            FailSafeRestoreOnDisconnect = true
        };

        await using var comm = new TcpMockCommunicator(options, _logger);

        var result = await comm.ConnectAsync();

        Assert.False(result);
        Assert.False(comm.IsConnected);
    }

    [Fact]
    public async Task SendCommand_Without_Connect_Throws_InvalidOperationException()
    {
        var options = MakeOptions(59998);
        await using var comm = new TcpMockCommunicator(options, _logger);

        var cmd = new IronCladCommand
        {
            Id = Guid.NewGuid(),
            Action = IronCladAction.Status,
            Parameter = "",
            IssuedAt = DateTime.UtcNow
        };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => comm.SendCommandAsync(cmd));
    }

    [Fact]
    public async Task Connect_To_Test_Server_Succeeds()
    {
        var (listener, port) = CreateEphemeralServer();
        try
        {
            var options = MakeOptions(port);
            await using var comm = new TcpMockCommunicator(options, _logger);

            var result = await comm.ConnectAsync();

            Assert.True(result);
            Assert.True(comm.IsConnected);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task SendCommand_Receives_Ack_Within_Timeout()
    {
        var (listener, port) = CreateEphemeralServer();
        try
        {
            // Start echo server that responds with ACK
            var serverTask = Task.Run(async () =>
            {
                using var client = await listener.AcceptTcpClientAsync();
                using var reader = new StreamReader(client.GetStream());
                await using var writer = new StreamWriter(client.GetStream()) { AutoFlush = true };

                var line = await reader.ReadLineAsync();
                if (line?.StartsWith("CMD:CUT_PORT") == true)
                {
                    await writer.WriteLineAsync("ACK:PORT_3_CUT");
                }
            });

            var options = MakeOptions(port);
            await using var comm = new TcpMockCommunicator(options, _logger);
            await comm.ConnectAsync();

            var cmd = new IronCladCommand
            {
                Id = Guid.NewGuid(),
                Action = IronCladAction.CutPort,
                Parameter = "3",
                IssuedAt = DateTime.UtcNow,
                Timeout = TimeSpan.FromSeconds(5)
            };

            var response = await comm.SendCommandAsync(cmd);

            Assert.Equal(IronCladResponseType.Ack, response.Type);
            Assert.True(response.IsAcknowledgement);
            Assert.Equal("ACK:PORT_3_CUT", response.RawPayload);

            await serverTask;
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task SendCommand_Beyond_Timeout_Returns_Error_Response()
    {
        var (listener, port) = CreateEphemeralServer();
        try
        {
            // Server accepts but never responds
            var serverTask = Task.Run(async () =>
            {
                using var client = await listener.AcceptTcpClientAsync();
                await Task.Delay(10_000); // hang
            });

            var options = MakeOptions(port);
            await using var comm = new TcpMockCommunicator(options, _logger);
            await comm.ConnectAsync();

            var cmd = new IronCladCommand
            {
                Id = Guid.NewGuid(),
                Action = IronCladAction.Status,
                Parameter = "",
                IssuedAt = DateTime.UtcNow,
                Timeout = TimeSpan.FromMilliseconds(500)
            };

            var response = await comm.SendCommandAsync(cmd);

            Assert.Equal(IronCladResponseType.Error, response.Type);
            Assert.Equal("TIMEOUT", response.ErrorCode);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task Connection_Loss_Raises_ConnectionLost_Event()
    {
        var (listener, port) = CreateEphemeralServer();
        ConnectionLostEventArgs? lostArgs = null;
        try
        {
            // Server accepts then immediately closes
            var serverTask = Task.Run(async () =>
            {
                using var client = await listener.AcceptTcpClientAsync();
                await Task.Delay(200);
                client.Close(); // Force disconnect
            });

            var options = MakeOptions(port);
            await using var comm = new TcpMockCommunicator(options, _logger);
            comm.ConnectionLost += (_, args) => lostArgs = args;

            await comm.ConnectAsync();
            await serverTask;

            // Wait for reader loop to detect disconnect
            await Task.Delay(1000);

            Assert.NotNull(lostArgs);
            Assert.False(comm.IsConnected);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task Heartbeat_Event_Fires_On_Pong()
    {
        var (listener, port) = CreateEphemeralServer();
        HeartbeatEventArgs? heartbeatArgs = null;
        try
        {
            var serverTask = Task.Run(async () =>
            {
                using var client = await listener.AcceptTcpClientAsync();
                using var reader = new StreamReader(client.GetStream());
                await using var writer = new StreamWriter(client.GetStream()) { AutoFlush = true };

                var line = await reader.ReadLineAsync();
                if (line?.Contains("HEARTBEAT") == true)
                {
                    await writer.WriteLineAsync("PONG");
                }

                await Task.Delay(2000); // Keep alive
            });

            var options = new IronCladOptions
            {
                TcpMockEndpoint = $"localhost:{port}",
                RelayCount = 4,
                CommandTimeoutSeconds = 5,
                HeartbeatIntervalSeconds = 1,
                HeartbeatTimeoutSeconds = 30,
                FailSafeRestoreOnDisconnect = true
            };
            await using var comm = new TcpMockCommunicator(options, _logger);
            comm.HeartbeatReceived += (_, args) => heartbeatArgs = args;

            await comm.ConnectAsync();

            // Wait for heartbeat cycle
            await Task.Delay(2000);

            Assert.NotNull(heartbeatArgs);
            Assert.True(heartbeatArgs.Latency > TimeSpan.Zero);

            await serverTask;
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task DisposeAsync_Cleans_Resources_No_Leak()
    {
        var (listener, port) = CreateEphemeralServer();
        try
        {
            var options = MakeOptions(port);
            var comm = new TcpMockCommunicator(options, _logger);
            await comm.ConnectAsync();
            Assert.True(comm.IsConnected);

            await comm.DisposeAsync();

            Assert.False(comm.IsConnected);
        }
        finally
        {
            listener.Stop();
        }
    }
}
