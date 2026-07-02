using System.Net;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RansomGuard.Agent.Core.Communication;
using RansomGuard.Agent.Core.Communication.Models;
using RansomGuard.Agent.Core.Persistence;
using RansomGuard.Agent.Core.Persistence.Entities;

namespace RansomGuard.Agent.Service;

/// <summary>
/// Background service that forwards detection alerts to the GRID server.
/// Consumes from an in-memory Channel queue (non-blocking for detection modules).
/// Failed sends are persisted to SQLite PendingAlertUpload for resilient retry.
/// </summary>
public sealed class AlertForwardingService : BackgroundService, IAlertForwardingQueue
{
    private const int QueueCapacity = 5000;
    private static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan RetryPollInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MaxRetryInterval = TimeSpan.FromMinutes(5);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = false,
    };

    private readonly Channel<AlertIngestRequest> _channel;
    private readonly IGridApiClient _gridClient;
    private readonly EnrollmentService _enrollmentService;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AlertForwardingService> _logger;

    /// <summary>Total alerts successfully forwarded since startup.</summary>
    public long TotalForwarded => _totalForwarded;
    private long _totalForwarded;

    /// <summary>Total alerts that failed validation (422) and won't be retried.</summary>
    public long TotalFailedValidation => _totalFailedValidation;
    private long _totalFailedValidation;

    public AlertForwardingService(
        IGridApiClient gridClient,
        EnrollmentService enrollmentService,
        IServiceScopeFactory scopeFactory,
        ILogger<AlertForwardingService> logger)
    {
        _gridClient = gridClient;
        _enrollmentService = enrollmentService;
        _scopeFactory = scopeFactory;
        _logger = logger;
        _channel = Channel.CreateBounded<AlertIngestRequest>(
            new BoundedChannelOptions(QueueCapacity)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
            });
    }

    /// <inheritdoc />
    public bool TryEnqueue(AlertIngestRequest request)
    {
        if (_channel.Writer.TryWrite(request))
            return true;

        _logger.LogWarning("Alert forwarding queue full, oldest alert dropped. ClientMessageId={Id}",
            request.ClientMessageId);
        return false;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Wait for enrollment before attempting to forward
        if (!_gridClient.IsEnrolled)
        {
            _logger.LogInformation("AlertForwardingService: waiting for enrollment...");
            try
            {
                await Task.WhenAny(_enrollmentService.EnrolledTask, Task.Delay(Timeout.Infinite, stoppingToken))
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) { return; }

            if (!_gridClient.IsEnrolled)
            {
                _logger.LogWarning("AlertForwardingService: enrollment did not complete, forwarding disabled");
                return;
            }
        }

        _logger.LogInformation("AlertForwardingService started — forwarding alerts to GRID");

        // Run channel consumer and retry worker in parallel
        var consumerTask = ConsumeChannelAsync(stoppingToken);
        var retryTask = RetryPendingAsync(stoppingToken);

        await Task.WhenAll(consumerTask, retryTask).ConfigureAwait(false);
    }

    private async Task ConsumeChannelAsync(CancellationToken ct)
    {
        await foreach (var request in _channel.Reader.ReadAllAsync(ct))
        {
            await SendWithFallbackAsync(request, ct).ConfigureAwait(false);
        }
    }

    private async Task RetryPendingAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(RetryPollInterval, ct).ConfigureAwait(false);
                await RetryDueUploadsAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "RetryPendingAsync error");
            }
        }
    }

    private async Task SendWithFallbackAsync(AlertIngestRequest request, CancellationToken ct)
    {
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(SendTimeout);

            var response = await _gridClient.SendAlertAsync(request, timeoutCts.Token).ConfigureAwait(false);
            Interlocked.Increment(ref _totalForwarded);
            _logger.LogInformation("Alert forwarded to GRID: {AlertId} status={Status} clientMsgId={ClientId}",
                response.AlertId, response.Status, request.ClientMessageId);
        }
        catch (HttpRequestException ex) when (IsValidationError(ex))
        {
            Interlocked.Increment(ref _totalFailedValidation);
            _logger.LogCritical("Alert rejected by GRID (validation error, will NOT retry): {Error} clientMsgId={ClientId}",
                ex.Message, request.ClientMessageId);
            await PersistFailedAsync(request, PendingUploadStatus.FailedValidation, ex.Message, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            if (ct.IsCancellationRequested) return;

            _logger.LogWarning("Alert send failed (will retry): {Error} clientMsgId={ClientId}",
                ex.Message, request.ClientMessageId);
            await PersistFailedAsync(request, PendingUploadStatus.Pending, ex.Message, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            if (ct.IsCancellationRequested) return;
            _logger.LogError(ex, "Unexpected error forwarding alert clientMsgId={ClientId}", request.ClientMessageId);
            await PersistFailedAsync(request, PendingUploadStatus.Pending, ex.Message, ct).ConfigureAwait(false);
        }
    }

    private async Task PersistFailedAsync(AlertIngestRequest request, PendingUploadStatus status, string errorMessage, CancellationToken ct)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AgentDbContext>();

            // Check if already persisted (idempotent)
            bool exists = await db.PendingAlertUploads
                .AnyAsync(p => p.ClientMessageId == request.ClientMessageId, ct).ConfigureAwait(false);
            if (exists) return;

            var pending = new PendingAlertUpload
            {
                ClientMessageId = request.ClientMessageId,
                SerializedPayload = JsonSerializer.Serialize(request, JsonOptions),
                FirstAttemptAt = DateTime.UtcNow,
                NextRetryAt = DateTime.UtcNow.AddSeconds(1),
                AttemptCount = 1,
                Status = status,
                LastErrorMessage = errorMessage.Length > 2000 ? errorMessage[..2000] : errorMessage,
            };

            db.PendingAlertUploads.Add(pending);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist pending alert upload");
        }
    }

    private async Task RetryDueUploadsAsync(CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AgentDbContext>();

        var dueUploads = await db.PendingAlertUploads
            .Where(p => p.Status == PendingUploadStatus.Pending && p.NextRetryAt <= DateTime.UtcNow)
            .OrderBy(p => p.NextRetryAt)
            .Take(20)
            .ToListAsync(ct).ConfigureAwait(false);

        foreach (var pending in dueUploads)
        {
            try
            {
                var request = JsonSerializer.Deserialize<AlertIngestRequest>(pending.SerializedPayload, JsonOptions);
                if (request is null)
                {
                    pending.Status = PendingUploadStatus.FailedValidation;
                    pending.LastErrorMessage = "Failed to deserialize payload";
                    await db.SaveChangesAsync(ct).ConfigureAwait(false);
                    continue;
                }

                pending.Status = PendingUploadStatus.Uploading;
                await db.SaveChangesAsync(ct).ConfigureAwait(false);

                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(SendTimeout);

                var response = await _gridClient.SendAlertAsync(request, timeoutCts.Token).ConfigureAwait(false);

                pending.Status = PendingUploadStatus.Uploaded;
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
                Interlocked.Increment(ref _totalForwarded);

                _logger.LogInformation("Retry succeeded: {AlertId} status={Status} attempt={Attempt}",
                    response.AlertId, response.Status, pending.AttemptCount);
            }
            catch (HttpRequestException ex) when (IsValidationError(ex))
            {
                pending.Status = PendingUploadStatus.FailedValidation;
                pending.LastErrorMessage = ex.Message.Length > 2000 ? ex.Message[..2000] : ex.Message;
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
                Interlocked.Increment(ref _totalFailedValidation);

                _logger.LogCritical("Retry rejected (validation, stopping): {Error}", ex.Message);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                if (ct.IsCancellationRequested) return;

                pending.AttemptCount++;
                int backoffSeconds = Math.Min((int)Math.Pow(2, pending.AttemptCount), (int)MaxRetryInterval.TotalSeconds);
                pending.NextRetryAt = DateTime.UtcNow.AddSeconds(backoffSeconds);
                pending.Status = PendingUploadStatus.Pending;
                pending.LastErrorMessage = ex.Message.Length > 2000 ? ex.Message[..2000] : ex.Message;
                await db.SaveChangesAsync(ct).ConfigureAwait(false);

                _logger.LogWarning("Retry failed (attempt {Attempt}, next in {Backoff}s): {Error}",
                    pending.AttemptCount, backoffSeconds, ex.Message);
            }
        }
    }

    private static bool IsValidationError(HttpRequestException ex)
    {
        return ex.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity;
    }
}
