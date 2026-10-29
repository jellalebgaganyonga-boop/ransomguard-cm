using System.Text.Json;
using Microsoft.Extensions.Logging;
using RansomGuard.Agent.Core.Communication;
using RansomGuard.Agent.Core.Communication.Models;
using RansomGuard.Agent.Core.Detection.Sentinel;
using RansomGuard.Agent.Core.Persistence;
using RansomGuard.Agent.Core.Persistence.Entities;
using RansomGuard.Agent.Core.Security;

namespace RansomGuard.Agent.Core.Detection.Genealogy;

/// <summary>
/// Enriches alerts with process tree forensics by using RestartManagerHelper
/// for file-to-process attribution, then building the full process tree.
/// </summary>
public sealed class GenealogyEnricher : IGenealogyEnricher
{
    private readonly IProcessSnapshotService _snapshotService;
    private readonly RestartManagerHelper _restartManager;
    private readonly AgentDbContext _context;
    private readonly ILogger<GenealogyEnricher> _logger;
    private readonly ProcessAncestryStore? _ancestry;

    /// <summary>
    /// Where the chain is sent. Null keeps it on the workstation, which is what it used to do:
    /// the tree was written to the agent's own database and never left, so the console could show
    /// an alert with no idea what had produced it.
    /// </summary>
    private readonly IAlertForwardingQueue? _forwarding;

    /// <summary>
    /// Initializes the genealogy enricher.
    /// </summary>
    /// <param name="snapshotService">Captures processes and walks their ancestors.</param>
    /// <param name="restartManager">The fallback lookup: who holds the file open right now.</param>
    /// <param name="context">Database.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="ancestry">
    /// Ancestries captured while the writer was alive. Null leaves only the Restart Manager, which
    /// answers nothing once the writer has closed its handle -- the old behaviour.
    /// </param>
    /// <param name="forwarding">
    /// Where the chain is sent. Null keeps it on the workstation, which is what it used to do.
    /// </param>
    public GenealogyEnricher(
        IProcessSnapshotService snapshotService,
        RestartManagerHelper restartManager,
        AgentDbContext context,
        ILogger<GenealogyEnricher> logger,
        ProcessAncestryStore? ancestry = null,
        IAlertForwardingQueue? forwarding = null)
    {
        _snapshotService = snapshotService;
        _restartManager = restartManager;
        _context = context;
        _logger = logger;
        _ancestry = ancestry;
        _forwarding = forwarding;
    }

    /// <inheritdoc />
    public async Task<string?> EnrichFromWriterAsync(
        Guid alertId,
        int writerPid,
        DateTime? writerStartedAtUtc,
        string offendingFilePath,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // The reliable path: captured while the writer was running, so it survives the
            // write-and-close that defeats the Restart Manager.
            ProcessTree? tree = _ancestry?.TryGet(writerPid, writerStartedAtUtc);

            if (tree is null)
            {
                // Nothing captured for this writer -- it exited before the capture loop reached it,
                // or the kernel session is down. Try it live; it is often still there.
                tree = _snapshotService.BuildProcessTree(writerPid);

                // A live read can hit a reused PID: the process answering now may have started
                // after the write. Refused rather than reported as the writer's ancestry.
                if (tree is not null && writerStartedAtUtc is not null && tree.Root.StartTime is not null
                    && Math.Abs((tree.Root.StartTime.Value - writerStartedAtUtc.Value).TotalSeconds) > 1)
                {
                    _logger.LogDebug(
                        "GENEALOGY refused PID {Pid} for alert {AlertId}: it started {Actual:O}, the writer started {Expected:O} -- the PID was reused",
                        writerPid, alertId, tree.Root.StartTime, writerStartedAtUtc);
                    tree = null;
                }
            }

            if (tree is null)
            {
                _logger.LogDebug(
                    "GENEALOGY has no ancestry for the writer PID {Pid} of alert {AlertId}; falling back to the file lookup",
                    writerPid, alertId);
                return await EnrichAlertAsync(alertId, offendingFilePath, cancellationToken).ConfigureAwait(false);
            }

            return await PersistAsync(alertId, tree, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GENEALOGY enrichment from writer {Pid} failed for alert {AlertId}", writerPid, alertId);
            return null;
        }
    }

