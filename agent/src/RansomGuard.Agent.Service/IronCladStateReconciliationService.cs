using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RansomGuard.Agent.Core.Detection.IronClad;
using RansomGuard.Agent.Core.Detection.IronClad.Actions;
using RansomGuard.Agent.Core.Detection.IronClad.Communication;
using RansomGuard.Agent.Core.Detection.IronClad.Models;
using RansomGuard.Agent.Core.Persistence.Entities;
using RansomGuard.Agent.Core.Persistence.Repositories;

namespace RansomGuard.Agent.Service;

/// <summary>
/// Runs once on startup to reconcile stored device state with actual device state.
/// If FailSafeRestoreOnDisconnect is true and agent had crashed mid-cut,
/// restores ports that should not be cut.
/// <para>
/// Registered as a hosted service, i.e. a singleton: the scoped repository and action engine
/// (both on the DbContext) are resolved in a scope per reconciliation, never captured.
/// </para>
/// </summary>
public sealed class IronCladStateReconciliationService : IHostedService
{
    private readonly IIronCladCommunicator _communicator;
    private readonly IronCladOptions _options;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<IronCladStateReconciliationService> _logger;

    /// <summary>Initializes the reconciliation service.</summary>
    public IronCladStateReconciliationService(
        IIronCladCommunicator communicator,
        IOptions<IronCladOptions> options,
        IServiceScopeFactory scopeFactory,
        ILogger<IronCladStateReconciliationService> logger)
    {
        _communicator = communicator;
        _options = options.Value;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("IronClad disabled, skipping state reconciliation");
            return;
        }

        // Brief delay to allow communicator to establish connection
        await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);

        if (!_communicator.IsConnected)
        {
            _logger.LogWarning("IronClad not connected, skipping reconciliation");
            return;
        }

        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            await ReconcileAsync(
                scope.ServiceProvider.GetRequiredService<IIronCladDeviceStateRepository>(),
                scope.ServiceProvider.GetRequiredService<IIronCladActionEngine>(),
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "IronClad state reconciliation failed");
        }
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task ReconcileAsync(
        IIronCladDeviceStateRepository stateRepo, IIronCladActionEngine actionEngine, CancellationToken ct)
    {
        var storedStates = await stateRepo.GetCurrentStatesAsync(ct).ConfigureAwait(false);
        var health = await _communicator.CheckHealthAsync(ct).ConfigureAwait(false);

        if (health.PortStates.Count == 0)
        {
            _logger.LogInformation("No device port states available for reconciliation");
            return;
        }

        var discrepancies = 0;

        foreach (var (portNumber, actualState) in health.PortStates)
        {
            var stored = storedStates.FirstOrDefault(s => s.PortNumber == portNumber);
            var storedState = stored?.State ?? "Unknown";

            var actualStateStr = actualState.ToString();
            if (storedState != actualStateStr)
            {
                discrepancies++;
                _logger.LogWarning("IronClad state discrepancy on port {Port}: stored={Stored} actual={Actual}",
                    portNumber, storedState, actualStateStr);

                // Update stored state to match actual device
                await stateRepo.UpdateStateAsync(new IronCladDeviceState
                {
                    Id = Guid.NewGuid(),
                    PortNumber = portNumber,
                    State = actualStateStr,
                    LastChangedAt = DateTime.UtcNow,
                    Reason = "State reconciliation on startup"
                }, ct).ConfigureAwait(false);
            }
        }

        // FailSafe: if agent crashed while ports were cut, restore them
        if (_options.FailSafeRestoreOnDisconnect && discrepancies > 0)
        {
            var cutPorts = health.PortStates.Where(p => p.Value == PortState.Cut).Select(p => p.Key).ToList();
            if (cutPorts.Count > 0)
            {
                _logger.LogWarning("FailSafe: restoring {Count} cut ports after agent restart: {Ports}",
                    cutPorts.Count, string.Join(", ", cutPorts));

                await actionEngine.RestoreAllPortsAsync("FailSafe restore after agent restart", ct)
                    .ConfigureAwait(false);
            }
        }

        if (discrepancies == 0)
        {
            _logger.LogInformation("IronClad state reconciliation complete — no discrepancies");
        }
        else
        {
            _logger.LogInformation("IronClad state reconciliation complete — {Count} discrepancies resolved", discrepancies);
        }
    }
}
