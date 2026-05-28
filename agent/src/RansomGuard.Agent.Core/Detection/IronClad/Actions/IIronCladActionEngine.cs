using RansomGuard.Agent.Core.Detection.IronClad.Models;

namespace RansomGuard.Agent.Core.Detection.IronClad.Actions;

/// <summary>
/// Orchestrates IronClad hardware actions with audit logging and persistence.
/// </summary>
public interface IIronCladActionEngine
{
    /// <summary>Whether the IronClad device is enabled and connected.</summary>
    bool IsAvailable { get; }

    /// <summary>Cuts power to a specific USB port relay.</summary>
    Task<IronCladActionResult> CutUsbPortAsync(int portNumber, string justification, Guid? sourceAlertId, CancellationToken cancellationToken = default);

    /// <summary>Restores power to a specific USB port relay.</summary>
    Task<IronCladActionResult> RestoreUsbPortAsync(int portNumber, string justification, CancellationToken cancellationToken = default);

    /// <summary>Cuts power to all USB port relays.</summary>
    Task<IronCladActionResult> CutAllPortsAsync(string justification, CancellationToken cancellationToken = default);

    /// <summary>Restores power to all USB port relays.</summary>
    Task<IronCladActionResult> RestoreAllPortsAsync(string justification, CancellationToken cancellationToken = default);

    /// <summary>Gets the current device health status.</summary>
    Task<HealthStatus> GetDeviceStatusAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of an IronClad action execution.
/// </summary>
public sealed record IronCladActionResult
{
    /// <summary>Whether the action completed successfully.</summary>
    public required bool IsSuccess { get; init; }

    /// <summary>Outcome classification.</summary>
    public required IronCladActionOutcome Outcome { get; init; }

    /// <summary>Human-readable result message.</summary>
    public required string Message { get; init; }

    /// <summary>Audit log entry ID for this action.</summary>
    public Guid? AuditLogEntryId { get; init; }

    /// <summary>Time elapsed for the action.</summary>
    public TimeSpan ElapsedTime { get; init; }
}

/// <summary>
/// Outcome classifications for IronClad actions.
/// </summary>
public enum IronCladActionOutcome
{
    /// <summary>Action completed successfully.</summary>
    Success,

    /// <summary>Device is not connected.</summary>
    DeviceUnavailable,

    /// <summary>Command timed out waiting for device response.</summary>
    Timeout,

    /// <summary>Device reported an error.</summary>
    DeviceError,

    /// <summary>Port number is out of valid range.</summary>
    InvalidPort,

    /// <summary>Port is already in the requested state.</summary>
    AlreadyInState,

    /// <summary>IronClad module is disabled in configuration.</summary>
    Disabled,

    /// <summary>Audit log write failed.</summary>
    AuditLogFailure
}
