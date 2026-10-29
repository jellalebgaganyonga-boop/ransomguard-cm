namespace RansomGuard.Agent.Core.Detection.Genealogy;

/// <summary>
/// Enriches alerts with process tree forensics and MITRE ATT and CK pattern analysis.
/// Called after an alert is persisted to add genealogy context.
/// </summary>
public interface IGenealogyEnricher
{
    /// <summary>
    /// Enriches an alert with process tree and suspicious pattern analysis.
    /// </summary>
    /// <param name="alertId">The alert to enrich.</param>
    /// <param name="offendingFilePath">The file that triggered the alert.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Summary of the genealogy, or null if attribution failed.</returns>
    Task<string?> EnrichAlertAsync(Guid alertId, string offendingFilePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Enriches from a writer the detection already attributed (ADR-025 for SENTINEL,
    /// AGT-ATTR-001 for ENTROPY), which is the reliable path: the ancestry was captured while the
    /// process was alive, so it works for the write-and-close that ransomware does.
    ///
    /// Falls back to <see cref="EnrichAlertAsync"/>'s Restart Manager lookup only when nothing was
    /// captured for that writer.
    /// </summary>
    /// <param name="alertId">The alert to enrich.</param>
    /// <param name="writerPid">The attributed writer's PID.</param>
    /// <param name="writerStartedAtUtc">Its creation time; a captured tree whose root started elsewhere is refused (PID reuse).</param>
    /// <param name="offendingFilePath">The file, for the fallback lookup.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>The chain summary, or null when no tree could be built.</returns>
    Task<string?> EnrichFromWriterAsync(
        Guid alertId,
        int writerPid,
        DateTime? writerStartedAtUtc,
        string offendingFilePath,
        CancellationToken cancellationToken = default);
}
