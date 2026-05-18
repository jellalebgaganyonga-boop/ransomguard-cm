using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Moq;
using RansomGuard.Agent.Core.Detection.Genealogy;
using Shouldly;

namespace RansomGuard.Agent.Tests.Genealogy;

/// <summary>
/// Tests for process snapshot and tree building.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ProcessTreeTests
{
    private readonly ProcessSnapshotService _service;

    public ProcessTreeTests()
    {
        _service = new ProcessSnapshotService(new Mock<ILogger<ProcessSnapshotService>>().Object);
    }

    [Fact]
    public void CaptureProcess_CurrentProcess_Succeeds()
    {
        int pid = Environment.ProcessId;
        ProcessSnapshot? snapshot = _service.CaptureProcess(pid);

        snapshot.ShouldNotBeNull();
        snapshot.ProcessId.ShouldBe(pid);
        snapshot.ProcessName.ShouldNotBeNullOrEmpty();
        snapshot.WorkingSetBytes.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void CaptureProcess_NonExistentPid_ReturnsNull()
    {
        ProcessSnapshot? snapshot = _service.CaptureProcess(99999);
        snapshot.ShouldBeNull();
    }

    [Fact]
    public void BuildProcessTree_CurrentProcess_HasAncestors()
    {
        ProcessTree? tree = _service.BuildProcessTree(Environment.ProcessId);

        tree.ShouldNotBeNull();
        tree.Root.ProcessId.ShouldBe(Environment.ProcessId);
        tree.Ancestors.Count.ShouldBeGreaterThan(0); // At least 1 parent
        tree.Summary.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public void BuildProcessTree_NonExistentPid_ReturnsNull()
    {
        ProcessTree? tree = _service.BuildProcessTree(99999);
        tree.ShouldBeNull();
    }

    [Fact]
    public void ProcessTree_Summary_ContainsProcessNames()
    {
        ProcessTree? tree = _service.BuildProcessTree(Environment.ProcessId);
        tree.ShouldNotBeNull();

        // Summary should be "parent -> ... -> current"
        tree.Summary.ShouldContain(tree.Root.ProcessName);
    }
}
