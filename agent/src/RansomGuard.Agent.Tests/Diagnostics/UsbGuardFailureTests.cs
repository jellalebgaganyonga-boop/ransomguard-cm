using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RansomGuard.Agent.Core.Configuration;
using RansomGuard.Agent.Core.Detection;
using RansomGuard.Agent.Core.Detection.UsbGuard;
using RansomGuard.Agent.Core.Detection.UsbGuard.Models;
using RansomGuard.Agent.Core.Detection.UsbGuard.Wmi;
using RansomGuard.Agent.Core.Diagnostics;
using RansomGuard.Agent.Core.Persistence;
using RansomGuard.Agent.Service;
using Shouldly;

namespace RansomGuard.Agent.Tests.Diagnostics;

/// <summary>
/// USB GUARD going down leaves the same three traces as the other detectors: registry state,
/// a Critical log, a signed audit entry. Before, it only published the state and an Error log.
/// </summary>
public sealed class UsbGuardFailureTests : IDisposable
{
    private readonly MonitorHarness _h = new();
    private readonly RecordingLogger<ModuleFailureReporter> _reporterLog = new();

    public void Dispose() => _h.Dispose();

    private UsbDeviceMonitor Usb(IWmiEventSubscriber wmi) => new(
        NullLogger<UsbDeviceMonitor>.Instance,
        wmi,
        new FileEventDeduplicator(50),
        new BootableUsbDetector(NullLogger<BootableUsbDetector>.Instance),
        _h.ScopeFactory,
        MonitorHarness.Config([], usbGuard: new UsbGuardOptions()),
        _h.Registry,
        new ModuleFailureReporter(_h.Registry, _h.ScopeFactory, _reporterLog));

    private async Task<List<string>> AuditActionsAsync()
    {
        using IServiceScope scope = _h.Provider.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AgentDbContext>().AuditLogs
            .Select(a => a.Action).ToListAsync();
    }

    [Fact(Timeout = 30_000)]
    public async Task A_usb_consumer_that_stops_on_its_own_reads_stopped_unexpectedly_with_audit_and_critical()
    {
        // The WMI capture ends at once: the consumer sees the end of the channel and returns,
        // a detector gone blind without throwing.
        var wmi = new Mock<IWmiEventSubscriber>();
        wmi.Setup(w => w.Start(It.IsAny<ChannelWriter<UsbConnectionEvent>>(), It.IsAny<CancellationToken>()))
            .Callback((ChannelWriter<UsbConnectionEvent> writer, CancellationToken _) => writer.TryComplete());
        using UsbDeviceMonitor monitor = Usb(wmi.Object);

        await monitor.StartAsync(CancellationToken.None);
        await Eventually.HoldsAsync(
            () => _h.State(ModuleCode.UsbGuard).ReasonCode == ModuleReasonCode.StoppedUnexpectedly,
            "USB GUARD publishes stopped_unexpectedly");
        await monitor.StopAsync(CancellationToken.None);

        _h.State(ModuleCode.UsbGuard).State.ShouldBe(ModuleState.Inactive);
        _reporterLog.Entries.ShouldContain(e => e.Level == LogLevel.Critical);
        (await AuditActionsAsync()).ShouldContain(ModuleFailureReporter.ModuleStoppedAction);
        wmi.Verify(w => w.StopAsync(), Times.Once); // the capture is stopped, nothing left running
    }

    [Fact(Timeout = 30_000)]
    public async Task A_usb_capture_that_cannot_start_reads_init_failed_with_audit_and_critical()
    {
        var wmi = new Mock<IWmiEventSubscriber>();
        wmi.Setup(w => w.Start(It.IsAny<ChannelWriter<UsbConnectionEvent>>(), It.IsAny<CancellationToken>()))
            .Throws(new InvalidOperationException("WMI unavailable (test)"));
        using UsbDeviceMonitor monitor = Usb(wmi.Object);

        await monitor.StartAsync(CancellationToken.None);
        await Eventually.HoldsAsync(
            () => _h.State(ModuleCode.UsbGuard).ReasonCode == ModuleReasonCode.InitFailed,
            "USB GUARD publishes init_failed");
        await monitor.StopAsync(CancellationToken.None);

        _reporterLog.Entries.ShouldContain(e => e.Level == LogLevel.Critical);
        (await AuditActionsAsync()).ShouldContain(ModuleFailureReporter.ModuleStoppedAction);
        wmi.Verify(w => w.StopAsync(), Times.Never); // never started, nothing to stop
    }
}
