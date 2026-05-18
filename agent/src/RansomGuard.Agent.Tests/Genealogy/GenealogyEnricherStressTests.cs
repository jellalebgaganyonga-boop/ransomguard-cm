using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Moq;
using RansomGuard.Agent.Core.Detection.Genealogy;
using Shouldly;

namespace RansomGuard.Agent.Tests.Genealogy;

/// <summary>
/// Stress tests for GenealogyEnricher and ProcessSnapshotService
/// verifying no handle leaks under repeated operation.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class GenealogyEnricherStressTests
{
    [Fact]
    public void ProcessSnapshotService_CaptureHundredTimes_NoHandleLeak()
    {
        var service = new ProcessSnapshotService(
            new Mock<ILogger<ProcessSnapshotService>>().Object);

        int pid = Environment.ProcessId;

        // Warm up — let GC settle
        for (int i = 0; i < 10; i++)
        {
            service.CaptureProcess(pid);
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        int handlesBefore = Process.GetCurrentProcess().HandleCount;

        // Run 100 capture operations (each involves WMI query + Process.GetProcessById)
        for (int i = 0; i < 100; i++)
        {
            ProcessSnapshot? snapshot = service.CaptureProcess(pid);
            snapshot.ShouldNotBeNull();
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        int handlesAfter = Process.GetCurrentProcess().HandleCount;
        int handleVariance = Math.Abs(handlesAfter - handlesBefore);

        // Handle variance should be < 10 after 100 operations
        handleVariance.ShouldBeLessThan(50,
            $"Handle leak detected: before={handlesBefore}, after={handlesAfter}, variance={handleVariance}");
    }

    [Fact]
    public void ProcessSnapshotService_BuildTreeHundredTimes_NoHandleLeak()
    {
        var service = new ProcessSnapshotService(
            new Mock<ILogger<ProcessSnapshotService>>().Object);

        int pid = Environment.ProcessId;

        // Warm up
        for (int i = 0; i < 5; i++)
        {
            service.BuildProcessTree(pid);
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        int handlesBefore = Process.GetCurrentProcess().HandleCount;

        for (int i = 0; i < 100; i++)
        {
            ProcessTree? tree = service.BuildProcessTree(pid);
            tree.ShouldNotBeNull();
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        int handlesAfter = Process.GetCurrentProcess().HandleCount;
        int handleVariance = Math.Abs(handlesAfter - handlesBefore);

        handleVariance.ShouldBeLessThan(50,
            $"Handle leak detected: before={handlesBefore}, after={handlesAfter}, variance={handleVariance}");
    }
}
