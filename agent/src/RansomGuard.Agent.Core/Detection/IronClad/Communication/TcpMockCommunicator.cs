using System.Collections.Concurrent;
using System.Net.Sockets;
using Microsoft.Extensions.Options;
using RansomGuard.Agent.Core.Detection.IronClad.Models;
using Serilog;

namespace RansomGuard.Agent.Core.Detection.IronClad.Communication;

/// <summary>
/// IronClad communicator using TCP socket to MockArduinoServer (localhost:9999).
/// Sprint 5 software-only implementation. Sprint 8 will swap for SerialPortCommunicator.
/// Thread-safe: SemaphoreSlim enforces one outstanding command at a time (CWE-400).
/// </summary>
public sealed class TcpMockCommunicator : IIronCladCommunicator
{
    private readonly IronCladOptions _options;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _commandLock = new(1, 1);
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource<IronCladResponse>> _pendingCommands = new();
    private readonly ConcurrentQueue<TimeSpan> _latencyHistory = new();

    private TcpClient? _client;
    private StreamReader? _reader;
    private StreamWriter? _writer;
    private CancellationTokenSource? _cts;
    private Task? _readerTask;
    private Timer? _heartbeatTimer;
    private DateTime _lastHeartbeatAt;
    private volatile bool _isConnected;
    private string? _deviceVersion;
    private int _reconnectAttempts;

    /// <inheritdoc />
    public bool IsConnected => _isConnected;

    /// <inheritdoc />
    public string? DeviceVersion => _deviceVersion;

    /// <inheritdoc />
    public int PortCount => _options.RelayCount;

    /// <inheritdoc />
    public event EventHandler<HeartbeatEventArgs>? HeartbeatReceived;

    /// <inheritdoc />
    public event EventHandler<ConnectionLostEventArgs>? ConnectionLost;

