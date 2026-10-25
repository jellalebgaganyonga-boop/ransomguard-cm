using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RansomGuard.Agent.Core.Detection.Genealogy;
using RansomGuard.Agent.Core.Detection.Sentinel.Etw;
using RansomGuard.Agent.Core.Diagnostics;

namespace RansomGuard.Agent.Service;

/// <summary>
/// Owns GENEALOGY's capability and its state.
///
/// <para>
/// GENEALOGY has no detector of its own -- it enriches other modules' alerts with the chain of
/// processes behind a write. Until now nothing owned it at runtime: the registry seeded it
/// <c>degraded / attribution_unreliable</c> and nothing ever published anything else, because its
/// only source was the Restart Manager, which names nobody once the writer has closed its handle
/// (0/120 attributed, AttributionBench).
/// </para>
///
/// <para>
/// This service gives it the same source SENTINEL and ENTROPY use: it subscribes
/// <see cref="ProcessAncestryStore"/> to the kernel FileIO session's fan-out, so the ancestry of a
/// process that writes a watched file is captured while that process is still alive. Then it
/// publishes what the module can actually do, re-checked while the agent runs:
/// </para>
///
/// <list type="bullet">
///   <item><c>active</c> -- the session is running and the capture loop is working;</item>
///   <item><c>degraded / attribution_unreliable</c> -- it is not, so a tree can only be built for a
///   writer that happens to still be alive, which for ransomware it will not be.</item>
/// </list>
/// </summary>
public sealed class GenealogyMonitor : BackgroundService
{
    /// <summary>How often the capability is re-checked.</summary>
    public static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(30);

    private readonly ProcessAncestryStore _ancestry;
    private readonly FileIoFanout? _fanout;
    private readonly EtwFileIoSession? _etwSession;
    private readonly IModuleStateRegistry _moduleState;
    private readonly ILogger<GenealogyMonitor> _logger;
    private readonly TimeProvider _time;
    private bool? _lastPublishedHealthy;

    /// <summary>Creates the monitor.</summary>
    /// <param name="ancestry">The ancestry store this service feeds and owns.</param>
    /// <param name="moduleState">Runtime module-state registry.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="fanout">The session fan-out; null leaves the store unfed and the module degraded.</param>
    /// <param name="etwSession">The session, read for whether it is running.</param>
    /// <param name="time">Clock of the re-check loop.</param>
    public GenealogyMonitor(
        ProcessAncestryStore ancestry,
        IModuleStateRegistry moduleState,
        ILogger<GenealogyMonitor> logger,
        FileIoFanout? fanout = null,
        EtwFileIoSession? etwSession = null,
        TimeProvider? time = null)
    {
        _ancestry = ancestry;
        _moduleState = moduleState;
        _logger = logger;
        _fanout = fanout;
        _etwSession = etwSession;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>
    /// Whether GENEALOGY can actually build the chain behind a write that has finished: the kernel
    /// session is running and the capture loop is alive and capturing.
    /// </summary>
    private bool CanBuildAncestry =>
        _fanout is not null
        && _etwSession?.State == EtwSessionState.Running
        && _ancestry.IsHealthy;

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            _ancestry.Start();

            if (_fanout is not null)
            {
                // Every write on a watched file tells the store to capture that writer's ancestry
                // before it exits. The callback does one dictionary probe and one non-blocking
                // channel write: it runs on the session thread and must never block it.
                _fanout.Subscribe(record =>
                {
                    if (EtwCanaryFilter.IsWrite(record))
                    {
                        _ancestry.OnWriterSeen(record.ProcessId);
                    }
                });
            }

            Publish();

            // The session can be stopped from outside and restart; the state follows it rather
            // than staying at whatever the first check saw.
            using PeriodicTimer timer = new(CheckInterval, _time);
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                Publish();
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // normal shutdown
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GENEALOGY monitor stopped unexpectedly");
            _moduleState.Publish(ModuleCode.Genealogy, ModuleState.Degraded, ModuleReasonCode.AttributionUnreliable);
        }
    }

    private void Publish()
    {
        bool healthy = CanBuildAncestry;
        if (_lastPublishedHealthy == healthy)
        {
            return;
        }

        _lastPublishedHealthy = healthy;
        if (healthy)
        {
            _logger.LogInformation(
                "GENEALOGY active: the ancestry of a process that writes a watched file is captured while it runs, "
                + "so an alert shows the chain behind the write ({Captured} captured, {Missed} already gone).",
                _ancestry.Captured_, _ancestry.Missed);
            _moduleState.Publish(ModuleCode.Genealogy, ModuleState.Active);
            return;
        }

        _logger.LogWarning(
            "GENEALOGY degraded (attribution_unreliable): without the kernel FileIO session the chain can only be "
            + "built for a writer still holding the file, which ransomware does not do. The session needs "
            + "administrator rights and a free system-logger slot.");
        _moduleState.Publish(ModuleCode.Genealogy, ModuleState.Degraded, ModuleReasonCode.AttributionUnreliable);
    }
}
