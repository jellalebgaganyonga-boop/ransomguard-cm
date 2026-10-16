using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace RansomGuard.Agent.Service;

/// <summary>
/// Background service polling for pending commands from the GRID server.
/// Reads command IDs from GridHeartbeatService.LastPendingCommands and
/// executes them locally.
///
/// Sprint 8: Only supports "isolate_endpoint" command type.
/// Sprint 9: Will add command fetch endpoint + full command execution pipeline.
/// </summary>
public sealed class GridCommandService : BackgroundService
{
    private readonly GridHeartbeatService _heartbeatService;
    private readonly ILogger<GridCommandService> _logger;
    private readonly HashSet<string> _processedCommandIds = [];

    public GridCommandService(
        GridHeartbeatService heartbeatService,
        ILogger<GridCommandService> logger)
    {
        _heartbeatService = heartbeatService;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("GRID command service started — polling for pending commands");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var pendingIds = _heartbeatService.LastPendingCommands;
                foreach (var commandId in pendingIds)
                {
                    if (_processedCommandIds.Contains(commandId))
                        continue;

                    _logger.LogWarning(
                        "Pending command {CommandId} received — command fetch/execution deferred to Sprint 9",
                        commandId);

                    _processedCommandIds.Add(commandId);
                }

                // Cap the processed set to prevent unbounded growth
                if (_processedCommandIds.Count > 1000)
                {
                    _processedCommandIds.Clear();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Command processing error");
            }

            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken).ConfigureAwait(false);
        }
    }
}
