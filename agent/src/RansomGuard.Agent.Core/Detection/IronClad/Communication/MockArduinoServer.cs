using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using RansomGuard.Agent.Core.Detection.IronClad.Models;
using Serilog;

namespace RansomGuard.Agent.Core.Detection.IronClad.Communication;

/// <summary>
/// Simulates the IronClad Arduino relay controller firmware behavior over TCP.
/// Faithful behavioral mirror of future Sprint 8 real firmware:
/// - Parses CMD:ACTION[:PARAM] wire protocol
/// - Maintains relay state per port (ConcurrentDictionary)
/// - 50ms relay switching delay (real hardware timing)
/// - Fail-safe: 30-second client timeout triggers RestoreAll
/// - Heartbeat: responds PONG to CMD:HEARTBEAT
/// - Single client at a time (matches real serial connection)
/// </summary>
public sealed class MockArduinoServer : IAsyncDisposable
{
    private readonly int _tcpPort;
    private readonly int _relayCount;
    private readonly int _relayDelayMs;
    private readonly int _clientTimeoutSeconds;
    private readonly ILogger _logger;
    private readonly ConcurrentDictionary<int, PortState> _portStates = new();

    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _acceptTask;

    /// <summary>The TCP port this server is listening on. Available after StartAsync.</summary>
    public int ListeningPort { get; private set; }

    /// <summary>
    /// Initializes the mock Arduino server.
    /// </summary>
    /// <param name="tcpPort">TCP port to listen on. Use 0 for ephemeral port (tests).</param>
    /// <param name="relayCount">Number of relay ports to simulate.</param>
    /// <param name="relayDelayMs">Simulated relay switching delay in milliseconds.</param>
    /// <param name="clientTimeoutSeconds">Seconds without client message before fail-safe RestoreAll.</param>
    /// <param name="logger">Serilog logger instance.</param>
    public MockArduinoServer(
        int tcpPort = 9999,
        int relayCount = 4,
        int relayDelayMs = 50,
        int clientTimeoutSeconds = 30,
        ILogger? logger = null)
    {
        _tcpPort = tcpPort;
        _relayCount = relayCount;
        _relayDelayMs = relayDelayMs;
        _clientTimeoutSeconds = clientTimeoutSeconds;
        _logger = (logger ?? Log.Logger).ForContext<MockArduinoServer>();

        for (int i = 1; i <= _relayCount; i++)
            _portStates[i] = PortState.Active;
    }

    /// <summary>Starts the TCP listener and begins accepting clients.</summary>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _listener = new TcpListener(IPAddress.Loopback, _tcpPort);
        _listener.Start();
        ListeningPort = ((IPEndPoint)_listener.LocalEndpoint).Port;

        _logger.Information("MockArduinoServer started on port {Port} with {RelayCount} relays",
            ListeningPort, _relayCount);

