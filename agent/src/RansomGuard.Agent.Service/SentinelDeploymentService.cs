using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RansomGuard.Agent.Core.Configuration;
using RansomGuard.Agent.Core.Detection.Sentinel;
using RansomGuard.Agent.Core.Persistence.Entities;

namespace RansomGuard.Agent.Service;

/// <summary>
/// BackgroundService that deploys SENTINEL canary files on startup.
/// Ensures the configured number of canaries exist in each watch directory.
/// Idempotent: existing active canaries are preserved; deleted/compromised are re-deployed.
/// </summary>
public sealed class SentinelDeploymentService : BackgroundService
{
    private readonly ILogger<SentinelDeploymentService> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly AgentConfiguration _config;

    /// <summary>
    /// Initializes the SENTINEL deployment service.
    /// </summary>
    public SentinelDeploymentService(
        ILogger<SentinelDeploymentService> logger,
        IOptionsMonitor<AgentConfiguration> config,
        IServiceScopeFactory scopeFactory)
    {
        _logger = logger;
        _config = config.CurrentValue;
        _scopeFactory = scopeFactory;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        SentinelOptions? sentinel = _config.Sentinel;

        if (sentinel is null || !sentinel.Enabled)
        {
            _logger.LogInformation("SENTINEL module is disabled");
            return;
        }

        _logger.LogInformation("SENTINEL deployment starting — {DirectoryCount} directories, {CanariesPerDir} canaries each",
            sentinel.WatchDirectories.Length, sentinel.CanariesPerDirectory);

        string[] resolvedDirs = EnvironmentVariableResolver.ResolvePaths(sentinel.WatchDirectories);
        int totalDeployed = 0;
        int totalExisting = 0;

        foreach (string directory in resolvedDirs)
        {
            try
            {
                int deployed = await DeployCanariesForDirectoryAsync(directory, sentinel, stoppingToken);
                totalDeployed += deployed;

                using IServiceScope scope = _scopeFactory.CreateScope();
                var canaryService = scope.ServiceProvider.GetRequiredService<ICanaryFileService>();
                var allActive = await canaryService.GetAllCanariesAsync(stoppingToken);
                int activeInDir = allActive.Count(c => c.Directory == directory);
                totalExisting += activeInDir;

                _logger.LogInformation("SENTINEL directory {Directory}: {ActiveCount} active canaries ({DeployedCount} newly deployed)",
                    directory, activeInDir, deployed);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "SENTINEL deployment failed for directory {Directory}. Continuing with remaining directories", directory);
            }
        }

        _logger.LogInformation("SENTINEL deployment complete — {TotalActive} active canaries across {DirCount} directories ({NewlyDeployed} newly deployed)",
            totalExisting, resolvedDirs.Length, totalDeployed);
    }

    private async Task<int> DeployCanariesForDirectoryAsync(string directory, SentinelOptions sentinel, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
            _logger.LogInformation("Created SENTINEL watch directory: {Directory}", directory);
        }

        using IServiceScope scope = _scopeFactory.CreateScope();
        var canaryService = scope.ServiceProvider.GetRequiredService<ICanaryFileService>();

        var existingActive = (await canaryService.GetAllCanariesAsync(cancellationToken))
            .Where(c => c.Directory == directory)
            .ToList();

        int needed = sentinel.CanariesPerDirectory - existingActive.Count;
        if (needed <= 0)
        {
            return 0;
        }

        int deployed = 0;
        int templateIndex = existingActive.Count;

        for (int i = 0; i < needed; i++)
        {
            string template = sentinel.CanaryTemplates[templateIndex % sentinel.CanaryTemplates.Length];
            templateIndex++;

            await canaryService.CreateCanaryAsync(directory, template, sentinel.CanaryPrefix, cancellationToken);
            deployed++;
        }

        return deployed;
    }
}
