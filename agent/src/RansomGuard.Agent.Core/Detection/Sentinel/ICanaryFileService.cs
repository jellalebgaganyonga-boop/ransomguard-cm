using RansomGuard.Agent.Core.Persistence.Entities;

namespace RansomGuard.Agent.Core.Detection.Sentinel;

/// <summary>
/// Service for creating, verifying, and managing SENTINEL canary files on disk.
/// </summary>
public interface ICanaryFileService
{
    /// <summary>
    /// Creates a new canary file in the specified directory using the given template.
    /// </summary>
    /// <param name="directory">Target directory for the canary file.</param>
    /// <param name="template">Template name for content generation.</param>
    /// <param name="prefix">Filename prefix for alphabetical prominence.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The persisted canary entity.</returns>
    Task<SentinelCanary> CreateCanaryAsync(string directory, string template, string prefix, CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifies the integrity of a canary file by comparing its current hash with the stored hash.
    /// </summary>
    /// <param name="canary">The canary to verify.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the file is intact; false if modified or missing.</returns>
    Task<bool> VerifyCanaryIntegrityAsync(SentinelCanary canary, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a canary file from disk and marks it as deleted in the database.
    /// </summary>
    Task DeleteCanaryAsync(SentinelCanary canary, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves all active canaries from the database.
    /// </summary>
    Task<IReadOnlyList<SentinelCanary>> GetAllCanariesAsync(CancellationToken cancellationToken = default);
}
