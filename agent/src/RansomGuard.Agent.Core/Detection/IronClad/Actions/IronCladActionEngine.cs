using System.Diagnostics;
using Microsoft.Extensions.Options;
using RansomGuard.Agent.Core.Detection.IronClad.Communication;
using RansomGuard.Agent.Core.Detection.IronClad.Models;
using RansomGuard.Agent.Core.Persistence.Entities;
using RansomGuard.Agent.Core.Persistence.Repositories;
using Serilog;

namespace RansomGuard.Agent.Core.Detection.IronClad.Actions;

/// <summary>
/// Production implementation of <see cref="IIronCladActionEngine"/>.
/// Every command is audit-logged BEFORE execution (forensic preservation).
/// Outcomes are persisted to IronCladEvent and IronCladDeviceState tables.
/// </summary>
public sealed class IronCladActionEngine : IIronCladActionEngine
{
    private readonly IIronCladCommunicator _communicator;
    private readonly IronCladOptions _options;
    private readonly IAuditLogRepository _auditLog;
    private readonly IIronCladEventRepository _eventRepo;
    private readonly IIronCladDeviceStateRepository _stateRepo;
    private readonly ILogger _logger;

    /// <summary>Initializes the action engine with all dependencies.</summary>
    public IronCladActionEngine(
        IIronCladCommunicator communicator,
        IOptions<IronCladOptions> options,
        IAuditLogRepository auditLog,
        IIronCladEventRepository eventRepo,
        IIronCladDeviceStateRepository stateRepo,
        ILogger logger)
    {
        _communicator = communicator;
        _options = options.Value;
        _auditLog = auditLog;
        _eventRepo = eventRepo;
        _stateRepo = stateRepo;
        _logger = logger.ForContext<IronCladActionEngine>();
    }

    /// <inheritdoc />
    public bool IsAvailable => _options.Enabled && _communicator.IsConnected;

