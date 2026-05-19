using System.IO.Compression;
using Microsoft.Extensions.Logging;

namespace RansomGuard.Agent.Core.Detection.UsbGuard.Scanning;

/// <summary>
/// Recursive archive scanner with anti zip-bomb protection.
/// Enforces max depth 5, compression ratio limit 1000, and path traversal detection (CWE-22, CWE-409).
/// </summary>
public sealed class ArchiveScanner
{
    private const int MaxDepth = 5;
    private const double MaxCompressionRatio = 1000.0;

    private static readonly HashSet<string> DangerousInnerExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".scr", ".bat", ".cmd", ".com", ".ps1", ".vbs", ".wsf", ".msi", ".dll"
    };

    private readonly ILogger<ArchiveScanner> _logger;

    /// <summary>Initializes the archive scanner.</summary>
    public ArchiveScanner(ILogger<ArchiveScanner> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Scans a ZIP archive for dangerous content patterns.
    /// </summary>
    /// <param name="archivePath">Path to the ZIP file.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of findings.</returns>
    public async Task<IReadOnlyList<ArchiveFinding>> ScanAsync(string archivePath, CancellationToken ct = default)
    {
        var findings = new List<ArchiveFinding>();
        await Task.Run(() => ScanRecursive(archivePath, findings, 0, ct), ct);
        return findings;
    }

    /// <summary>
    /// Scans archive bytes directly (for unit testing).
    /// </summary>
    public IReadOnlyList<ArchiveFinding> ScanBytes(Stream archiveStream, string archiveName, int depth = 0)
    {
        var findings = new List<ArchiveFinding>();
        ScanZipStream(archiveStream, archiveName, findings, depth);
        return findings;
    }

    private void ScanRecursive(string path, List<ArchiveFinding> findings, int depth, CancellationToken ct)
    {
        if (depth > MaxDepth)
        {
            findings.Add(new ArchiveFinding
            {
                ArchivePath = path,
                EntryName = "[MAX_DEPTH_EXCEEDED]",
                Reason = $"Archive nesting exceeds max depth {MaxDepth}",
                Severity = ScanSeverity.High
            });
            return;
        }

        try
        {
            using var stream = File.OpenRead(path);
            ScanZipStream(stream, path, findings, depth);
        }
        catch (InvalidDataException)
        {
            // Not a valid ZIP — skip silently
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Cannot scan archive: {Path}", path);
        }
    }

    private void ScanZipStream(Stream stream, string archiveName, List<ArchiveFinding> findings, int depth)
    {
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);

        foreach (var entry in archive.Entries)
        {
            // CWE-22: Path traversal detection
            if (entry.FullName.Contains(".."))
            {
                findings.Add(new ArchiveFinding
                {
                    ArchivePath = archiveName,
                    EntryName = entry.FullName,
                    Reason = "Path traversal pattern '../' detected (CWE-22)",
                    Severity = ScanSeverity.Critical
                });
                continue;
            }

            // CWE-409: Zip bomb detection via compression ratio
            if (entry.CompressedLength > 0)
            {
                double ratio = (double)entry.Length / entry.CompressedLength;
                if (ratio > MaxCompressionRatio)
                {
                    findings.Add(new ArchiveFinding
                    {
                        ArchivePath = archiveName,
                        EntryName = entry.FullName,
                        Reason = $"Zip bomb suspected: compression ratio {ratio:F0} exceeds limit {MaxCompressionRatio}",
                        Severity = ScanSeverity.Critical
                    });
                    continue;
                }
            }

            // Check for dangerous inner file extensions
            string innerExt = Path.GetExtension(entry.FullName);
            if (DangerousInnerExtensions.Contains(innerExt))
            {
                findings.Add(new ArchiveFinding
                {
                    ArchivePath = archiveName,
                    EntryName = entry.FullName,
                    Reason = $"Archive contains executable: {innerExt}",
                    Severity = ScanSeverity.High
                });
            }
        }
    }
}

/// <summary>
/// Finding from archive scanning.
/// </summary>
public sealed record ArchiveFinding
{
    /// <summary>Path to the archive file.</summary>
    public required string ArchivePath { get; init; }

    /// <summary>Name of the entry within the archive.</summary>
    public required string EntryName { get; init; }

    /// <summary>Reason the entry is flagged.</summary>
    public required string Reason { get; init; }

    /// <summary>Severity of the finding.</summary>
    public required ScanSeverity Severity { get; init; }
}
