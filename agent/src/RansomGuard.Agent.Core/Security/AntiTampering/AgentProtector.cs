using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;

namespace RansomGuard.Agent.Core.Security.AntiTampering;

/// <summary>
/// Composite agent protector that orchestrates multiple anti-tampering sub-components.
/// Addresses CWE-345 (insufficient verification), CWE-269 (privilege escalation),
/// and CWE-426 (untrusted search path).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class AgentProtector : IAgentProtector, IDisposable
{
    private readonly DebuggerDetector _debuggerDetector;
    private readonly CodeSectionIntegrity _codeIntegrity;
    private readonly RegistryWatcher _registryWatcher;
    private readonly ILogger<AgentProtector> _logger;
    private Timer? _periodicCheck;

    /// <summary>Initializes the agent protector with all sub-components.</summary>
    public AgentProtector(
        DebuggerDetector debuggerDetector,
        CodeSectionIntegrity codeIntegrity,
        RegistryWatcher registryWatcher,
        ILogger<AgentProtector> logger)
    {
        _debuggerDetector = debuggerDetector;
        _codeIntegrity = codeIntegrity;
        _registryWatcher = registryWatcher;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task StartProtectionAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("ANTI-TAMPER: Starting agent self-protection");

        // 1. Code integrity baseline
        _codeIntegrity.ComputeBaseline();

        // 2. Registry baseline + monitoring
        _registryWatcher.StartMonitoring();

        // 3. Debugger check at startup
        string? debuggerResult = _debuggerDetector.Check();
        if (debuggerResult is not null)
        {
            _logger.LogCritical("ANTI-TAMPER: Startup debugger check — {Result}", debuggerResult);
        }

        // 4. Periodic check every 60 seconds
        _periodicCheck = new Timer(
            _ => PeriodicIntegrityCheck(),
            null,
            TimeSpan.FromSeconds(60),
            TimeSpan.FromSeconds(60));

        _logger.LogInformation("ANTI-TAMPER: Agent self-protection active (60-second check interval)");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<TamperingStatus> CheckIntegrityAsync(CancellationToken cancellationToken = default)
    {
        var indicators = new List<string>();

        // Check debugger
        string? debugger = _debuggerDetector.Check();
        if (debugger is not null) indicators.Add(debugger);

        // Check code integrity
        string? codeIntegrity = _codeIntegrity.Verify();
        if (codeIntegrity is not null) indicators.Add(codeIntegrity);

        // Check registry
        var registryMods = _registryWatcher.CheckForModifications();
        indicators.AddRange(registryMods);

        var status = new TamperingStatus
        {
            IsIntact = indicators.Count == 0,
            TamperingIndicators = indicators,
            CheckedAt = DateTime.UtcNow
        };

        return Task.FromResult(status);
    }

    private void PeriodicIntegrityCheck()
    {
        try
        {
            var status = CheckIntegrityAsync().GetAwaiter().GetResult();
            if (!status.IsIntact)
            {
                foreach (string indicator in status.TamperingIndicators)
                {
                    _logger.LogCritical("ANTI-TAMPER periodic check: {Indicator}", indicator);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ANTI-TAMPER periodic check failed");
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _periodicCheck?.Dispose();
        _registryWatcher.Dispose();
    }
}
