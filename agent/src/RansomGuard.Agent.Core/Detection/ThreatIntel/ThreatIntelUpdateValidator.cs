using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using NSec.Cryptography;

namespace RansomGuard.Agent.Core.Detection.ThreatIntel;

/// <summary>
/// Validates and applies signed threat intel update packages.
/// ZIP format: manifest.json + lists/ folder + signature.bin.
/// Ed25519 signature verification with version downgrade protection.
/// </summary>
public sealed class ThreatIntelUpdateValidator
{
    private readonly ILogger<ThreatIntelUpdateValidator> _logger;
    private readonly string _updateDirectory;
    private readonly string _dataDirectory;
    private readonly string _backupDirectory;
    private static readonly Mutex UpdateMutex = new(false, "Global\\RansomGuard-ThreatIntelUpdate");

    /// <summary>Initializes the update validator.</summary>
    public ThreatIntelUpdateValidator(
        ILogger<ThreatIntelUpdateValidator> logger,
        string updateDirectory,
        string dataDirectory)
    {
        _logger = logger;
        _updateDirectory = updateDirectory;
        _dataDirectory = dataDirectory;
        _backupDirectory = Path.Combine(dataDirectory, "backup");
    }

    /// <summary>
    /// Checks for available update packages in the update directory.
    /// Returns the path to the newest valid package, or null.
    /// </summary>
    public string? CheckForUpdates()
    {
        if (!Directory.Exists(_updateDirectory))
            return null;

        var packages = Directory.GetFiles(_updateDirectory, "threat-intel-v*.pkg")
            .OrderByDescending(f => f)
            .ToList();

        return packages.FirstOrDefault();
    }

