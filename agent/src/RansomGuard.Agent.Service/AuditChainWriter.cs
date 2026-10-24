using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using RansomGuard.Agent.Core.Persistence.Repositories;

namespace RansomGuard.Agent.Service;

/// <summary>
/// Chains the recorded audit entries in the background (AGT-AUD-002). Detections and actions
/// record with one INSERT and go on; this writer, alone on the chain, links them in insertion
/// order by batches.
/// </summary>
/// <remarks>
/// The wake-up channel carries signals, not entries: the entries are already in the database,
/// so a coalesced signal loses nothing. A periodic pass covers a missed signal. A failed pass
/// leaves the entries waiting (never dropped), is logged Critical and counted for the heartbeat,
/// and is retried.
/// </remarks>
public sealed class AuditChainWriter : BackgroundService, IAuditChainWriter
{
    private static readonly TimeSpan SafetyPass = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly AuditHealth _health;
    private readonly ILogger<AuditChainWriter> _logger;
    private readonly Channel<bool> _wakeUp = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true });

    /// <summary>Creates the writer.</summary>
    public AuditChainWriter(IServiceScopeFactory scopeFactory, AuditHealth health, ILogger<AuditChainWriter> logger)
    {
        _scopeFactory = scopeFactory;
        _health = health;
        _logger = logger;
    }

    /// <inheritdoc />
    public void RequestChaining() => _wakeUp.Writer.TryWrite(true);

    /// <summary>Chains everything waiting now. Used at startup, at shutdown and by each pass.</summary>
    public async Task<int> ChainAllAsync(CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IAuditLogRepository>();
        return await repository.ChainPendingAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ChainAllAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _health.RecordFailure();
                _logger.LogCritical(ex, "Audit chain writer failed; the entries stay waiting and will be chained on retry");
                await DelayAsync(RetryDelay, stoppingToken).ConfigureAwait(false);
                continue;
            }

            // Wait for a signal, or the safety pass.
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            timeout.CancelAfter(SafetyPass);
            try
            {
                await _wakeUp.Reader.ReadAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
            {
                // Safety pass.
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <inheritdoc />
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken).ConfigureAwait(false);

        // Last pass at shutdown; whatever is still waiting is chained at the next start.
        try
        {
            await ChainAllAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Audit entries left waiting at shutdown; they are chained at the next start");
        }
    }

    private static async Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Shutdown.
        }
    }
}
