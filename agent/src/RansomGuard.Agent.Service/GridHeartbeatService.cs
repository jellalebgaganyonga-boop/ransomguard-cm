using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RansomGuard.Agent.Core.Communication;
using RansomGuard.Agent.Core.Communication.Models;
using RansomGuard.Agent.Core.Configuration;

namespace RansomGuard.Agent.Service;

/// <summary>
/// Background service sending periodic heartbeats to the GRID server.
/// Heartbeat interval is driven by the server response (next_heartbeat_in_seconds)
/// with a fallback to the configured HeartbeatIntervalSeconds.
/// </summary>
public sealed class GridHeartbeatService : BackgroundService
{
    private readonly IGridApiClient _gridClient;
    private readonly EnrollmentService _enrollmentService;
    private readonly AgentConfiguration _config;
    private readonly ILogger<GridHeartbeatService> _logger;
    private readonly DateTime _startedAt = DateTime.UtcNow;

    /// <summary>Last list of pending command IDs returned by the server.</summary>
    public IReadOnlyList<string> LastPendingCommands { get; private set; } = [];

    public GridHeartbeatService(
        IGridApiClient gridClient,
        EnrollmentService enrollmentService,
        IOptions<AgentConfiguration> config,
        ILogger<GridHeartbeatService> logger)
    {
        _gridClient = gridClient;
        _enrollmentService = enrollmentService;
        _config = config.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Wait for enrollment to complete before starting heartbeat
        if (!_gridClient.IsEnrolled)
        {
            _logger.LogInformation("GridHeartbeatService: waiting for enrollment...");
            try
            {
                await Task.WhenAny(_enrollmentService.EnrolledTask, Task.Delay(Timeout.Infinite, stoppingToken))
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (!_gridClient.IsEnrolled)
            {
                _logger.LogWarning("GridHeartbeatService: enrollment did not complete, heartbeat disabled");
                return;
            }
        }

        var intervalSeconds = _config.Server.HeartbeatIntervalSeconds;
        _logger.LogInformation("GRID heartbeat started (interval={Interval}s)", intervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var uptimeSeconds = (int)(DateTime.UtcNow - _startedAt).TotalSeconds;
                var request = new HeartbeatRequest
                {
                    AgentUptimeSeconds = uptimeSeconds,
                    ThreatIntelVersion = null,
                    ModulesStatus = GetModulesStatus(),
                };

                var response = await _gridClient.SendHeartbeatAsync(request, stoppingToken)
                    .ConfigureAwait(false);

                LastPendingCommands = response.PendingCommands;
                intervalSeconds = response.NextHeartbeatInSeconds > 0
                    ? response.NextHeartbeatInSeconds
                    : _config.Server.HeartbeatIntervalSeconds;

                if (response.PendingCommands.Count > 0)
                {
                    _logger.LogInformation("Heartbeat OK — {Count} pending commands",
                        response.PendingCommands.Count);
                }
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(ex, "Heartbeat failed — will retry in {Interval}s", intervalSeconds);
            }
            catch (TaskCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected heartbeat error");
            }

            await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), stoppingToken)
                .ConfigureAwait(false);
        }
    }

    private Dictionary<string, string> GetModulesStatus()
    {
        var status = new Dictionary<string, string>
        {
            ["sentinel"] = _config.Sentinel?.Enabled == true ? "active" : "disabled",
            ["entropy"] = _config.Entropy?.Enabled == true ? "active" : "disabled",
            ["usb_guard"] = _config.UsbGuard?.Enabled == true ? "active" : "disabled",
            ["exfil_watch"] = _config.ExfilWatch?.Enabled == true ? "active" : "disabled",
        };
        return status;
    }
}