    /// <summary>Stores the tree and its patterns, and logs what was found.</summary>
    private async Task<string?> PersistAsync(Guid alertId, ProcessTree tree, CancellationToken cancellationToken)
    {
        IReadOnlyList<SuspiciousPatternFlag> patterns = SuspiciousPatternDetector.Analyze(tree);

        var record = new GenealogyRecord
        {
            AlertId = alertId,
            ProcessTreeJson = JsonSerializer.Serialize(tree),
            SuspiciousPatternsJson = JsonSerializer.Serialize(patterns),
            RootProcessId = tree.Root.ProcessId,
            RootProcessName = tree.Root.ProcessName,
            Summary = tree.Summary
        };

        _context.Set<GenealogyRecord>().Add(record);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "GENEALOGY enrichment for alert {AlertId}: {Summary} | {PatternCount} suspicious patterns",
            alertId, tree.Summary, patterns.Count);

        foreach (SuspiciousPatternFlag p in patterns)
        {
            _logger.LogWarning(
                "GENEALOGY suspicious pattern: [{TechniqueId}] {TechniqueName} — {Description}",
                p.TechniqueId, p.TechniqueName, p.Description);
        }

        Forward(alertId, tree, patterns);

        return tree.Summary;
    }

    /// <summary>
    /// Sends the chain to the GRID, against the alert it belongs to.
    ///
    /// <para>
    /// The alert's client_message_id is derived from its id the same way the alert itself derived
    /// it, so no lookup is needed and the two cannot disagree. A failure here is logged and
    /// dropped: the enrichment is not worth losing the alert over, and the chain is still in the
    /// agent's own database.
    /// </para>
    /// </summary>
    private void Forward(Guid alertId, ProcessTree tree, IReadOnlyList<SuspiciousPatternFlag> patterns)
    {
        if (_forwarding is null)
        {
            return;
        }

        try
        {
            _forwarding.TryEnqueueGenealogy(
                AlertMapper.DeterministicId(alertId),
                new GenealogyComplementRequest
                {
                    Writer = Describe(tree.Root),
                    Ancestors = [.. tree.Ancestors.Select(Describe)],
                    Patterns = [.. patterns.Select(p => new GenealogyPatternDto
                    {
                        TechniqueId = p.TechniqueId,
                        TechniqueName = p.TechniqueName,
                        Description = p.Description,
                    })],
                });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GENEALOGY chain for alert {AlertId} could not be queued", alertId);
        }
    }

    private static GenealogyProcessDto Describe(ProcessSnapshot process) => new()
    {
        Pid = process.ProcessId,
        Name = process.ProcessName,
        Path = process.ExecutablePath,
        StartedAt = process.StartTime,
        CommandLine = process.CommandLine,
    };

    /// <inheritdoc />
    public async Task<string?> EnrichAlertAsync(Guid alertId, string offendingFilePath, CancellationToken cancellationToken = default)
    {
        try
        {
            // Validate file path at system boundary (CWE-22)
            var pathResult = PathValidator.Validate(offendingFilePath);
            if (!pathResult.IsValid)
            {
                _logger.LogWarning("GENEALOGY rejected invalid path: {Error}", pathResult.ErrorMessage);
                return null;
            }

            // Step 1: Find which process holds the file
            if (!OperatingSystem.IsWindows()) return null;
            IReadOnlyList<ProcessAttribution> attributions = _restartManager.GetProcessesLockingFile(pathResult.CanonicalPath!);

            int targetPid;
            if (attributions.Count > 0)
            {
                // Pick most recently started
                targetPid = attributions
                    .OrderByDescending(a => a.StartTime ?? DateTime.MinValue)
                    .First().ProcessId;
            }
            else
            {
                _logger.LogDebug("No process found locking {FilePath}, cannot build genealogy", offendingFilePath);
                return null;
            }

            // Step 2: Build process tree
            ProcessTree? tree = _snapshotService.BuildProcessTree(targetPid);
            if (tree is null)
            {
                _logger.LogDebug("Cannot build process tree for PID {PID}", targetPid);
                return null;
            }

            // Steps 3 and 4: patterns and persistence, shared with the attributed-writer path.
            return await PersistAsync(alertId, tree, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GENEALOGY enrichment failed for alert {AlertId}", alertId);
            return null;
        }
    }
}