        _acceptTask = AcceptClientLoopAsync(_cts.Token);
        return Task.CompletedTask;
    }

    /// <summary>Stops the TCP listener and disconnects any connected client.</summary>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_cts is not null)
            await _cts.CancelAsync().ConfigureAwait(false);

        _listener?.Stop();

        if (_acceptTask is not null)
        {
            try { await _acceptTask.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }

        _logger.Information("MockArduinoServer stopped");
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None).ConfigureAwait(false);
        _cts?.Dispose();
        _listener?.Stop();
    }

    /// <summary>Returns a snapshot of current port states.</summary>
    public IReadOnlyDictionary<int, PortState> GetPortStates()
        => new Dictionary<int, PortState>(_portStates);

    private async Task AcceptClientLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var client = await _listener!.AcceptTcpClientAsync(ct).ConfigureAwait(false);
                _logger.Information("MockArduino: client connected");

                // Handle one client at a time (real Arduino has single serial connection)
                await HandleClientAsync(client, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (SocketException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                _logger.Warning("MockArduino accept error: {Error}", ex.Message);
            }
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        using var _ = client;
        await using var stream = client.GetStream();
        using var reader = new StreamReader(stream);
        await using var writer = new StreamWriter(stream) { AutoFlush = true };

        var lastActivity = DateTime.UtcNow;

        using var clientCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        // Client timeout monitor
        var timeoutTask = Task.Run(async () =>
        {
            while (!clientCts.Token.IsCancellationRequested)
            {
                await Task.Delay(1000, clientCts.Token).ConfigureAwait(false);
                if ((DateTime.UtcNow - lastActivity).TotalSeconds > _clientTimeoutSeconds)
                {
                    _logger.Warning("MockArduino: client timeout ({Timeout}s), restoring all ports (fail-safe)",
                        _clientTimeoutSeconds);
                    await RestoreAllPortsAsync().ConfigureAwait(false);
                    lastActivity = DateTime.UtcNow; // Reset to avoid repeated restores
                }
            }
        }, clientCts.Token);

        try
        {
            while (!clientCts.Token.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(clientCts.Token).ConfigureAwait(false);
                if (line is null)
                {
                    _logger.Information("MockArduino: client disconnected");
                    break;
                }

                lastActivity = DateTime.UtcNow;
                var response = await ProcessCommandAsync(line.Trim()).ConfigureAwait(false);

                _logger.Debug("MockArduino: {Command} -> {Response}", line.Trim(), response);
                await writer.WriteLineAsync(response).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
        catch (IOException) { _logger.Information("MockArduino: client connection lost"); }
        finally
        {
            await clientCts.CancelAsync().ConfigureAwait(false);
            try { await timeoutTask.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }
    }

    private async Task<string> ProcessCommandAsync(string command)
    {
        if (!command.StartsWith("CMD:"))
            return "ERR:E999:Unknown command";

        var parts = command["CMD:".Length..].Split(':', 2);
        var action = parts[0];
        var param = parts.Length > 1 ? parts[1] : "";

        return action switch
        {
            "CUT_PORT" => await HandleCutPortAsync(param).ConfigureAwait(false),
            "RESTORE_PORT" => await HandleRestorePortAsync(param).ConfigureAwait(false),
            "CUT_ALL" => await HandleCutAllAsync().ConfigureAwait(false),
            "RESTORE_ALL" => await HandleRestoreAllAsync().ConfigureAwait(false),
            "STATUS" => HandleStatus(),
            "VERSION" => "VERSION:1.0.0-mock",
            "HEARTBEAT" => "PONG",
            _ => "ERR:E999:Unknown command"
        };
    }

    private async Task<string> HandleCutPortAsync(string param)
    {
        if (!int.TryParse(param, out var port) || port < 1 || port > _relayCount)
            return "ERR:E001:Invalid port number";

        if (_portStates.TryGetValue(port, out var state) && state == PortState.Cut)
            return "ERR:E002:Port already in requested state";

        await Task.Delay(_relayDelayMs).ConfigureAwait(false);
        _portStates[port] = PortState.Cut;
        return $"ACK:PORT_{port}_CUT";
    }

    private async Task<string> HandleRestorePortAsync(string param)
    {
        if (!int.TryParse(param, out var port) || port < 1 || port > _relayCount)
            return "ERR:E001:Invalid port number";

        if (_portStates.TryGetValue(port, out var state) && state == PortState.Active)
            return "ERR:E002:Port already in requested state";

        await Task.Delay(_relayDelayMs).ConfigureAwait(false);
        _portStates[port] = PortState.Active;
        return $"ACK:PORT_{port}_RESTORED";
    }

    private async Task<string> HandleCutAllAsync()
    {
        for (int i = 1; i <= _relayCount; i++)
        {
            await Task.Delay(_relayDelayMs).ConfigureAwait(false);
            _portStates[i] = PortState.Cut;
        }
        return "ACK:ALL_PORTS_CUT";
    }

    private async Task<string> HandleRestoreAllAsync()
    {
        await RestoreAllPortsAsync().ConfigureAwait(false);
        return "ACK:ALL_RESTORED";
    }

    private string HandleStatus()
    {
        var parts = new List<string>();
        for (int i = 1; i <= _relayCount; i++)
        {
            var state = _portStates.GetValueOrDefault(i, PortState.Unknown);
            var stateStr = state switch
            {
                PortState.Active => "ACTIVE",
                PortState.Cut => "CUT",
                PortState.Faulted => "FAULTED",
                _ => "UNKNOWN"
            };
            parts.Add($"PORT_{i}_{stateStr}");
        }
        return $"STATUS:{string.Join(';', parts)}";
    }

    private async Task RestoreAllPortsAsync()
    {
        for (int i = 1; i <= _relayCount; i++)
        {
            await Task.Delay(_relayDelayMs).ConfigureAwait(false);
            _portStates[i] = PortState.Active;
        }
    }
}
