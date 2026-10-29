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
                    // Nothing is skipped for being Hidden or System. That was the default here
                    // and it is the wrong default on a USB key: Hidden + System is not where
                    // Windows keeps its furniture on removable media, it is where USB malware
                    // hides. On 2026-10-29 a real key was scanned and reported "2 files scanned,
                    // 0 findings" while 769 files sat in a Hidden + System folder called
                    // RECYCLER.BIN -- the owner's documents under base64 names, and a script that
                    // collects systeminfo, ipconfig, netstat, arp and tasklist onto the key. The
                    // module walked past all of it.
                    //
                    // The two folders Windows really does own are skipped by name instead, in
                    // SkipDirectory, which is a closed list rather than an attribute anything can
                    // set on itself.
                    files = Directory.EnumerateFiles(rootPath, "*", new EnumerationOptions
                    {
                        RecurseSubdirectories = true,
                        IgnoreInaccessible = true,
                        AttributesToSkip = FileAttributes.None
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

                    // Windows' own two folders, by name. Everything else is looked at.
                    if (IsWindowsOwned(rootPath, filePath))
                    {
                        Interlocked.Increment(ref filesSkipped);
                        continue;
                    }

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
                    var findings = await ScanFileAsync(filePath, rootPath, policy, ct);
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

    /// <summary>
    /// The two folders Windows itself puts on a removable volume, matched by name at the root.
    ///
    /// <para>
    /// `System Volume Information` holds the volume's own metadata and is unreadable anyway;
    /// `$RECYCLE.BIN` is the real recycle bin. Both are skipped because they belong to Windows,
    /// not because of an attribute -- a folder that merely *calls itself* a recycle bin, like the
    /// `RECYCLER.BIN` found on a real key on 2026-10-29, is somebody else's idea and is read.
    /// </para>
    /// </summary>
    private static bool IsWindowsOwned(string rootPath, string filePath)
    {
        string relative = Path.GetRelativePath(rootPath, filePath);
        string first = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
        return first.Equals("System Volume Information", StringComparison.OrdinalIgnoreCase)
            || first.Equals("$RECYCLE.BIN", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Extensions that run something when somebody double-clicks them.</summary>
    private static readonly HashSet<string> RunnableExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".exe", ".com", ".scr", ".pif", ".bat", ".cmd", ".vbs", ".vbe", ".js", ".jse",
            ".wsf", ".wsh", ".ps1", ".msi", ".hta", ".cpl", ".dll", ".lnk",
        };

    /// <summary>The CLSID that makes a folder show up in Explorer as the recycle bin.</summary>
    private const string RecycleBinClsid = "{645FF040-5081-101B-9F08-00AA002F954E}";

    /// <summary>
    /// Is this file inside a folder that is hiding it -- Hidden and System, on a key where
    /// Windows owns no such folder?
    /// </summary>
    private static bool IsConcealedByItsFolder(string rootPath, string filePath)
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(filePath)!);
        var root = new DirectoryInfo(rootPath);
        while (directory is not null && !PathsEqual(directory.FullName, root.FullName))
        {
            if (directory.Attributes.HasFlag(FileAttributes.Hidden)
                && directory.Attributes.HasFlag(FileAttributes.System))
            {
                return true;
            }

            directory = directory.Parent;
        }

        return false;
    }

    private static bool PathsEqual(string a, string b) =>
        string.Equals(a.TrimEnd(Path.DirectorySeparatorChar), b.TrimEnd(Path.DirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// A folder dressed up as the recycle bin: a desktop.ini that claims the recycle bin's CLSID,
    /// in a folder that is not the real `$RECYCLE.BIN`.
    ///
    /// <para>
    /// This is not a guess about a name. Explorer reads that CLSID and draws the folder as an
    /// empty recycle bin, which is the whole point of writing it: whatever is inside stops being
    /// visible to the person carrying the key. Found on a real key on 2026-10-29, holding 766 of
    /// the owner's documents under base64 names.
    /// </para>
    /// </summary>
    private static async Task<bool> IsDisguisedAsRecycleBinAsync(string directoryPath, CancellationToken ct)
    {
        string desktopIni = Path.Combine(directoryPath, "desktop.ini");
        if (!File.Exists(desktopIni))
        {
            return false;
        }

        try
        {
            string text = await File.ReadAllTextAsync(desktopIni, ct);
            return text.Contains(RecycleBinClsid, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private async Task<List<UsbFileFinding>> ScanFileAsync(
        string filePath, string rootPath, UsbPolicy policy, CancellationToken ct)
    {
        var findings = new List<UsbFileFinding>();
        string ext = Path.GetExtension(filePath).ToLowerInvariant();
        string fileName = Path.GetFileName(filePath);
        bool concealed = IsConcealedByItsFolder(rootPath, filePath);

        // 0. Hidden where somebody put it, not where Windows did.
        if (concealed && RunnableExtensions.Contains(ext))
        {
            findings.Add(new UsbFileFinding
            {
                FilePath = filePath,
                FindingType = "ConcealedExecutable",
                Description =
                    $"'{fileName}' runs when it is opened, and sits in a folder marked hidden and "
                    + "system so that nobody carrying this key would see it",
                Severity = ScanSeverity.High,
                DetectorName = nameof(UsbContentScanner)
            });
        }

        // 0b. A folder wearing the recycle bin's face.
        if (string.Equals(fileName, "desktop.ini", StringComparison.OrdinalIgnoreCase))
        {
            string folder = Path.GetDirectoryName(filePath)!;
            if (!IsWindowsOwned(rootPath, filePath)
                && await IsDisguisedAsRecycleBinAsync(folder, ct))
            {
                int inside = 0;
                try
                {
                    inside = Directory.EnumerateFiles(folder, "*", new EnumerationOptions
                    {
                        RecurseSubdirectories = true,
                        IgnoreInaccessible = true,
                        AttributesToSkip = FileAttributes.None,
                    }).Count();
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // the count is a detail; the disguise is the finding
                }

                findings.Add(new UsbFileFinding
                {
                    FilePath = filePath,
                    FindingType = "DisguisedRecycleBin",
                    Description =
                        $"'{Path.GetFileName(folder)}' is dressed up as the recycle bin -- its "
                        + "desktop.ini claims the recycle bin's CLSID, so Explorer draws it empty "
                        + $"-- and it holds {inside} file(s). The real one is $RECYCLE.BIN",
                    Severity = ScanSeverity.High,
                    DetectorName = nameof(UsbContentScanner)
                });
            }
        }

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
