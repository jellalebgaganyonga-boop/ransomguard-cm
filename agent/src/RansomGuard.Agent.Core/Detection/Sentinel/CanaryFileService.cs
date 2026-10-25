using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using RansomGuard.Agent.Core.Configuration;
using RansomGuard.Agent.Core.Detection.Sentinel.Generators;
using RansomGuard.Agent.Core.Persistence.Entities;
using RansomGuard.Agent.Core.Persistence.Repositories;

namespace RansomGuard.Agent.Core.Detection.Sentinel;

/// <summary>
/// Manages SENTINEL canary files on disk: creation, integrity verification, and deletion.
/// Supports multiple file formats (.txt, .docx) via generator factory.
/// Each canary is written with unique synthetic medical content and tracked in the database.
/// </summary>
public sealed class CanaryFileService : ICanaryFileService
{
    private readonly ISentinelCanaryRepository _repository;
    private readonly ILogger<CanaryFileService> _logger;
    private readonly AgentOwnWrites? _ownWrites;

    /// <summary>
    /// Initializes the canary file service.
    /// </summary>
    /// <param name="repository">Where the canaries are recorded.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="ownWrites">
    /// The paths the agent writes itself. This service is the only thing that writes a canary, so
    /// it is the thing that marks one: the mark has to exist before the bytes reach the disk, or
    /// the FileSystemWatcher reports the agent's own write as an attack on the decoy. Null leaves
    /// writes unmarked (tests that do not exercise the watcher).
    /// </param>
    public CanaryFileService(
        ISentinelCanaryRepository repository,
        ILogger<CanaryFileService> logger,
        AgentOwnWrites? ownWrites = null)
    {
        _repository = repository;
        _logger = logger;
        _ownWrites = ownWrites;
    }

    /// <inheritdoc />
    public async Task<SentinelCanary> CreateCanaryAsync(string directory, string template, string prefix, CancellationToken cancellationToken = default)
    {
        return await CreateCanaryAsync(directory, template, prefix, ".txt", cancellationToken);
    }

    /// <summary>
    /// Creates a canary file in the specified format.
    /// </summary>
    /// <param name="directory">Target directory.</param>
    /// <param name="template">Content template name.</param>
    /// <param name="prefix">Filename prefix.</param>
    /// <param name="extension">File extension (e.g., ".docx", ".txt").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The persisted canary entity.</returns>
    public async Task<SentinelCanary> CreateCanaryAsync(string directory, string template, string prefix, string extension, CancellationToken cancellationToken = default)
    {
        // AGT-PATH-001: the last gate, for every caller (deployment, regeneration).
        if (WatchedPathPolicy.IsRefused(directory))
        {
            throw new InvalidOperationException(
                $"No canary in a system or service profile (AGT-PATH-001): {directory}");
        }

        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
            _logger.LogInformation("Created canary directory: {Directory}", directory);
        }

        ICanaryFileGenerator generator = CanaryFileGeneratorFactory.GetGenerator(extension);
        byte[] content = generator.Generate(template);
        string contentHash = CanaryContentGenerator.ComputeHash(content);

        string shortId = Guid.NewGuid().ToString("N")[..8];
        string fileName = $"{prefix}{template}_{shortId}{generator.Extension}";
        string filePath = Path.Combine(directory, fileName);

        // Before the write, never after. The deployment service and the monitor are both hosted
        // services and start together, so by the time the first canary is written the watcher is
        // usually already watching: a mark set after WriteAllBytesAsync returns can arrive after
        // the watcher has already turned that write into a Critical "canary modified". A fresh
        // install reported fifteen of those on the bench (PC-LABO, 2026-10-25), one per canary,
        // each within a second of its file being created, with no process named -- ETW filters
        // the agent's own PID out, so nothing could be attributed either.
        _ownWrites?.Mark(filePath, AgentOwnWrites.DeploymentGrace);

        try
        {
            await File.WriteAllBytesAsync(filePath, content, cancellationToken);
        }
        catch
        {
            // Nothing was written, so nothing is coming: the path must not stay excused.
            _ownWrites?.Clear(filePath);
            throw;
        }

        // Hide canaries from normal user view (CWE-732 mitigation).
        // Ransomware still enumerates them via FindFirstFile default flags.
        File.SetAttributes(filePath, FileAttributes.Hidden | FileAttributes.System);

        var canary = new SentinelCanary
        {
            FilePath = filePath,
            FileName = fileName,
            Directory = directory,
            TemplateUsed = template,
            OriginalContentHash = contentHash,
            FileSize = content.Length
        };

        await _repository.AddAsync(canary, cancellationToken);

        _logger.LogInformation(
            "SENTINEL canary deployed: {FileName} in {Directory} (template: {Template}, format: {Format}, size: {Size} bytes)",
            fileName, directory, template, generator.Extension, content.Length);

        return canary;
    }

    /// <inheritdoc />
    public async Task<bool> VerifyCanaryIntegrityAsync(SentinelCanary canary, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(canary.FilePath))
        {
            return false;
        }

        byte[] currentContent = await File.ReadAllBytesAsync(canary.FilePath, cancellationToken);
        string currentHash = Convert.ToHexString(SHA256.HashData(currentContent)).ToLowerInvariant();

        return currentHash == canary.OriginalContentHash;
    }

    /// <inheritdoc />
    public async Task RestoreCanaryAsync(SentinelCanary canary, CancellationToken cancellationToken = default)
    {
        ICanaryFileGenerator generator = CanaryFileGeneratorFactory.GetGenerator(Path.GetExtension(canary.FilePath));
        byte[] content = generator.Generate(canary.TemplateUsed);

        // Marked before the rewrite, for the same reason as a new canary: restoring a decoy is the
        // agent's own write and must not come back as a second alert on the file it just repaired.
        _ownWrites?.Mark(canary.FilePath, AgentOwnWrites.RestoreGrace);

        if (File.Exists(canary.FilePath))
        {
            // Hidden | System (and read-only, if the attacker set it) would refuse the rewrite.
            File.SetAttributes(canary.FilePath, FileAttributes.Normal);
        }

        await File.WriteAllBytesAsync(canary.FilePath, content, cancellationToken);
        File.SetAttributes(canary.FilePath, FileAttributes.Hidden | FileAttributes.System);

        canary.OriginalContentHash = CanaryContentGenerator.ComputeHash(content);
        canary.FileSize = content.Length;
        canary.Status = CanaryStatus.Active;
        canary.LastCheckedAt = DateTime.UtcNow;
        canary.UpdatedAt = DateTime.UtcNow;
        await _repository.UpdateAsync(canary, cancellationToken);

        _logger.LogInformation("SENTINEL canary restored: {FilePath} (template: {Template}, {Size} bytes), still watched",
            canary.FilePath, canary.TemplateUsed, content.Length);
    }

    /// <inheritdoc />
    public async Task DeleteCanaryAsync(SentinelCanary canary, CancellationToken cancellationToken = default)
    {
        if (File.Exists(canary.FilePath))
        {
            File.Delete(canary.FilePath);
        }

        canary.Status = CanaryStatus.Deleted;
        await _repository.UpdateAsync(canary, cancellationToken);

        _logger.LogInformation("SENTINEL canary deleted: {FilePath}", canary.FilePath);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SentinelCanary>> GetAllCanariesAsync(CancellationToken cancellationToken = default)
    {
        return await _repository.GetActiveAsync(cancellationToken);
    }
}
