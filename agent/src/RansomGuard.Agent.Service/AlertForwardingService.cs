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

    private readonly Channel<OutboundMessage> _channel;
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

    private long _lastUploadOkTicks; // 0 = never; UTC ticks otherwise
    private long _lastUploadOkTimestamp; // 0 = never; TimeProvider (monotonic) timestamp otherwise
    private readonly TimeProvider _time;
    private long _lastDetectionTicks; // 0 = never; UTC ticks otherwise
    private volatile string? _lastUploadErrorCode;

    /// <summary>
    /// When the agent last detected something locally (an alert entered the queue),
    /// UTC. Null if nothing has been detected since startup.
    /// </summary>
    public DateTime? LastDetectionAtUtc
    {
        get
        {
            long ticks = Interlocked.Read(ref _lastDetectionTicks);
            return ticks == 0 ? null : new DateTime(ticks, DateTimeKind.Utc);
        }
    }

    /// <summary>When the last upload succeeded, UTC. Null if none since startup.</summary>
    public DateTime? LastUploadOkAtUtc
    {
        get
        {
            long ticks = Interlocked.Read(ref _lastUploadOkTicks);
            return ticks == 0 ? null : new DateTime(ticks, DateTimeKind.Utc);
        }
    }

    /// <summary>
    /// Stable code of the last upload failure (e.g. HTTP_422, NETWORK_UNREACHABLE,
    /// TIMEOUT). Never a raw exception message. Null if the last upload succeeded.
    /// </summary>
    public string? LastUploadErrorCode => _lastUploadErrorCode;

    /// <summary>
    /// Seconds since the last successful upload, on the monotonic clock (Stopwatch):
    /// a wall-clock change or a skewed clock cannot make it lie. Null if none since startup.
    /// </summary>
    public long? LastUploadOkAgeSeconds
    {
        get
        {
            long stamp = Interlocked.Read(ref _lastUploadOkTimestamp);
            return stamp == 0 ? null : (long)_time.GetElapsedTime(stamp).TotalSeconds;
        }
    }

    public AlertForwardingService(
        IGridApiClient gridClient,
        EnrollmentService enrollmentService,
        IServiceScopeFactory scopeFactory,
        ILogger<AlertForwardingService> logger,
        TimeProvider? time = null)
    {
        _time = time ?? TimeProvider.System;
        _gridClient = gridClient;
        _enrollmentService = enrollmentService;
        _scopeFactory = scopeFactory;
        _logger = logger;
        _channel = Channel.CreateBounded<OutboundMessage>(
            new BoundedChannelOptions(QueueCapacity)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
            });
    }

    /// <inheritdoc />
    public bool TryEnqueue(AlertIngestRequest request)
    {
        // An alert entering the queue is a local detection, whether or not it
        // later reaches the GRID. Record the moment for the heartbeat.
        Interlocked.Exchange(ref _lastDetectionTicks, DateTime.UtcNow.Ticks);

        if (_channel.Writer.TryWrite(new AlertMessage(request)))
            return true;

        _logger.LogWarning("Alert forwarding queue full, oldest alert dropped. ClientMessageId={Id}",
            request.ClientMessageId);
        return false;
    }

    /// <inheritdoc />
    public bool TryEnqueueAttribution(string clientMessageId, AttributionComplementRequest complement)
    {
        if (_channel.Writer.TryWrite(new AttributionMessage(clientMessageId, complement)))
            return true;

        _logger.LogWarning("Alert forwarding queue full, attribution complement dropped. ClientMessageId={Id}",
            clientMessageId);
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
        await foreach (OutboundMessage message in _channel.Reader.ReadAllAsync(ct))
        {
            await SendWithFallbackAsync(message, ct).ConfigureAwait(false);
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

    private async Task SendWithFallbackAsync(OutboundMessage message, CancellationToken ct)
    {
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(SendTimeout);

            string outcome = await SendAsync(message, timeoutCts.Token).ConfigureAwait(false);
            _logger.LogInformation("{Kind} forwarded to GRID: {Outcome} clientMsgId={ClientId}",
                message.Kind, outcome, message.ClientMessageId);
        }
        catch (HttpRequestException ex) when (IsValidationError(ex))
        {
            if (message.Kind == PendingUploadKind.Alert)
            {
                Interlocked.Increment(ref _totalFailedValidation);
                MarkUploadError(ex);
            }

            _logger.LogCritical("{Kind} rejected by GRID (validation error, will NOT retry): {Error} clientMsgId={ClientId}",
                message.Kind, ex.Message, message.ClientMessageId);
            await PersistFailedAsync(message, PendingUploadStatus.FailedValidation, ex.Message, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            if (ct.IsCancellationRequested) return;

            // A complement answered 404 is retried too: its alert may still be waiting here.
            if (message.Kind == PendingUploadKind.Alert) MarkUploadError(ex);
            _logger.LogWarning("{Kind} send failed (will retry): {Error} clientMsgId={ClientId}",
                message.Kind, ex.Message, message.ClientMessageId);
            await PersistFailedAsync(message, PendingUploadStatus.Pending, ex.Message, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            if (ct.IsCancellationRequested) return;
            if (message.Kind == PendingUploadKind.Alert) MarkUploadError(ex);
            _logger.LogError(ex, "Unexpected error forwarding {Kind} clientMsgId={ClientId}", message.Kind, message.ClientMessageId);
            await PersistFailedAsync(message, PendingUploadStatus.Pending, ex.Message, ct).ConfigureAwait(false);
        }
    }

    // Sends one message; the transmission health of the heartbeat (last upload OK, error code)
    // follows the alerts only: a complement waiting for its alert is not a transmission failure.
    private async Task<string> SendAsync(OutboundMessage message, CancellationToken ct)
    {
        switch (message)
        {
            case AlertMessage alert:
                AlertIngestResponse response = await _gridClient.SendAlertAsync(alert.Request, ct).ConfigureAwait(false);
                Interlocked.Increment(ref _totalForwarded);
                MarkUploadOk();
                return $"{response.AlertId} status={response.Status}";
            case AttributionMessage attribution:
                await _gridClient.SendAttributionAsync(attribution.ClientMessageId, attribution.Complement, ct).ConfigureAwait(false);
                return $"attribution {attribution.Complement.State}";
            default:
                throw new InvalidOperationException($"Unknown outbound message {message.GetType().Name}");
        }
    }

    private static string Serialize(OutboundMessage message) => message switch
    {
        AlertMessage alert => JsonSerializer.Serialize(alert.Request, JsonOptions),
        AttributionMessage attribution => JsonSerializer.Serialize(attribution.Complement, JsonOptions),
        _ => throw new InvalidOperationException($"Unknown outbound message {message.GetType().Name}"),
    };

    private static OutboundMessage? Deserialize(PendingAlertUpload pending) => pending.Kind switch
    {
        PendingUploadKind.Attribution => JsonSerializer.Deserialize<AttributionComplementRequest>(pending.SerializedPayload, JsonOptions)
            is { } complement ? new AttributionMessage(pending.ClientMessageId, complement) : null,
        _ => JsonSerializer.Deserialize<AlertIngestRequest>(pending.SerializedPayload, JsonOptions)
            is { } request ? new AlertMessage(request) : null,
    };

    private async Task PersistFailedAsync(OutboundMessage message, PendingUploadStatus status, string errorMessage, CancellationToken ct)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AgentDbContext>();

            // Check if already persisted (idempotent): one alert and one complement per ClientMessageId.
            bool exists = await db.PendingAlertUploads
                .AnyAsync(p => p.ClientMessageId == message.ClientMessageId && p.Kind == message.Kind, ct).ConfigureAwait(false);
            if (exists) return;

            var pending = new PendingAlertUpload
            {
                ClientMessageId = message.ClientMessageId,
                Kind = message.Kind,
                SerializedPayload = Serialize(message),
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
            _logger.LogError(ex, "Failed to persist pending {Kind} upload", message.Kind);
        }
    }

    private async Task RetryDueUploadsAsync(CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AgentDbContext>();

        var dueUploads = await db.PendingAlertUploads
            .Where(p => p.Status == PendingUploadStatus.Pending && p.NextRetryAt <= DateTime.UtcNow)
            .OrderBy(p => p.NextRetryAt)
            .ThenBy(p => p.Kind) // an alert before its complement when both are due
            .Take(20)
            .ToListAsync(ct).ConfigureAwait(false);

        foreach (var pending in dueUploads)
        {
            try
            {
                OutboundMessage? message = Deserialize(pending);
                if (message is null)
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

                string outcome = await SendAsync(message, timeoutCts.Token).ConfigureAwait(false);

                pending.Status = PendingUploadStatus.Uploaded;
                await db.SaveChangesAsync(ct).ConfigureAwait(false);

                _logger.LogInformation("Retry succeeded: {Kind} {Outcome} attempt={Attempt}",
                    pending.Kind, outcome, pending.AttemptCount);
            }
            catch (HttpRequestException ex) when (IsValidationError(ex))
            {
                pending.Status = PendingUploadStatus.FailedValidation;
                pending.LastErrorMessage = ex.Message.Length > 2000 ? ex.Message[..2000] : ex.Message;
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
                if (pending.Kind == PendingUploadKind.Alert)
                {
                    Interlocked.Increment(ref _totalFailedValidation);
                    MarkUploadError(ex);
                }

                _logger.LogCritical("Retry rejected (validation, stopping): {Kind} {Error}", pending.Kind, ex.Message);
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
                if (pending.Kind == PendingUploadKind.Alert) MarkUploadError(ex);

                _logger.LogWarning("Retry failed ({Kind}, attempt {Attempt}, next in {Backoff}s): {Error}",
                    pending.Kind, pending.AttemptCount, backoffSeconds, ex.Message);
            }
        }
    }

    internal void MarkUploadOk()
    {
        Interlocked.Exchange(ref _lastUploadOkTicks, DateTime.UtcNow.Ticks);
        // A real timestamp is never 0 (Stopwatch counts from boot), so 0 keeps meaning "never".
        Interlocked.Exchange(ref _lastUploadOkTimestamp, Math.Max(1, _time.GetTimestamp()));
        _lastUploadErrorCode = null;
    }

    private void MarkUploadError(Exception ex) => _lastUploadErrorCode = ClassifyUploadError(ex);

    /// <summary>
    /// Maps an upload exception to a stable, non-sensitive code for the heartbeat.
    /// Never returns the raw message (it can leak paths, hostnames or tokens).
    /// </summary>
    private static string ClassifyUploadError(Exception ex) => ex switch
    {
        HttpRequestException { StatusCode: { } code } => $"HTTP_{(int)code}",
        HttpRequestException => "NETWORK_UNREACHABLE",
        TaskCanceledException or OperationCanceledException => "TIMEOUT",
        _ => "UNKNOWN",
    };

    private static bool IsValidationError(HttpRequestException ex)
    {
        return ex.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity;
    }
}

/// <summary>What the forwarding queue carries: an alert, or the attribution complement of an alert.</summary>
internal abstract record OutboundMessage(string ClientMessageId, PendingUploadKind Kind);

/// <summary>An alert.</summary>
internal sealed record AlertMessage(AlertIngestRequest Request)
    : OutboundMessage(Request.ClientMessageId, PendingUploadKind.Alert);

/// <summary>The attribution complement of the alert of the same ClientMessageId.</summary>
internal sealed record AttributionMessage(string Id, AttributionComplementRequest Complement)
    : OutboundMessage(Id, PendingUploadKind.Attribution);
