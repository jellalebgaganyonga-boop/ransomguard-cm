using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using RansomGuard.Agent.Core.Persistence.Entities;
using RansomGuard.Agent.Core.Persistence.Repositories;

namespace RansomGuard.Agent.Core.Detection.Sentinel;

/// <summary>
/// Manages SENTINEL canary files on disk: creation, integrity verification, and deletion.
/// Each canary is written with unique synthetic medical content and tracked in the database.
/// </summary>
public sealed class CanaryFileService : ICanaryFileService
{
    private readonly ISentinelCanaryRepository _repository;
    private readonly ILogger<CanaryFileService> _logger;

    /// <summary>
    /// Initializes the canary file service.
    /// </summary>
    public CanaryFileService(ISentinelCanaryRepository repository, ILogger<CanaryFileService> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<SentinelCanary> CreateCanaryAsync(string directory, string template, string prefix, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
            _logger.LogInformation("Created canary directory: {Directory}", directory);
        }

        string shortId = Guid.NewGuid().ToString("N")[..8];
        string fileName = $"{prefix}{template}_{shortId}.txt";
        string filePath = Path.Combine(directory, fileName);

        (byte[] content, _) = CanaryContentGenerator.Generate(template);
        string contentHash = CanaryContentGenerator.ComputeHash(content);

        await File.WriteAllBytesAsync(filePath, content, cancellationToken);

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
            "SENTINEL canary deployed: {FileName} in {Directory} (template: {Template}, size: {Size} bytes)",
            fileName, directory, template, content.Length);

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
