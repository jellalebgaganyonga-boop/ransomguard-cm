using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RansomGuard.Agent.Core.Communication.Models;
using RansomGuard.Agent.Core.Configuration;

namespace RansomGuard.Agent.Core.Communication;

/// <summary>
/// HTTP client for the RansomGuard GRID management server.
/// Uses IHttpClientFactory for proper connection management.
/// mTLS is configured at the HttpClientHandler level (Sprint 9).
/// </summary>
public sealed class GridApiClient : IGridApiClient
{
    /// <summary>Name used for IHttpClientFactory registration.</summary>
    public const string HttpClientName = "GridApi";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = false,
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<GridApiClient> _logger;
    private readonly ServerOptions _serverOptions;

    private string? _agentId;
    private string? _tenantId;

    /// <inheritdoc />
    public bool IsEnrolled => _agentId is not null;

    /// <inheritdoc />
    public string? AgentId => _agentId;

    /// <inheritdoc />
    public string? TenantId => _tenantId;

    /// <summary>Initializes a new GridApiClient.</summary>
    public GridApiClient(
        IHttpClientFactory httpClientFactory,
        IOptions<AgentConfiguration> config,
        ILogger<GridApiClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _serverOptions = config.Value.Server;
    }

    /// <summary>
    /// Restore enrollment state from persisted agent ID and tenant ID.
    /// Called on startup when the agent was previously enrolled.
    /// </summary>
    public void RestoreEnrollment(string agentId, string tenantId)
    {
        _agentId = agentId;
        _tenantId = tenantId;
    }

    /// <inheritdoc />
    public async Task<EnrollmentResponse> EnrollAsync(EnrollmentRequest request, CancellationToken ct = default)
    {
        var client = CreateClient();
        var response = await client.PostAsJsonAsync(
            "/api/v1/agents/enroll", request, JsonOptions, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<EnrollmentResponse>(JsonOptions, ct)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("Empty enrollment response");

        _agentId = result.AgentId;
        _tenantId = result.TenantId;
        _logger.LogInformation("Enrolled with GRID server: AgentId={AgentId}, TenantId={TenantId}",
            _agentId, _tenantId);

        return result;
    }

    /// <inheritdoc />
    public async Task<HeartbeatResponse> SendHeartbeatAsync(HeartbeatRequest request, CancellationToken ct = default)
    {
        EnsureEnrolled();
        var client = CreateClient();
        var response = await client.PostAsJsonAsync(
            $"/api/v1/agents/{_agentId}/heartbeat", request, JsonOptions, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<HeartbeatResponse>(JsonOptions, ct)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("Empty heartbeat response");
    }

    /// <inheritdoc />
    public async Task<AlertIngestResponse> SendAlertAsync(AlertIngestRequest request, CancellationToken ct = default)
    {
        EnsureEnrolled();
        var client = CreateClient();
        var response = await client.PostAsJsonAsync(
            $"/api/v1/agents/{_agentId}/alerts", request, JsonOptions, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<AlertIngestResponse>(JsonOptions, ct)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("Empty alert response");
    }

    /// <inheritdoc />
    public async Task<AuditLogBatchResponse> SendAuditLogBatchAsync(AuditLogBatchRequest request, CancellationToken ct = default)
    {
        EnsureEnrolled();
        var client = CreateClient();
        var response = await client.PostAsJsonAsync(
            $"/api/v1/agents/{_agentId}/audit-logs", request, JsonOptions, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<AuditLogBatchResponse>(JsonOptions, ct)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("Empty audit log response");
    }

    private HttpClient CreateClient()
    {
        var client = _httpClientFactory.CreateClient(HttpClientName);
        client.BaseAddress = new Uri(_serverOptions.BaseUrl);
        client.Timeout = TimeSpan.FromSeconds(_serverOptions.ConnectionTimeoutSeconds);
        return client;
    }

    private void EnsureEnrolled()
    {
        if (_agentId is null)
            throw new InvalidOperationException("Agent is not enrolled. Call EnrollAsync first.");
    }
}
