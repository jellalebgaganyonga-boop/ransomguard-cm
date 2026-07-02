using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RansomGuard.Agent.Core.Communication;
using RansomGuard.Agent.Core.Communication.Models;
using RansomGuard.Agent.Core.Configuration;

namespace RansomGuard.Agent.Service;

/// <summary>
/// Background service that ensures the agent is enrolled with the GRID server.
/// On startup: restores previous enrollment from disk, or enrolls using the configured OTP.
/// Signals enrollment completion via <see cref="IsEnrolled"/> for other services to wait on.
/// </summary>
public sealed class EnrollmentService : BackgroundService
{
    private readonly GridApiClient _gridClient;
    private readonly EnrollmentStateManager _stateManager;
    private readonly AgentConfiguration _config;
    private readonly ILogger<EnrollmentService> _logger;
    private readonly TaskCompletionSource _enrolledSignal = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>A task that completes when the agent is enrolled (or restored from disk).</summary>
    public Task EnrolledTask => _enrolledSignal.Task;

    /// <summary>Whether the agent is currently enrolled.</summary>
    public bool IsEnrolled => _enrolledSignal.Task.IsCompletedSuccessfully;

    public EnrollmentService(
        GridApiClient gridClient,
        EnrollmentStateManager stateManager,
        IOptions<AgentConfiguration> config,
        ILogger<EnrollmentService> logger)
    {
        _gridClient = gridClient;
        _stateManager = stateManager;
        _config = config.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Step 1: Try to restore previous enrollment
        var savedState = _stateManager.TryLoad();
        if (savedState is not null)
        {
            _gridClient.RestoreEnrollment(savedState.AgentId, savedState.TenantId);
            _logger.LogInformation("Enrollment restored from disk: AgentId={AgentId}", savedState.AgentId);
            _enrolledSignal.TrySetResult();
            return;
        }

        // Step 2: Enroll using OTP
        string? otp = _config.Server.EnrollmentOtp;
        if (string.IsNullOrWhiteSpace(otp))
        {
            _logger.LogWarning(
                "No enrollment state and no EnrollmentOtp configured. " +
                "Set Agent:Server:EnrollmentOtp in appsettings.json to enroll this agent.");
            // Don't signal — heartbeat/command services will wait indefinitely
            return;
        }

        _logger.LogInformation("Enrolling with GRID server using OTP...");

        // Retry loop with exponential backoff
        int attempt = 0;
        int[] delaysSeconds = [5, 10, 30, 60, 120];

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var request = new EnrollmentRequest
                {
                    Otp = otp,
                    Hostname = Environment.MachineName,
                    Fqdn = $"{Environment.MachineName}.local",
                    OsVersion = Environment.OSVersion.ToString(),
                    AgentVersion = _config.Identity.Version,
                    HardwareFingerprint = GenerateHardwareFingerprint(),
                };

                var response = await _gridClient.EnrollAsync(request, stoppingToken).ConfigureAwait(false);

                // Persist enrollment state
                _stateManager.Save(response.AgentId, response.TenantId);

                _logger.LogInformation(
                    "Enrollment successful: AgentId={AgentId}, TenantId={TenantId}",
                    response.AgentId, response.TenantId);

                _enrolledSignal.TrySetResult();
                return;
            }
            catch (HttpRequestException ex)
            {
                int delay = delaysSeconds[Math.Min(attempt, delaysSeconds.Length - 1)];
                _logger.LogWarning(ex,
                    "Enrollment failed (attempt {Attempt}), retrying in {Delay}s",
                    attempt + 1, delay);
                attempt++;

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(delay), stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
            catch (TaskCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected enrollment error");
                break;
            }
        }
    }

    /// <summary>
    /// Generate a stable hardware fingerprint from machine name + processor count + OS.
    /// In production this would use WMI for motherboard serial, but this is sufficient for dev.
    /// </summary>
    private static string GenerateHardwareFingerprint()
    {
        string raw = $"{Environment.MachineName}-{Environment.ProcessorCount}-{Environment.OSVersion}";
        byte[] hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
