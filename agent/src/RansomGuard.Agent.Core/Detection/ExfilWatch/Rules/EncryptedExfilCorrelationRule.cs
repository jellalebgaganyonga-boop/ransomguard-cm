using System.Collections.Concurrent;
using RansomGuard.Agent.Core.Configuration;
using RansomGuard.Agent.Core.Detection.CrossModule;
using RansomGuard.Agent.Core.Detection.ExfilWatch.Models;

namespace RansomGuard.Agent.Core.Detection.ExfilWatch.Rules;

/// <summary>
/// Rule 8: Encrypted exfiltration correlation — detects double extortion pattern
/// by correlating ENTROPY signals with network exfiltration events.
/// When a process encrypts a file (EntropySignal) and then uploads data
/// matching the file size within 60 seconds, a critical alert is raised.
/// MITRE T1486 + T1041 — Data Encrypted for Impact + Exfiltration Over C2 Channel.
/// </summary>
public sealed class EncryptedExfilCorrelationRule : IExfilDetectionRule, IDisposable
{
    private const int CorrelationWindowSeconds = 60;
    private readonly ConcurrentDictionary<int, EntropySignal> _recentSignalsByPid = new();
    private readonly IDetectionEventBus _eventBus;
    private IDisposable? _subscription;

    /// <summary>Initializes the rule and subscribes to EntropySignals.</summary>
    public EncryptedExfilCorrelationRule(IDetectionEventBus eventBus)
    {
        _eventBus = eventBus;
        _subscription = _eventBus.Subscribe<EntropySignal>((signal, ct) =>
        {
            _recentSignalsByPid[signal.ProcessId] = signal;

            // Schedule cleanup after correlation window
            Task.Run(async () =>
            {
                await Task.Delay(CorrelationWindowSeconds * 1000);
                _recentSignalsByPid.TryRemove(signal.ProcessId, out _);
            });

            return Task.CompletedTask;
        });
    }

    /// <inheritdoc />
    public string RuleName => "EncryptedExfilCorrelation";

    /// <summary>The entropy alert ID from the last correlation match (for cross-linking).</summary>
    public Guid? LastCorrelatedEntropyAlertId { get; private set; }

    /// <inheritdoc />
    public ExfilFinding? Evaluate(NetworkEvent evt, IDataVolumeTracker tracker, ExfilWatchOptions options)
    {
        if (evt.EventType != NetworkEventType.TcpSend || evt.DestinationAddress is null)
            return null;

        if (!_recentSignalsByPid.TryGetValue(evt.ProcessId, out var signal))
            return null;

        double elapsedSeconds = (DateTime.UtcNow - signal.EmittedAt).TotalSeconds;
        if (elapsedSeconds > CorrelationWindowSeconds)
            return null;

        // Upload bytes must be at least 80% of encrypted file size
        if (evt.BytesSent < signal.FileSize * 0.8)
            return null;

        LastCorrelatedEntropyAlertId = signal.EntropyAlertId;

        return new ExfilFinding
        {
            RuleName = RuleName,
            Severity = ExfilSeverity.Critical,
            Description = $"DOUBLE EXTORTION: Process {evt.ProcessName} encrypted " +
                          $"{signal.FilePath} then uploaded {evt.BytesSent / 1024 / 1024} MB " +
                          $"to {evt.DestinationAddress}",
            ProcessId = evt.ProcessId,
            ProcessName = evt.ProcessName,
            Destination = evt.DestinationAddress,
            DestinationPort = evt.DestinationPort,
            BytesTransferred = evt.BytesSent,
            MitreId = "T1486+T1041"
        };
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _subscription?.Dispose();
        _subscription = null;
    }
}
