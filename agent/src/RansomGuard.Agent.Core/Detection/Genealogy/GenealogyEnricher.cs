using System.Text.Json;
using Microsoft.Extensions.Logging;
using RansomGuard.Agent.Core.Detection.Sentinel;
using RansomGuard.Agent.Core.Persistence;
using RansomGuard.Agent.Core.Persistence.Entities;

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

    /// <summary>
    /// Initializes the genealogy enricher.
    /// </summary>
    public GenealogyEnricher(
        IProcessSnapshotService snapshotService,
        RestartManagerHelper restartManager,
        AgentDbContext context,
        ILogger<GenealogyEnricher> logger)
    {
        _snapshotService = snapshotService;
        _restartManager = restartManager;
        _context = context;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<string?> EnrichAlertAsync(Guid alertId, string offendingFilePath, CancellationToken cancellationToken = default)
    {
        try
        {
            // Step 1: Find which process holds the file
            if (!OperatingSystem.IsWindows()) return null;
            IReadOnlyList<ProcessAttribution> attributions = _restartManager.GetProcessesLockingFile(offendingFilePath);

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

            // Step 3: Detect suspicious patterns
            IReadOnlyList<SuspiciousPatternFlag> patterns = SuspiciousPatternDetector.Analyze(tree);

            // Step 4: Persist
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
            await _context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "GENEALOGY enrichment for alert {AlertId}: {Summary} | {PatternCount} suspicious patterns",
                alertId, tree.Summary, patterns.Count);

            if (patterns.Count > 0)
            {
                foreach (var p in patterns)
                {
                    _logger.LogWarning(
                        "GENEALOGY suspicious pattern: [{TechniqueId}] {TechniqueName} — {Description}",
                        p.TechniqueId, p.TechniqueName, p.Description);
                }
            }

            return tree.Summary;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GENEALOGY enrichment failed for alert {AlertId}", alertId);
            return null;
        }
    }
}
