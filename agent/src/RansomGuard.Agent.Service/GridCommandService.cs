using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using RansomGuard.Agent.Core.Communication;
using RansomGuard.Agent.Core.Communication.Models;
using RansomGuard.Agent.Core.Detection.Sentinel.Response;
using RansomGuard.Agent.Core.Persistence.Repositories;

namespace RansomGuard.Agent.Service;

/// <summary>
/// Executes the commands the console queued for this agent (plan de finition, J1 § 1.3), and
/// reports what happened: executed, or not, and why. Today two commands act, both through the
/// SENTINEL response engine (ADR-026): suspend_process and resume_process. Anything else is
/// answered "not executed" with a closed reason -- isolate included, until AGT-ISO-001.
/// </summary>
public sealed class GridCommandService : BackgroundService
{
    /// <summary>Between two looks at the heartbeat's pending list.</summary>
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    /// <summary>A fetch even when the heartbeat announced nothing (a heartbeat may have been missed).</summary>
    public static readonly TimeSpan FetchAnyway = TimeSpan.FromMinutes(1);

    /// <summary>Command ids answered in this run: a command fetched twice is executed once.</summary>
    private const int DoneCapacity = 1000;

    private readonly IGridApiClient _grid;
    private readonly GridHeartbeatService? _heartbeat;
    private readonly CanaryResponseEngine? _response;
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<GridCommandService> _logger;
    private readonly TimeProvider _time;
    private readonly HashSet<string> _done = [];

    /// <summary>Creates the service.</summary>
    /// <param name="grid">The GRID client.</param>
    /// <param name="scopes">For the audit.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="heartbeat">The heartbeat, whose pending list triggers a fetch; null: periodic fetch only.</param>
    /// <param name="response">The SENTINEL response engine; null: process commands are answered "no_response_engine".</param>
    /// <param name="time">The clock.</param>
    public GridCommandService(
        IGridApiClient grid,
        IServiceScopeFactory scopes,
        ILogger<GridCommandService> logger,
        GridHeartbeatService? heartbeat = null,
        CanaryResponseEngine? response = null,
        TimeProvider? time = null)
    {
        _grid = grid;
        _scopes = scopes;
        _logger = logger;
        _heartbeat = heartbeat;
        _response = response;
        _time = time ?? TimeProvider.System;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("GRID command service started");
        long lastFetch = 0;
        try
        {
            using var timer = new PeriodicTimer(PollInterval, _time);
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                bool announced = _heartbeat?.LastPendingCommands.Count > 0;
                bool overdue = lastFetch == 0 || _time.GetElapsedTime(lastFetch) >= FetchAnyway;
                if (!announced && !overdue)
                {
                    continue;
                }

                lastFetch = _time.GetTimestamp();
                await ProcessOnceAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
    }

    /// <summary>Fetches the open commands and answers each one not answered yet. Returns how many were answered.</summary>
    public async Task<int> ProcessOnceAsync(CancellationToken ct)
    {
        IReadOnlyList<AgentCommand> commands;
        try
        {
            commands = await _grid.FetchCommandsAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Not enrolled yet, or the GRID unreachable: the commands wait there.
            _logger.LogDebug(ex, "Commands not fetched");
            return 0;
        }

        int answered = 0;
        foreach (AgentCommand command in commands)
        {
            if (_done.Contains(command.Id))
            {
                continue;
            }

            CommandResultRequest result = await ExecuteAsync(command, ct).ConfigureAwait(false);
            try
            {
                await _grid.SendCommandResultAsync(command.Id, result, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The result could not be reported: the GRID will hand the command out again, and the
                // engine is idempotent (a suspended process is not suspended twice).
                _logger.LogWarning(ex, "Result of command {Id} not reported; it will be fetched again", command.Id);
                continue;
            }

            if (_done.Count >= DoneCapacity)
            {
                _done.Clear();
            }

            _done.Add(command.Id);
            answered++;
        }

        return answered;
    }

    private async Task<CommandResultRequest> ExecuteAsync(AgentCommand command, CancellationToken ct)
    {
        CommandResultRequest result;
        try
        {
            result = command.CommandType switch
            {
                "resume_process" => await ResumeAsync(command, ct).ConfigureAwait(false),
                "suspend_process" => await SuspendAsync(command, ct).ConfigureAwait(false),
                // Queued by the console since Sprint 7, never executed: said as such (AGT-ISO-001).
                "isolate" => CommandResultRequest.NotExecuted("not_implemented"),
                _ => CommandResultRequest.NotExecuted("unsupported_command"),
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Command {Id} ({Type}) failed", command.Id, command.CommandType);
            result = CommandResultRequest.NotExecuted("error", ex.GetType().Name);
        }

        _logger.LogWarning("Command {Id} ({Type}): {Outcome} {Reason}", command.Id, command.CommandType,
            result.Success ? "executed" : "not executed", result.Payload.GetValueOrDefault("reason"));
        try
        {
            await using AsyncServiceScope scope = _scopes.CreateAsyncScope();
            var audit = scope.ServiceProvider.GetRequiredService<IAuditLogRepository>();
            await audit.AppendAsync(
                result.Success ? "CommandExecuted" : "CommandNotExecuted",
                $"{command.CommandType} {command.Id}: {JsonSerializer.Serialize(result.Payload)}",
                "Command", null, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Command {Id}: audit entry not recorded", command.Id);
        }

        return result;
    }

    private async Task<CommandResultRequest> ResumeAsync(AgentCommand command, CancellationToken ct)
    {
        if (_response is null)
        {
            return CommandResultRequest.NotExecuted("no_response_engine");
        }

        if (!Guid.TryParse(command.Text("suspension_id"), out Guid suspensionId))
        {
            return CommandResultRequest.NotExecuted("invalid_parameters", "suspension_id");
        }

        bool resumed = await _response.ResumeAsync(suspensionId, $"command {command.Id}", ct).ConfigureAwait(false);
        return resumed
            ? CommandResultRequest.Executed(("state", "resumed"), ("suspension_id", suspensionId.ToString()))
            : CommandResultRequest.NotExecuted("resume_failed", suspensionId.ToString());
    }

    private async Task<CommandResultRequest> SuspendAsync(AgentCommand command, CancellationToken ct)
    {
        if (_response is null)
        {
            return CommandResultRequest.NotExecuted("no_response_engine");
        }

        string? clientMessageId = command.Text("alert_client_message_id");
        string? createdAtText = command.Text("process_created_at");
        if (command.Int("pid") is not int pid || clientMessageId is null || createdAtText is null
            || !DateTime.TryParse(createdAtText, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime createdAt))
        {
            return CommandResultRequest.NotExecuted("invalid_parameters", "pid, process_created_at, alert_client_message_id");
        }

        ResponseOutcome outcome = await _response.SuspendOnRequestAsync(pid, createdAt, clientMessageId, $"command {command.Id}", ct).ConfigureAwait(false);
        return outcome.Suspended
            ? CommandResultRequest.Executed(("state", "suspended"), ("suspension_id", outcome.SuspensionId?.ToString()))
            : CommandResultRequest.NotExecuted(outcome.Reason ?? "not_suspended");
    }
}
