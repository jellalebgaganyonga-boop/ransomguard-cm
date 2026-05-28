using System.Runtime.Versioning;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RansomGuard.Agent.Core.Configuration;
using RansomGuard.Agent.Core.Detection.ExfilWatch.Actions;
using RansomGuard.Agent.Core.Persistence.Repositories;

namespace RansomGuard.Agent.Service;

/// <summary>
/// Background service that removes expired RansomGuard firewall block rules.
/// Runs daily at 02:00 local time. Each cleanup is audit-logged with Ed25519 signature.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ExfilFirewallRuleCleanupService : BackgroundService
{
    private static readonly TimeSpan CleanupHour = TimeSpan.FromHours(2); // 02:00 local

    private readonly IFirewallManager _firewall;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly AgentConfiguration _config;
    private readonly ILogger<ExfilFirewallRuleCleanupService> _logger;

    /// <summary>Initializes the cleanup service.</summary>
    public ExfilFirewallRuleCleanupService(
        IFirewallManager firewall,
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<AgentConfiguration> config,
        ILogger<ExfilFirewallRuleCleanupService> logger)
    {
        _firewall = firewall;
        _scopeFactory = scopeFactory;
        _config = config.CurrentValue;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_config.ExfilWatch is null || !_config.ExfilWatch.Enabled)
            return;

        _logger.LogInformation("Firewall rule cleanup service started (runs daily at 02:00)");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                TimeSpan delayUntilNext = CalculateDelayUntilNextRun();
                await Task.Delay(delayUntilNext, stoppingToken);

                await RunCleanupAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Firewall rule cleanup failed");
                // Wait 1 hour before retrying on error
                await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
            }
        }
    }

    /// <summary>
    /// Removes all expired RansomGuard firewall rules. Exposed for testing.
    /// </summary>
    internal async Task RunCleanupAsync(CancellationToken ct)
    {
        int expiryHours = _config.ExfilWatch?.BlockRuleExpiryHours ?? 24;
        var cutoff = DateTime.UtcNow.AddHours(-expiryHours);

        var ruleNames = _firewall.GetManagedRuleNames();
        int removed = 0;

        foreach (var name in ruleNames)
        {
            var created = BlockIpAction.ParseRuleTimestamp(name);
            if (created is null || created.Value >= cutoff)
                continue;

            if (_firewall.RemoveRule(name))
            {
                removed++;
                _logger.LogInformation("Expired firewall rule removed: {Rule}", name);
            }
        }

        // Audit log the cleanup
        await using var scope = _scopeFactory.CreateAsyncScope();
        var auditLog = scope.ServiceProvider.GetRequiredService<IAuditLogRepository>();

        await auditLog.AppendAsync(
            "ExfilFirewallCleanup",
            $"Expired rules removed: {removed}, Total managed: {ruleNames.Count}, Expiry: {expiryHours}h",
            entityType: "FirewallRule",
            cancellationToken: ct);

        _logger.LogInformation(
            "Firewall cleanup complete: {Removed}/{Total} rules removed (expiry: {Hours}h)",
            removed, ruleNames.Count, expiryHours);
    }

    private static TimeSpan CalculateDelayUntilNextRun()
    {
        var now = DateTime.Now;
        var nextRun = now.Date.Add(CleanupHour);

        if (nextRun <= now)
            nextRun = nextRun.AddDays(1);

        return nextRun - now;
    }
}
