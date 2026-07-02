using System.Runtime.Versioning;
using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RansomGuard.Agent.Core.Configuration;
using RansomGuard.Agent.Core.Detection.ExfilWatch;
using RansomGuard.Agent.Core.Detection.ExfilWatch.Actions;
using RansomGuard.Agent.Core.Detection.ExfilWatch.Models;
using RansomGuard.Agent.Core.Detection.ExfilWatch.Rules;
using RansomGuard.Agent.Core.Communication;
using RansomGuard.Agent.Core.Security.RateLimiting;

namespace RansomGuard.Agent.Service;

/// <summary>
/// BackgroundService that runs the EXFIL WATCH network monitoring pipeline.
/// Architecture: ETW Capture -> Channel -> DataVolumeTracker -> DetectionRules -> Alerts.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ExfilWatchMonitor : BackgroundService
{
    private const int EventChannelCapacity = 10_000;

    private readonly ILogger<ExfilWatchMonitor> _logger;
    private readonly INetworkActivityMonitor _networkMonitor;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IAlertForwardingQueue? _alertQueue;
    private readonly AgentConfiguration _config;
    private readonly Channel<NetworkEvent> _eventChannel;
    private long _totalEventsProcessed;

    /// <summary>Total events processed since start.</summary>
    public long TotalEventsProcessed => Interlocked.Read(ref _totalEventsProcessed);

    /// <summary>Initializes the EXFIL WATCH monitor.</summary>
    public ExfilWatchMonitor(
        ILogger<ExfilWatchMonitor> logger,
        INetworkActivityMonitor networkMonitor,
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<AgentConfiguration> config,
        IAlertForwardingQueue? alertQueue = null)
    {
        _logger = logger;
        _networkMonitor = networkMonitor;
        _scopeFactory = scopeFactory;
        _alertQueue = alertQueue;
        _config = config.CurrentValue;
        _eventChannel = Channel.CreateBounded<NetworkEvent>(
            new BoundedChannelOptions(EventChannelCapacity)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true
            });
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_config.ExfilWatch is null || !_config.ExfilWatch.Enabled)
        {
            _logger.LogInformation("EXFIL WATCH module is disabled");
            return;
        }

        _logger.LogInformation("EXFIL WATCH monitor starting (learning phase: {Days} days)",
            _config.ExfilWatch.LearningPhaseDays);

        // Start ETW capture
        _networkMonitor.Start(_eventChannel.Writer, stoppingToken);

        // Start consumer
        Task consumerTask = ConsumeEventsAsync(stoppingToken);

        _logger.LogInformation("EXFIL WATCH monitor active");

        // Heartbeat
        using var heartbeat = new PeriodicTimer(TimeSpan.FromSeconds(60));
        try
        {
            while (await heartbeat.WaitForNextTickAsync(stoppingToken))
            {
                _logger.LogInformation(
                    "EXFIL WATCH heartbeat | Events processed: {Processed} | ETW captured: {Captured}",
                    TotalEventsProcessed, _networkMonitor.TotalEventsCaptured);
            }
        }
        catch (OperationCanceledException) { }

        _eventChannel.Writer.TryComplete();
        await consumerTask;
        await _networkMonitor.StopAsync();
    }

    private async Task ConsumeEventsAsync(CancellationToken ct)
    {
        await foreach (var evt in _eventChannel.Reader.ReadAllAsync(ct))
        {
            try
            {
                Interlocked.Increment(ref _totalEventsProcessed);

                await using var scope = _scopeFactory.CreateAsyncScope();
                var tracker = scope.ServiceProvider.GetService<IDataVolumeTracker>();

                // Feed event to data volume tracker
                tracker?.RecordEvent(evt);

                // Evaluate detection rules and dispatch actions for findings >= Medium
                if (tracker is not null)
                {
                    var ruleEngine = scope.ServiceProvider.GetService<ExfilRuleEngine>();
                    var findings = ruleEngine?.Evaluate(evt, tracker, _config.ExfilWatch!);

                    if (findings is { Count: > 0 })
                    {
                        var actionEngine = scope.ServiceProvider.GetService<IExfilActionEngine>();
                        if (actionEngine is not null)
                        {
                            foreach (var finding in findings.Where(f => f.Severity >= ExfilSeverity.Medium))
                            {
                                // Fire-and-forget for non-blocking pipeline
                                _ = Task.Run(() => actionEngine.ExecuteAsync(finding, ct), ct);

                                // Forward to GRID in parallel
                                _alertQueue?.TryEnqueue(AlertMapper.FromExfilFinding(finding));
                            }
                        }
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "EXFIL WATCH: Error processing event from {Process}:{PID}",
                    evt.ProcessName, evt.ProcessId);
            }
        }
    }
}
