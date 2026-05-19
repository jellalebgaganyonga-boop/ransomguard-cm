using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using RansomGuard.Agent.Core.Detection.Genealogy;
using RansomGuard.Agent.Core.Detection.Sentinel;
using RansomGuard.Agent.Core.Persistence;
using Shouldly;

namespace RansomGuard.Agent.Tests.Genealogy;

/// <summary>
/// Stress tests for GenealogyEnricher and ProcessSnapshotService
/// verifying no handle leaks under repeated operation (1000 iterations).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class GenealogyEnricherStressTests : IDisposable
{
    private readonly ProcessSnapshotService _snapshotService;
    private readonly AgentDbContext _context;

    public GenealogyEnricherStressTests()
    {
        _snapshotService = new ProcessSnapshotService(
            new Mock<ILogger<ProcessSnapshotService>>().Object);

        var options = new DbContextOptionsBuilder<AgentDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        _context = new AgentDbContext(options);
        _context.Database.OpenConnection();
        _context.Database.EnsureCreated();
    }

    [Fact(Timeout = 600_000)]
    [Trait("Category", "Stress")]
    public void ProcessSnapshotService_CaptureThousandTimes_NoHandleLeak()
    {
        int pid = Environment.ProcessId;

        // Warm up
        for (int i = 0; i < 10; i++)
            _snapshotService.CaptureProcess(pid);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        int handlesBefore = Process.GetCurrentProcess().HandleCount;

        for (int i = 0; i < 1000; i++)
        {
            ProcessSnapshot? snapshot = _snapshotService.CaptureProcess(pid);
            snapshot.ShouldNotBeNull();
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        int handlesAfter = Process.GetCurrentProcess().HandleCount;
        int handleVariance = Math.Abs(handlesAfter - handlesBefore);

        handleVariance.ShouldBeLessThan(50,
            $"Handle leak: before={handlesBefore}, after={handlesAfter}, variance={handleVariance}");
    }

    [Fact(Timeout = 600_000)]
    [Trait("Category", "Stress")]
    public void ProcessSnapshotService_BuildTreeThousandTimes_NoHandleLeak()
    {
        int pid = Environment.ProcessId;

        // Warm up
        for (int i = 0; i < 5; i++)
            _snapshotService.BuildProcessTree(pid);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        int handlesBefore = Process.GetCurrentProcess().HandleCount;

        for (int i = 0; i < 1000; i++)
        {
            ProcessTree? tree = _snapshotService.BuildProcessTree(pid);
            tree.ShouldNotBeNull();
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        int handlesAfter = Process.GetCurrentProcess().HandleCount;
        int handleVariance = Math.Abs(handlesAfter - handlesBefore);

        handleVariance.ShouldBeLessThan(50,
            $"Handle leak: before={handlesBefore}, after={handlesAfter}, variance={handleVariance}");
    }

    [Fact(Timeout = 600_000)]
    [Trait("Category", "Stress")]
    public async Task GenealogyEnricher_EnrichesThousandAlerts_NoHandleLeak()
    {
        var enricher = new GenealogyEnricher(
            _snapshotService,
            new RestartManagerHelper(new Mock<ILogger<RestartManagerHelper>>().Object),
            _context,
            new Mock<ILogger<GenealogyEnricher>>().Object);

        // Warm up
        for (int i = 0; i < 5; i++)
            await enricher.EnrichAlertAsync(Guid.NewGuid(), @"C:\Windows\System32\notepad.exe");

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        int handlesBefore = Process.GetCurrentProcess().HandleCount;

        for (int i = 0; i < 1000; i++)
        {
            // notepad.exe won't be locked by any process, so enricher returns null (no attribution)
            // This still exercises the full PathValidator + RestartManager + exception handling path
            await enricher.EnrichAlertAsync(Guid.NewGuid(), @"C:\Windows\System32\notepad.exe");
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        int handlesAfter = Process.GetCurrentProcess().HandleCount;
        int handleVariance = Math.Abs(handlesAfter - handlesBefore);

        handleVariance.ShouldBeLessThan(50,
            $"Handle leak: before={handlesBefore}, after={handlesAfter}, variance={handleVariance}");
    }

    public void Dispose()
    {
        _context.Database.CloseConnection();
        _context.Dispose();
    }
}