    /// <summary>
    /// Validates an update package. Returns validation result.
    /// </summary>
    public UpdateValidationResult Validate(string packagePath, string currentVersion, PublicKey publicKey)
    {
        if (!File.Exists(packagePath))
            return UpdateValidationResult.Fail("Package file not found");

        try
        {
            using var zipStream = File.OpenRead(packagePath);
            using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);

            // Check manifest.json exists
            var manifestEntry = archive.GetEntry("manifest.json");
            if (manifestEntry is null)
                return UpdateValidationResult.Fail("Missing manifest.json in package");

            // Parse manifest
            ThreatIntelManifest manifest;
            using (var manifestStream = manifestEntry.Open())
            {
                var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                manifest = JsonSerializer.Deserialize<ThreatIntelManifest>(manifestStream, jsonOptions)
                    ?? throw new JsonException("Failed to deserialize manifest");
            }

            // Version downgrade check
            if (string.Compare(manifest.Version, currentVersion, StringComparison.OrdinalIgnoreCase) <= 0)
                return UpdateValidationResult.Fail(
                    $"Version downgrade rejected: package {manifest.Version} <= current {currentVersion}");

            // Check signature.bin exists
            var signatureEntry = archive.GetEntry("signature.bin");
            if (signatureEntry is null)
                return UpdateValidationResult.Fail("Missing signature.bin in package (unsigned)");

            // Read signature
            byte[] signature;
            using (var sigStream = signatureEntry.Open())
            using (var ms = new MemoryStream())
            {
                sigStream.CopyTo(ms);
                signature = ms.ToArray();
            }

            // Compute SHA-256 of manifest + list files (deterministic)
            byte[] contentHash = ComputeContentHash(archive);

            // Verify Ed25519 signature
            var algorithm = SignatureAlgorithm.Ed25519;
            if (!algorithm.Verify(publicKey, contentHash, signature))
                return UpdateValidationResult.Fail("Ed25519 signature verification FAILED — package tampered");

            // Validate list files exist and have valid schema
            var requiredFiles = new[] { "lists/tor-exit-nodes.json", "lists/cloud-providers.json",
                                        "lists/known-c2-servers.json", "lists/lolbas-binaries.json" };
            foreach (var file in requiredFiles)
            {
                var entry = archive.GetEntry(file);
                if (entry is null)
                    return UpdateValidationResult.Fail($"Missing required file: {file}");

                if (!ValidateListSchema(archive, file))
                    return UpdateValidationResult.Fail($"Invalid schema in: {file}");
            }

            return UpdateValidationResult.Success(manifest.Version, packagePath);
        }
        catch (InvalidDataException)
        {
            return UpdateValidationResult.Fail("Invalid ZIP structure");
        }
        catch (Exception ex)
        {
            return UpdateValidationResult.Fail($"Validation error: {ex.Message}");
        }
    }

    /// <summary>
    /// Applies a validated update package. Creates backup, extracts lists, updates provider.
    /// Thread-safe via global mutex.
    /// </summary>
    public bool Apply(string packagePath, ThreatIntelDataLoader provider)
    {
        if (!UpdateMutex.WaitOne(TimeSpan.FromSeconds(5)))
        {
            _logger.LogWarning("Another threat intel update is in progress");
            return false;
        }

        try
        {
            // Backup current data
            BackupCurrentData();

            try
            {
                // Extract list files
                using var zipStream = File.OpenRead(packagePath);
                using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);

                if (!Directory.Exists(_dataDirectory))
                    Directory.CreateDirectory(_dataDirectory);

                foreach (var entry in archive.Entries)
                {
                    if (!entry.FullName.StartsWith("lists/", StringComparison.OrdinalIgnoreCase))
                        continue;

                    string fileName = Path.GetFileName(entry.FullName);
                    if (string.IsNullOrEmpty(fileName)) continue;

                    string destPath = Path.Combine(_dataDirectory, fileName);
                    entry.ExtractToFile(destPath, overwrite: true);
                }

                // Reload provider
                provider.ReloadFromDirectory(_dataDirectory);

                _logger.LogInformation("Threat intel update applied from {Package}", packagePath);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Update failed, rolling back");
                Rollback();
                return false;
            }
        }
        finally
        {
            UpdateMutex.ReleaseMutex();
        }
    }

    /// <summary>Restores data from backup after a failed update.</summary>
    public void Rollback()
    {
        if (!Directory.Exists(_backupDirectory))
        {
            _logger.LogWarning("No backup found for rollback");
            return;
        }

        foreach (var file in Directory.GetFiles(_backupDirectory, "*.json"))
        {
            string destPath = Path.Combine(_dataDirectory, Path.GetFileName(file));
            File.Copy(file, destPath, overwrite: true);
        }

        _logger.LogWarning("Threat intel rollback completed from backup");
    }

    private void BackupCurrentData()
    {
        if (!Directory.Exists(_dataDirectory))
            return;

        if (!Directory.Exists(_backupDirectory))
            Directory.CreateDirectory(_backupDirectory);

        foreach (var file in Directory.GetFiles(_dataDirectory, "*.json"))
        {
            string destPath = Path.Combine(_backupDirectory, Path.GetFileName(file));
            File.Copy(file, destPath, overwrite: true);
        }
    }

    private static byte[] ComputeContentHash(ZipArchive archive)
    {
        using var sha = SHA256.Create();
        var entries = archive.Entries
            .Where(e => e.FullName != "signature.bin")
            .OrderBy(e => e.FullName, StringComparer.Ordinal);

        using var ms = new MemoryStream();
        foreach (var entry in entries)
        {
            // Include filename in hash
            byte[] nameBytes = Encoding.UTF8.GetBytes(entry.FullName);
            ms.Write(nameBytes, 0, nameBytes.Length);

            using var entryStream = entry.Open();
            entryStream.CopyTo(ms);
        }

        ms.Position = 0;
        return sha.ComputeHash(ms);
    }

    private static bool ValidateListSchema(ZipArchive archive, string entryName)
    {
        var entry = archive.GetEntry(entryName);
        if (entry is null) return false;

        try
        {
            using var stream = entry.Open();
            var doc = JsonDocument.Parse(stream);
            var root = doc.RootElement;

            // All list files must have "version" and either "entries" or "providers"
            if (!root.TryGetProperty("version", out _))
                return false;

            bool hasEntries = root.TryGetProperty("entries", out var entries) &&
                              entries.ValueKind == JsonValueKind.Array;
            bool hasProviders = root.TryGetProperty("providers", out _);

            return hasEntries || hasProviders;
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>
/// Result of update package validation.
/// </summary>
public sealed record UpdateValidationResult
{
    /// <summary>Whether validation passed.</summary>
    public required bool IsValid { get; init; }

    /// <summary>Error message if validation failed.</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>Version in the package.</summary>
    public string? PackageVersion { get; init; }

    /// <summary>Path to the validated package.</summary>
    public string? PackagePath { get; init; }

    /// <summary>Creates a failed result.</summary>
    public static UpdateValidationResult Fail(string error) => new()
    {
        IsValid = false, ErrorMessage = error
    };

    /// <summary>Creates a successful result.</summary>
    public static UpdateValidationResult Success(string version, string path) => new()
    {
        IsValid = true, PackageVersion = version, PackagePath = path
    };
}

/// <summary>
/// Manifest inside a signed threat intel update package.
/// </summary>
public sealed record ThreatIntelManifest
{
    /// <summary>Package version (semver).</summary>
    public required string Version { get; init; }

    /// <summary>ISO 8601 timestamp of when package was built.</summary>
    public string? BuildDate { get; init; }

    /// <summary>Description of changes.</summary>
    public string? Description { get; init; }
}