    /// <summary>
    /// Initializes the TCP mock communicator.
    /// </summary>
    public TcpMockCommunicator(IOptions<IronCladOptions> options, ILogger logger)
    {
        _options = options.Value;
        _logger = logger.ForContext<TcpMockCommunicator>();
        _lastHeartbeatAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Initializes with explicit options (for testing without DI).
    /// </summary>
    public TcpMockCommunicator(IronCladOptions options, ILogger logger)
    {
        _options = options;
        _logger = logger.ForContext<TcpMockCommunicator>();
        _lastHeartbeatAt = DateTime.UtcNow;
    }

    /// <inheritdoc />
    public async Task<bool> ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (_isConnected)
            return true;

        var parts = _options.TcpMockEndpoint.Split(':');
        var host = parts[0];
        var port = parts.Length > 1 && int.TryParse(parts[1], out var p) ? p : 9999;

        try
        {
            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _client = new TcpClient();

            using var connectCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, connectCts.Token);
            await _client.ConnectAsync(host, port, linked.Token).ConfigureAwait(false);

            var stream = _client.GetStream();
            _reader = new StreamReader(stream);
            _writer = new StreamWriter(stream) { AutoFlush = true };
            _isConnected = true;
            _reconnectAttempts = 0;
            _lastHeartbeatAt = DateTime.UtcNow;

            _readerTask = Task.Run(() => ReadLoopAsync(_cts.Token), _cts.Token);

            _heartbeatTimer = new Timer(
                _ => _ = SendHeartbeatAsync(),
                null,
                TimeSpan.FromSeconds(_options.HeartbeatIntervalSeconds),
                TimeSpan.FromSeconds(_options.HeartbeatIntervalSeconds));

            _logger.Information("IronClad TCP connected to {Endpoint}", _options.TcpMockEndpoint);
            return true;
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException or IOException)
        {
            _logger.Warning("IronClad TCP connection failed: {Error}", ex.Message);
            _isConnected = false;
            return false;
        }
    }

    /// <inheritdoc />
    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        _isConnected = false;
        _heartbeatTimer?.Dispose();
        _heartbeatTimer = null;

        if (_cts is not null)
        {
            await _cts.CancelAsync().ConfigureAwait(false);
        }

        if (_readerTask is not null)
        {
            try { await _readerTask.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }

        _writer?.Dispose();
        _reader?.Dispose();
        _client?.Dispose();
        _writer = null;
        _reader = null;
        _client = null;

        _logger.Information("IronClad TCP disconnected");
    }

    /// <inheritdoc />
    public async Task<IronCladResponse> SendCommandAsync(IronCladCommand command, CancellationToken cancellationToken = default)
    {
        if (!_isConnected || _writer is null)
            throw new InvalidOperationException("Not connected to IronClad device. Call ConnectAsync first.");

        await _commandLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var tcs = new TaskCompletionSource<IronCladResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pendingCommands[command.Id] = tcs;

            var wireCommand = IronCladProtocol.Serialize(command);
            await _writer.WriteLineAsync(wireCommand).ConfigureAwait(false);

            using var timeoutCts = new CancellationTokenSource(command.Timeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            await using var reg = linked.Token.Register(() => tcs.TrySetCanceled());

            try
            {
                return await tcs.Task.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
            {
                return new IronCladResponse
                {
                    CommandId = command.Id,
                    Type = IronCladResponseType.Error,
                    RawPayload = "",
                    ReceivedAt = DateTime.UtcNow,
                    IsAcknowledgement = false,
                    ErrorCode = "TIMEOUT",
                    ErrorMessage = $"Command timed out after {command.Timeout.TotalSeconds}s"
                };
            }
            finally
            {
                _pendingCommands.TryRemove(command.Id, out _);
            }
        }
        finally
        {
            _commandLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<HealthStatus> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        var portStates = new Dictionary<int, PortState>();

        if (_isConnected)
        {
            try
            {
                var statusCmd = new IronCladCommand
                {
                    Id = Guid.NewGuid(),
                    Action = IronCladAction.Status,
                    Parameter = "",
                    IssuedAt = DateTime.UtcNow
                };
                var statusResp = await SendCommandAsync(statusCmd, cancellationToken).ConfigureAwait(false);
                if (statusResp.Type == IronCladResponseType.Status)
                {
                    portStates = new Dictionary<int, PortState>(IronCladProtocol.ParsePortStates(statusResp.RawPayload));
                }

                if (_deviceVersion is null)
                {
                    var versionCmd = new IronCladCommand
                    {
                        Id = Guid.NewGuid(),
                        Action = IronCladAction.Version,
                        Parameter = "",
                        IssuedAt = DateTime.UtcNow
                    };
                    var versionResp = await SendCommandAsync(versionCmd, cancellationToken).ConfigureAwait(false);
                    if (versionResp.Type == IronCladResponseType.Version)
                    {
                        _deviceVersion = IronCladProtocol.ParseVersion(versionResp.RawPayload);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Warning("Health check failed: {Error}", ex.Message);
            }
        }

        var avgLatency = TimeSpan.Zero;
        if (!_latencyHistory.IsEmpty)
        {
            var samples = _latencyHistory.ToArray();
            avgLatency = TimeSpan.FromTicks((long)samples.Select(s => s.Ticks).Average());
        }

        return new HealthStatus
        {
            IsConnected = _isConnected,
            DeviceVersion = _deviceVersion,
            PortCount = _options.RelayCount,
            PortStates = portStates,
            LastHeartbeatAt = _lastHeartbeatAt,
            AverageLatency = avgLatency,
            MissedHeartbeats = 0
        };
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync().ConfigureAwait(false);
        _cts?.Dispose();
        _commandLock.Dispose();
    }

    private async Task ReadLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && _reader is not null)
            {
                var line = await _reader.ReadLineAsync(ct).ConfigureAwait(false);
                if (line is null)
                {
                    HandleConnectionLost("Device closed connection");
                    return;
                }

                if (line.Trim() == "PONG")
                {
                    // Unsolicited heartbeat from server — complete any pending heartbeat command
                    CompletePendingOrRaiseHeartbeat(line);
                    continue;
                }

                // Try to complete a pending command
                CompletePendingCommand(line);
            }
        }
        catch (OperationCanceledException) { }
        catch (IOException ex)
        {
            HandleConnectionLost(ex.Message);
        }
    }

    private void CompletePendingCommand(string line)
    {
        // Complete the first pending command (serial protocol — one at a time)
        foreach (var kvp in _pendingCommands)
        {
            var response = IronCladProtocol.Parse(line, kvp.Key);
            if (kvp.Value.TrySetResult(response))
            {
                _pendingCommands.TryRemove(kvp.Key, out _);
                return;
            }
        }
    }

    private void CompletePendingOrRaiseHeartbeat(string line)
    {
        var now = DateTime.UtcNow;
        var latency = now - _lastHeartbeatAt;
        _lastHeartbeatAt = now;

        // Keep last 60 latency samples
        _latencyHistory.Enqueue(latency);
        while (_latencyHistory.Count > 60)
            _latencyHistory.TryDequeue(out _);

        // Try to complete a pending heartbeat command first
        foreach (var kvp in _pendingCommands)
        {
            var response = IronCladProtocol.Parse(line, kvp.Key);
            if (kvp.Value.TrySetResult(response))
            {
                _pendingCommands.TryRemove(kvp.Key, out _);
                break;
            }
        }

        HeartbeatReceived?.Invoke(this, new HeartbeatEventArgs
        {
            Timestamp = now,
            Latency = latency
        });
    }

    private async Task SendHeartbeatAsync()
    {
        if (!_isConnected || _writer is null)
            return;

        try
        {
            var cmd = new IronCladCommand
            {
                Id = Guid.NewGuid(),
                Action = IronCladAction.Heartbeat,
                Parameter = "",
                IssuedAt = DateTime.UtcNow,
                Timeout = TimeSpan.FromSeconds(5)
            };
            await SendCommandAsync(cmd).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Debug("Heartbeat send failed: {Error}", ex.Message);
        }
    }

    private void HandleConnectionLost(string reason)
    {
        if (!_isConnected) return;
        _isConnected = false;

        _logger.Warning("IronClad connection lost: {Reason}", reason);

        // Fail all pending commands
        foreach (var kvp in _pendingCommands)
        {
            kvp.Value.TrySetCanceled();
            _pendingCommands.TryRemove(kvp.Key, out _);
        }

        ConnectionLost?.Invoke(this, new ConnectionLostEventArgs
        {
            DisconnectedAt = DateTime.UtcNow,
            Reason = reason
        });

        // Start background reconnection
        _ = Task.Run(ReconnectLoopAsync);
    }

    private async Task ReconnectLoopAsync()
    {
        const int maxAttempts = 12;
        while (_reconnectAttempts < maxAttempts && !_isConnected)
        {
            _reconnectAttempts++;
            await Task.Delay(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

            _logger.Information("IronClad reconnection attempt {Attempt}/{Max}", _reconnectAttempts, maxAttempts);
            if (await ConnectAsync().ConfigureAwait(false))
            {
                _logger.Information("IronClad reconnected after {Attempts} attempts", _reconnectAttempts);
                return;
            }
        }

        _logger.Error("IronClad reconnection failed after {Max} attempts — giving up", maxAttempts);
    }
}