    /// <inheritdoc />
    public async Task<IronCladActionResult> CutUsbPortAsync(int portNumber, string justification, Guid? sourceAlertId, CancellationToken cancellationToken = default)
    {
        return await ExecuteActionAsync(
            IronCladAction.CutPort,
            portNumber.ToString(),
            justification,
            sourceAlertId,
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IronCladActionResult> RestoreUsbPortAsync(int portNumber, string justification, CancellationToken cancellationToken = default)
    {
        return await ExecuteActionAsync(
            IronCladAction.RestorePort,
            portNumber.ToString(),
            justification,
            null,
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IronCladActionResult> CutAllPortsAsync(string justification, CancellationToken cancellationToken = default)
    {
        return await ExecuteActionAsync(
            IronCladAction.CutAll,
            "",
            justification,
            null,
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IronCladActionResult> RestoreAllPortsAsync(string justification, CancellationToken cancellationToken = default)
    {
        return await ExecuteActionAsync(
            IronCladAction.RestoreAll,
            "",
            justification,
            null,
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<HealthStatus> GetDeviceStatusAsync(CancellationToken cancellationToken = default)
    {
        return await _communicator.CheckHealthAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<IronCladActionResult> ExecuteActionAsync(
        IronCladAction action,
        string parameter,
        string justification,
        Guid? sourceAlertId,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();

        // Check disabled
        if (!_options.Enabled)
        {
            return new IronCladActionResult
            {
                IsSuccess = false,
                Outcome = IronCladActionOutcome.Disabled,
                Message = "IronClad is disabled in configuration",
                ElapsedTime = sw.Elapsed
            };
        }

        // Check connected
        if (!_communicator.IsConnected)
        {
            return new IronCladActionResult
            {
                IsSuccess = false,
                Outcome = IronCladActionOutcome.DeviceUnavailable,
                Message = "IronClad device is not connected",
                ElapsedTime = sw.Elapsed
            };
        }

        // Validate port number for port-specific actions
        if (action is IronCladAction.CutPort or IronCladAction.RestorePort)
        {
            if (!int.TryParse(parameter, out var port) || port < 1 || port > _options.RelayCount)
            {
                return new IronCladActionResult
                {
                    IsSuccess = false,
                    Outcome = IronCladActionOutcome.InvalidPort,
                    Message = $"Port number must be between 1 and {_options.RelayCount}",
                    ElapsedTime = sw.Elapsed
                };
            }
        }

        var commandId = Guid.NewGuid();
        var eventId = Guid.NewGuid();

        // Audit log BEFORE sending command (forensic preservation)
        try
        {
            await _auditLog.AppendAsync(
                $"IronClad:{action}",
                $"Port={parameter} Justification={justification}",
                "IronCladEvent",
                eventId,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to write audit log for IronClad action {Action}", action);
            return new IronCladActionResult
            {
                IsSuccess = false,
                Outcome = IronCladActionOutcome.AuditLogFailure,
                Message = "Audit log write failed — action aborted for forensic integrity",
                ElapsedTime = sw.Elapsed
            };
        }

        // Build and send command
        var command = new IronCladCommand
        {
            Id = commandId,
            Action = action,
            Parameter = parameter,
            IssuedAt = DateTime.UtcNow,
            Timeout = TimeSpan.FromSeconds(_options.CommandTimeoutSeconds)
        };

        IronCladActionOutcome outcome;
        string message;
        string? responsePayload = null;
        string? errorMessage = null;

        try
        {
            var response = await _communicator.SendCommandAsync(command, cancellationToken).ConfigureAwait(false);
            responsePayload = response.RawPayload;

            if (response.IsAcknowledgement)
            {
                outcome = IronCladActionOutcome.Success;
                message = $"IronClad {action} executed: {response.RawPayload}";

                // Update device state
                await UpdateDeviceStateAsync(action, parameter, eventId, cancellationToken).ConfigureAwait(false);
            }
            else if (response.ErrorCode == "TIMEOUT")
            {
                outcome = IronCladActionOutcome.Timeout;
                message = response.ErrorMessage ?? "Command timed out";
            }
            else if (response.ErrorCode == "E002")
            {
                outcome = IronCladActionOutcome.AlreadyInState;
                message = response.ErrorMessage ?? "Port already in requested state";
            }
            else
            {
                outcome = IronCladActionOutcome.DeviceError;
                message = $"{response.ErrorCode}: {response.ErrorMessage}";
                errorMessage = response.ErrorMessage;
            }
        }
        catch (OperationCanceledException)
        {
            outcome = IronCladActionOutcome.Timeout;
            message = "Command cancelled or timed out";
        }
        catch (Exception ex)
        {
            outcome = IronCladActionOutcome.DeviceError;
            message = ex.Message;
            errorMessage = ex.Message;
            _logger.Error(ex, "IronClad action {Action} failed", action);
        }

        sw.Stop();

        // Persist event (always, even on failure)
        try
        {
            await _eventRepo.AppendAsync(new IronCladEvent
            {
                Id = eventId,
                CommandId = commandId,
                Action = action.ToString(),
                Parameter = parameter,
                Justification = justification,
                SourceAlertId = sourceAlertId,
                IssuedAt = command.IssuedAt,
                CompletedAt = DateTime.UtcNow,
                Outcome = outcome.ToString(),
                ResponsePayload = responsePayload,
                ErrorMessage = errorMessage,
                IssuedByUser = "SYSTEM"
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to persist IronClad event");
        }

        if (outcome == IronCladActionOutcome.Success)
        {
            _logger.Information("IronClad {Action} port={Parameter} succeeded in {Elapsed}ms",
                action, parameter, sw.ElapsedMilliseconds);
        }

        return new IronCladActionResult
        {
            IsSuccess = outcome == IronCladActionOutcome.Success,
            Outcome = outcome,
            Message = message,
            AuditLogEntryId = eventId,
            ElapsedTime = sw.Elapsed
        };
    }

    private async Task UpdateDeviceStateAsync(IronCladAction action, string parameter, Guid eventId, CancellationToken ct)
    {
        var now = DateTime.UtcNow;

        if (action == IronCladAction.CutPort && int.TryParse(parameter, out var cutPort))
        {
            await _stateRepo.UpdateStateAsync(new IronCladDeviceState
            {
                Id = Guid.NewGuid(),
                PortNumber = cutPort,
                State = "Cut",
                LastChangedAt = now,
                LastEventId = eventId,
                Reason = "CutPort command"
            }, ct).ConfigureAwait(false);
        }
        else if (action == IronCladAction.RestorePort && int.TryParse(parameter, out var restorePort))
        {
            await _stateRepo.UpdateStateAsync(new IronCladDeviceState
            {
                Id = Guid.NewGuid(),
                PortNumber = restorePort,
                State = "Active",
                LastChangedAt = now,
                LastEventId = eventId,
                Reason = "RestorePort command"
            }, ct).ConfigureAwait(false);
        }
        else if (action == IronCladAction.CutAll)
        {
            for (int i = 1; i <= _options.RelayCount; i++)
            {
                await _stateRepo.UpdateStateAsync(new IronCladDeviceState
                {
                    Id = Guid.NewGuid(),
                    PortNumber = i,
                    State = "Cut",
                    LastChangedAt = now,
                    LastEventId = eventId,
                    Reason = "CutAll command"
                }, ct).ConfigureAwait(false);
            }
        }
        else if (action == IronCladAction.RestoreAll)
        {
            for (int i = 1; i <= _options.RelayCount; i++)
            {
                await _stateRepo.UpdateStateAsync(new IronCladDeviceState
                {
                    Id = Guid.NewGuid(),
                    PortNumber = i,
                    State = "Active",
                    LastChangedAt = now,
                    LastEventId = eventId,
                    Reason = "RestoreAll command"
                }, ct).ConfigureAwait(false);
            }
        }
    }
}
