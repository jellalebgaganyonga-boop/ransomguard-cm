using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RansomGuard.Agent.Core.Detection.IronClad;
using RansomGuard.Agent.Core.Detection.IronClad.Communication;

namespace RansomGuard.Agent.Service;

/// <summary>
/// Background service monitoring IronClad device liveness via heartbeats.
/// Raises Critical alert after HeartbeatTimeoutSeconds without heartbeat.
/// Resets on reconnection.
/// </summary>
public sealed class IronCladHeartbeatService : BackgroundService
{
    private readonly IIronCladCommunicator _communicator;
    private readonly IronCladOptions _options;
    private readonly ILogger<IronCladHeartbeatService> _logger;
    private DateTime _lastHeartbeatAt;
    private long _consecutiveMissed;
    private bool _alertRaised;

    /// <summary>Number of consecutive missed heartbeats since last successful one.</summary>
    public long ConsecutiveMissed => _consecutiveMissed;

    /// <summary>Initializes the heartbeat monitor.</summary>
    public IronCladHeartbeatService(
        IIronCladCommunicator communicator,
        IOptions<IronCladOptions> options,
        ILogger<IronCladHeartbeatService> logger)
    {
        _communicator = communicator;
        _options = options.Value;
        _logger = logger;
        _lastHeartbeatAt = DateTime.UtcNow;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("IronClad disabled, heartbeat monitor not starting");
            return;
        }

        _communicator.HeartbeatReceived += OnHeartbeatReceived;
        _communicator.ConnectionLost += OnConnectionLost;

        _logger.LogInformation("IronClad heartbeat monitor started (timeout={Timeout}s)", _options.HeartbeatTimeoutSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(_options.HeartbeatIntervalSeconds), stoppingToken)
                .ConfigureAwait(false);

            var elapsed = (DateTime.UtcNow - _lastHeartbeatAt).TotalSeconds;
            if (elapsed > _options.HeartbeatTimeoutSeconds && !_alertRaised)
            {
                _alertRaised = true;
                _logger.LogCritical("IronClad device unreachable — no heartbeat for {Elapsed}s (threshold: {Threshold}s)",
                    (int)elapsed, _options.HeartbeatTimeoutSeconds);
            }

            if (elapsed > _options.HeartbeatIntervalSeconds)
            {
                Interlocked.Increment(ref _consecutiveMissed);
            }
        }
    }

    private void OnHeartbeatReceived(object? sender, HeartbeatEventArgs e)
    {
        _lastHeartbeatAt = e.Timestamp;
        var missed = Interlocked.Exchange(ref _consecutiveMissed, 0);

        if (_alertRaised)
        {
            _alertRaised = false;
            _logger.LogInformation("IronClad device reconnected after {Missed} missed heartbeats (latency={Latency}ms)",
                missed, (int)e.Latency.TotalMilliseconds);
        }
    }

    private void OnConnectionLost(object? sender, ConnectionLostEventArgs e)
    {
        _logger.LogWarning("IronClad connection lost: {Reason}", e.Reason);
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        _communicator.HeartbeatReceived -= OnHeartbeatReceived;
        _communicator.ConnectionLost -= OnConnectionLost;
        base.Dispose();
    }
}
