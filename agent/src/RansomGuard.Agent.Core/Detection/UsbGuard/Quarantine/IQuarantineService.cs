using RansomGuard.Agent.Core.Persistence.Entities;

namespace RansomGuard.Agent.Core.Detection.UsbGuard.Quarantine;

/// <summary>
/// Secure file quarantine with AES-256-GCM encryption and DACL-hardened storage.
/// Files are moved (not copied), encrypted, and tracked with full audit trail.
/// </summary>
public interface IQuarantineService
{
    /// <summary>
    /// Quarantines a suspicious file: move, encrypt, persist metadata, audit log.
    /// </summary>
    /// <param name="filePath">Path to the file to quarantine.</param>
    /// <param name="reason">Reason for quarantine.</param>
    /// <param name="severity">Severity of the finding.</param>
    /// <param name="sourceUsbSerial">Hashed serial of the source USB device.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The quarantine record.</returns>
    Task<QuarantinedFile> QuarantineAsync(string filePath, string reason, QuarantineSeverity severity,
        string sourceUsbSerial, CancellationToken ct = default);

    /// <summary>
    /// Restores a quarantined file to its original location.
    /// Requires admin elevation. Verifies hash integrity.
    /// </summary>
    /// <param name="id">Quarantine record ID.</param>
    /// <param name="justification">Justification for restore.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if restored successfully.</returns>
    Task<bool> RestoreAsync(Guid id, string justification, CancellationToken ct = default);

    /// <summary>
    /// Removes expired quarantine entries (retention period exceeded).
    /// </summary>
    Task<int> CleanupExpiredAsync(CancellationToken ct = default);

    /// <summary>
    /// Lists all active (non-restored, non-expired) quarantine entries.
    /// </summary>
    Task<IReadOnlyList<QuarantinedFile>> ListActiveAsync(CancellationToken ct = default);
}
