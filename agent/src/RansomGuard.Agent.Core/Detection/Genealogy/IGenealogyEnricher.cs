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
}
