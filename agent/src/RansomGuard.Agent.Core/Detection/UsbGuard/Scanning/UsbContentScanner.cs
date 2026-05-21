using System.Diagnostics;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using RansomGuard.Agent.Core.Detection.UsbGuard.Models;
using RansomGuard.Agent.Core.Persistence.Entities;
using RansomGuard.Agent.Core.Security;
using RansomGuard.Agent.Core.Security.RateLimiting;

namespace RansomGuard.Agent.Core.Detection.UsbGuard.Scanning;

/// <summary>
/// Orchestrates all USB content sub-scanners with rate limiting and parallel scanning.
/// Pipeline: enumerate files -> rate-limit -> filter -> scan (4 workers) -> aggregate.
/// </summary>
public sealed class UsbContentScanner : IUsbContentScanner
{
    private const int WorkerCount = 4;
    private const int FileChannelCapacity = 500;

    private static readonly HashSet<string> ArchiveExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".zip", ".7z", ".rar" };

    private readonly IMagicByteValidator _magicByteValidator;
    private readonly AutorunInfDetector _autorunDetector;
    private readonly SuspiciousLnkDetector _lnkDetector;
    private readonly ArchiveScanner _archiveScanner;
    private readonly UsbEntropyScanner _entropyScanner;
    private readonly IOperationRateLimiter? _rateLimiter;
    private readonly ILogger<UsbContentScanner> _logger;

    /// <summary>Initializes the USB content scanner orchestrator.</summary>
    public UsbContentScanner(
        IMagicByteValidator magicByteValidator,
        AutorunInfDetector autorunDetector,
        SuspiciousLnkDetector lnkDetector,
        ArchiveScanner archiveScanner,
        UsbEntropyScanner entropyScanner,
        ILogger<UsbContentScanner> logger,
        IOperationRateLimiter? rateLimiter = null)
    {
        _magicByteValidator = magicByteValidator;
        _autorunDetector = autorunDetector;
        _lnkDetector = lnkDetector;
        _archiveScanner = archiveScanner;
        _entropyScanner = entropyScanner;
        _logger = logger;
        _rateLimiter = rateLimiter;
    }

    /// <inheritdoc />
    public async Task<UsbScanReport> ScanAsync(
        UsbDevice device, UsbPolicy policy, CancellationToken cancellationToken = default)
    {
        var scanId = Guid.NewGuid();
        var sw = Stopwatch.StartNew();
        var scannedAt = DateTime.UtcNow;
        string driveLetter = device.DriveLetter ?? "UNKNOWN";
        bool wasCancelled = false;

        _logger.LogInformation(
            "USB SCAN: Starting scan of {Drive} (device: {Product}, max size: {MaxMB} MB, timeout: {Timeout}s)",
            driveLetter, device.ProductDescription, policy.MaxFileSizeForScanMB, policy.MaxScanDurationSeconds);

        // Validate drive path
        var pathResult = PathValidator.Validate(driveLetter + @"\");
        if (!pathResult.IsValid)
        {
            _logger.LogWarning("USB SCAN: Invalid drive path {Drive}: {Error}", driveLetter, pathResult.ErrorMessage);
            return EmptyReport(scanId, device.Id, driveLetter, scannedAt, sw.Elapsed, false);
        }

        string rootPath = pathResult.CanonicalPath!;

        // Create scan timeout linked with caller cancellation
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(policy.MaxScanDurationSeconds));
        var ct = timeoutCts.Token;

        // Enumerate files into a channel for parallel consumption
        var fileChannel = Channel.CreateBounded<string>(
            new BoundedChannelOptions(FileChannelCapacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleWriter = true
            });

        int filesSkipped = 0;
        long maxFileSizeBytes = (long)policy.MaxFileSizeForScanMB * 1024 * 1024;

        // Producer: enumerate files
        var producerTask = Task.Run(async () =>
        {
            try
            {
                IEnumerable<string> files;
                try
                {
                    files = Directory.EnumerateFiles(rootPath, "*", new EnumerationOptions
                    {
                        RecurseSubdirectories = true,
                        IgnoreInaccessible = true,
                        AttributesToSkip = FileAttributes.System
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "USB SCAN: Cannot enumerate files on {Drive}", driveLetter);
                    return;
                }

                foreach (string filePath in files)
                {
                    ct.ThrowIfCancellationRequested();

                    // Skip SENTINEL canary files
                    string fileName = Path.GetFileName(filePath);
                    if (fileName.StartsWith("0001_", StringComparison.Ordinal))
                    {
                        Interlocked.Increment(ref filesSkipped);
                        continue;
                    }

                    // Skip files exceeding size limit
                    try
                    {
                        var fileInfo = new FileInfo(filePath);
                        if (fileInfo.Length > maxFileSizeBytes)
                        {
                            Interlocked.Increment(ref filesSkipped);
                            continue;
                        }
                    }
                    catch
                    {
                        Interlocked.Increment(ref filesSkipped);
                        continue;
                    }

                    // Rate limit
                    if (_rateLimiter is not null)
                    {
                        bool acquired = await _rateLimiter.AcquireAsync(ct);
                        if (!acquired)
                        {
                            Interlocked.Increment(ref filesSkipped);
                            continue;
                        }
                    }

                    await fileChannel.Writer.WriteAsync(filePath, ct);
                }
            }
            catch (OperationCanceledException) { }
            finally
            {
                fileChannel.Writer.TryComplete();
            }
        }, ct);

        // Consumers: 4 parallel workers scanning files
        var allFindings = new System.Collections.Concurrent.ConcurrentBag<UsbFileFinding>();
        int filesScanned = 0;

        var workerTasks = Enumerable.Range(0, WorkerCount).Select(_ => Task.Run(async () =>
        {
            await foreach (string filePath in fileChannel.Reader.ReadAllAsync(ct))
            {
                try
                {
                    var findings = await ScanFileAsync(filePath, policy, ct);
                    Interlocked.Increment(ref filesScanned);
                    foreach (var finding in findings)
                        allFindings.Add(finding);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "USB SCAN: Error scanning {File}", filePath);
                    Interlocked.Increment(ref filesScanned);
                }
            }
        }, ct)).ToArray();

        try
        {
            await producerTask;
            await Task.WhenAll(workerTasks);
        }
        catch (OperationCanceledException)
        {
            wasCancelled = true;
            _logger.LogInformation("USB SCAN: Scan cancelled for {Drive} after {Duration}ms",
                driveLetter, sw.ElapsedMilliseconds);
        }

        sw.Stop();

        var findingsList = allFindings.ToList();
        var overallSeverity = findingsList.Count > 0
            ? findingsList.Max(f => f.Severity)
            : ScanSeverity.None;

        _logger.LogInformation(
            "USB SCAN: Completed {Drive} — {Scanned} files scanned, {Skipped} skipped, {Findings} findings, severity: {Severity}, duration: {Duration}ms",
            driveLetter, filesScanned, filesSkipped, findingsList.Count, overallSeverity, sw.ElapsedMilliseconds);

        return new UsbScanReport
        {
            Id = scanId,
            UsbDeviceId = device.Id,
            DriveLetter = driveLetter,
            FilesScanned = filesScanned,
            FilesSkipped = filesSkipped,
            Findings = findingsList,
            OverallSeverity = overallSeverity,
            Duration = sw.Elapsed,
            ScannedAt = scannedAt,
            WasCancelled = wasCancelled
        };
    }

    private async Task<List<UsbFileFinding>> ScanFileAsync(
        string filePath, UsbPolicy policy, CancellationToken ct)
    {
        var findings = new List<UsbFileFinding>();
        string ext = Path.GetExtension(filePath).ToLowerInvariant();
        string fileName = Path.GetFileName(filePath);

        // 1. Magic byte validation (all files)
        var magicResult = await _magicByteValidator.ValidateAsync(filePath, ct);
        if (!magicResult.IsMatch && magicResult.Severity > ScanSeverity.None)
        {
            findings.Add(new UsbFileFinding
            {
                FilePath = filePath,
                FindingType = "MagicByteMismatch",
                Description = $"Extension '{magicResult.DeclaredExtension}' does not match content '{magicResult.DetectedFormat ?? "unknown"}'",
                Severity = magicResult.Severity,
                DetectorName = nameof(MagicByteValidator)
            });
        }

        // 2. Autorun.inf detection (root-level autorun.inf only)
        if (string.Equals(fileName, "autorun.inf", StringComparison.OrdinalIgnoreCase))
        {
            var autorunFinding = await _autorunDetector.DetectAsync(
                Path.GetDirectoryName(filePath)!, ct);
            if (autorunFinding is not null)
            {
                findings.Add(new UsbFileFinding
                {
                    FilePath = filePath,
                    FindingType = "Autorun",
                    Description = $"Dangerous autorun directives: {string.Join("; ", autorunFinding.Directives)}",
                    Severity = autorunFinding.Severity,
                    DetectorName = nameof(AutorunInfDetector)
                });
            }
        }

        // 3. Suspicious LNK detection
        if (string.Equals(ext, ".lnk", StringComparison.OrdinalIgnoreCase))
        {
            var lnkFinding = await _lnkDetector.AnalyzeAsync(filePath, ct);
            if (lnkFinding is not null)
            {
                findings.Add(new UsbFileFinding
                {
                    FilePath = filePath,
                    FindingType = "SuspiciousLnk",
                    Description = $"Suspicious shortcut: {string.Join("; ", lnkFinding.Reasons)}",
                    Severity = lnkFinding.Severity,
                    DetectorName = nameof(SuspiciousLnkDetector)
                });
            }
        }

        // 4. Archive scanning (ZIP, 7z, RAR)
        if (policy.ScanArchiveContents && ArchiveExtensions.Contains(ext))
        {
            var archiveFindings = await _archiveScanner.ScanAsync(filePath, ct);
            foreach (var af in archiveFindings)
            {
                findings.Add(new UsbFileFinding
                {
                    FilePath = filePath,
                    FindingType = "ArchiveThreat",
                    Description = $"{af.EntryName}: {af.Reason}",
                    Severity = af.Severity,
                    DetectorName = nameof(ArchiveScanner)
                });
            }
        }

        // 5. Entropy scanning (large files, non-whitelisted extensions)
        var entropyFinding = await _entropyScanner.ScanAsync(filePath, ct);
        if (entropyFinding is not null)
        {
            findings.Add(new UsbFileFinding
            {
                FilePath = filePath,
                FindingType = "HighEntropy",
                Description = $"High entropy {entropyFinding.Entropy:F2} bits/byte on {entropyFinding.FileExtension}",
                Severity = entropyFinding.Severity,
                DetectorName = nameof(UsbEntropyScanner)
            });
        }

        return findings;
    }

    private static UsbScanReport EmptyReport(
        Guid id, Guid deviceId, string driveLetter, DateTime scannedAt, TimeSpan duration, bool cancelled)
    {
        return new UsbScanReport
        {
            Id = id,
            UsbDeviceId = deviceId,
            DriveLetter = driveLetter,
            FilesScanned = 0,
            FilesSkipped = 0,
            Findings = [],
            OverallSeverity = ScanSeverity.None,
            Duration = duration,
            ScannedAt = scannedAt,
            WasCancelled = cancelled
        };
    }
}
