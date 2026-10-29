using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using RansomGuard.Agent.Core.Communication;
using RansomGuard.Agent.Core.Communication.Models;
using RansomGuard.Agent.Core.Persistence.Entities;
using RansomGuard.Agent.Core.Persistence.Repositories;
using RansomGuard.Agent.Core.Security.Cryptography;

namespace RansomGuard.Agent.Service;

/// <summary>
/// Hands the workstation's signed audit chain to the GRID, so the console can show it.
/// </summary>
/// <remarks>
/// <para>
/// Until 2026-10-27 nothing did this. The agent chained and signed every entry correctly, the
/// GRID had an endpoint waiting and the console had a page for it, and the two halves were never
/// joined: <c>SendAuditLogBatchAsync</c> existed on the client and had no caller anywhere. The
/// owner's Audit log page read "No entries found" after a real detection, a real suspension and
/// a real Resume, which is the worst way for a gap like this to be found.
/// </para>
/// <para>
/// Entries go in sequence order, oldest first, in batches. An entry is marked uploaded only
/// after the GRID has said it accepted it, so a workstation that loses the network re-sends from
/// where it stopped rather than skipping a stretch -- and the GRID refuses a gap, which is the
/// point of the chain.
/// </para>
/// </remarks>
public sealed class AuditLogUploadService : BackgroundService
{
    /// <summary>Between two looks at what is waiting.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    /// <summary>Entries per request. The GRID's schema refuses more than 500.</summary>
    public const int BatchSize = 100;

    private readonly IGridApiClient _grid;
    private readonly IServiceScopeFactory _scopes;
    private readonly IAuditUploadSigner _signer;
    private readonly EnrollmentService? _enrollment;
    private readonly ILogger<AuditLogUploadService> _logger;
    private readonly TimeProvider _time;

    /// <summary>Creates the service.</summary>
    public AuditLogUploadService(
        IGridApiClient grid,
        IServiceScopeFactory scopes,
        IAuditUploadSigner signer,
        ILogger<AuditLogUploadService> logger,
        EnrollmentService? enrollment = null,
        TimeProvider? time = null)
    {
        _grid = grid;
        _scopes = scopes;
        _signer = signer;
        _logger = logger;
        _enrollment = enrollment;
        _time = time ?? TimeProvider.System;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();
        await _scopes.WaitForDatabaseAsync(stoppingToken);

        if (!_grid.IsEnrolled && _enrollment is not null)
        {
            try
            {
                await Task.WhenAny(_enrollment.EnrolledTask, Task.Delay(Timeout.Infinite, stoppingToken))
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) { return; }
        }

        if (!_grid.IsEnrolled)
        {
            _logger.LogWarning("Audit upload disabled: this workstation is not enrolled");
            return;
        }

        _logger.LogInformation("Audit log upload started (every {Interval})", Interval);

        try
        {
            using var timer = new PeriodicTimer(Interval, _time);
            do
            {
                try
                {
                    int sent = await UploadOnceAsync(stoppingToken).ConfigureAwait(false);
                    if (sent > 0)
                    {
                        _logger.LogInformation("{Count} audit entries handed to the GRID", sent);
                    }
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    // The entries stay unmarked and go again on the next tick. Nothing is lost
                    // by a failed upload; the chain is on the workstation either way.
                    _logger.LogError(ex, "Audit upload failed; the entries wait and are sent again in {Interval}", Interval);
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
    }

    /// <summary>
    /// Sends one batch. Returns how many the GRID accepted.
    /// </summary>
    public async Task<int> UploadOnceAsync(CancellationToken ct)
    {
        await using AsyncServiceScope scope = _scopes.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IAuditLogRepository>();

        IReadOnlyList<AuditLog> waiting = await repo.GetPendingUploadAsync(BatchSize, ct).ConfigureAwait(false);
        if (waiting.Count == 0)
        {
            return 0;
        }

        var entries = new List<AuditLogEntryInput>(waiting.Count);
        foreach (AuditLog entry in waiting)
        {
            // One string: signed, then parsed back into what is sent. See AuditUploadPayload.
            string canonical = AuditUploadPayload.CanonicalJson(entry);
            entries.Add(new AuditLogEntryInput
            {
                SequenceNumber = entry.Sequence,
                Payload = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(canonical)!,
                Ed25519SignatureBase64 = _signer.SignUpload(System.Text.Encoding.UTF8.GetBytes(canonical)),
                SigningKeyId = _signer.KeyId,
                Timestamp = entry.CreatedAt,
            });
        }

        AuditLogBatchResponse response = await _grid
            .SendAuditLogBatchAsync(new AuditLogBatchRequest { Entries = entries }, ct)
            .ConfigureAwait(false);

        if (response.RejectedCount > 0)
        {
            // Said out loud and in full: a rejected entry is the console showing a chain that is
            // not the workstation's, and the reasons are the only way to tell which half is wrong.
            _logger.LogError(
                "The GRID refused {Rejected} of {Total} audit entries: {Reasons}",
                response.RejectedCount, entries.Count, string.Join("; ", response.RejectedReasons));
        }

        if (response.AcceptedCount <= 0)
        {
            return 0;
        }

        // Only the accepted prefix is marked: the GRID takes them in order and stops at a gap,
        // so marking the whole batch would skip whatever it refused.
        Guid[] accepted = waiting.Take(response.AcceptedCount).Select(e => e.Id).ToArray();
        await repo.MarkUploadedAsync(accepted, _time.GetUtcNow().UtcDateTime, ct).ConfigureAwait(false);

        return response.AcceptedCount;
    }
}
