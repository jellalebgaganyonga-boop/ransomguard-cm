using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RansomGuard.Agent.Core.Configuration;

namespace RansomGuard.Agent.Core.Communication;

/// <summary>
/// Persists and restores agent enrollment state (agent_id, tenant_id) to a JSON file
/// in the same directory as the database keys. Survives agent restarts.
/// </summary>
public sealed class EnrollmentStateManager
{
    private readonly string _stateFilePath;
    private readonly ILogger<EnrollmentStateManager> _logger;

    /// <summary>Initializes a new EnrollmentStateManager.</summary>
    public EnrollmentStateManager(
        IOptions<AgentConfiguration> config,
        ILogger<EnrollmentStateManager> logger)
    {
        _logger = logger;

        // Store enrollment state alongside the DB key directory
        string keyDir = EnvironmentVariableResolver.ResolvePath(
            config.Value.Database?.ConnectionString ?? "%ProgramData%\\RansomGuard-CM\\data");

        // Extract directory from connection string "Data Source=<path>"
        string dataDir = keyDir;
        const string prefix = "Data Source=";
        int idx = keyDir.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
        if (idx >= 0)
        {
            string dbPath = keyDir[(idx + prefix.Length)..].Split(';')[0].Trim();
            dbPath = EnvironmentVariableResolver.ResolvePath(dbPath);
            dataDir = Path.GetDirectoryName(dbPath) ?? dataDir;
        }

        if (!Directory.Exists(dataDir))
            Directory.CreateDirectory(dataDir);

        _stateFilePath = Path.Combine(dataDir, "enrollment.json");
    }

    /// <summary>
    /// Persist enrollment state to disk after successful enrollment.
    /// </summary>
    public void Save(string agentId, string tenantId)
    {
        var state = new EnrollmentState { AgentId = agentId, TenantId = tenantId };
        string json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_stateFilePath, json);
        _logger.LogInformation("Enrollment state saved to {Path}", _stateFilePath);
    }

    /// <summary>
    /// Try to restore enrollment state from disk. Returns null if not enrolled.
    /// </summary>
    public EnrollmentState? TryLoad()
    {
        if (!File.Exists(_stateFilePath))
        {
            _logger.LogInformation("No enrollment state file found at {Path}", _stateFilePath);
            return null;
        }

        try
        {
            string json = File.ReadAllText(_stateFilePath);
            var state = JsonSerializer.Deserialize<EnrollmentState>(json);
            if (state is not null && !string.IsNullOrEmpty(state.AgentId))
            {
                _logger.LogInformation("Enrollment state restored: AgentId={AgentId}", state.AgentId);
                return state;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read enrollment state from {Path}", _stateFilePath);
        }

        return null;
    }

    /// <summary>
    /// Delete enrollment state (for re-enrollment).
    /// </summary>
    public void Clear()
    {
        if (File.Exists(_stateFilePath))
        {
            File.Delete(_stateFilePath);
            _logger.LogInformation("Enrollment state cleared");
        }
    }
}

/// <summary>
/// Serializable enrollment state.
/// </summary>
public sealed class EnrollmentState
{
    /// <summary>Agent ID assigned by GRID server.</summary>
    public string AgentId { get; set; } = string.Empty;

    /// <summary>Tenant ID assigned by GRID server.</summary>
    public string TenantId { get; set; } = string.Empty;
}
