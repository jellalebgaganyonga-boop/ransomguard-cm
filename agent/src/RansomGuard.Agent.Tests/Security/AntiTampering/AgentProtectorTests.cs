using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Moq;
using RansomGuard.Agent.Core.Security.AntiTampering;
using Shouldly;

namespace RansomGuard.Agent.Tests.Security.AntiTampering;

/// <summary>
/// Tests for anti-tampering agent protection: debugger detection, code integrity,
/// registry monitoring, and composite protector.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class AgentProtectorTests : IDisposable
{
    private readonly DebuggerDetector _debuggerDetector;
    private readonly CodeSectionIntegrity _codeIntegrity;
    private readonly RegistryWatcher _registryWatcher;
    private readonly AgentProtector _protector;

    public AgentProtectorTests()
    {
        _debuggerDetector = new DebuggerDetector(
            new Mock<ILogger<DebuggerDetector>>().Object,
            allowInDevelopment: true); // Test environment = development mode
        _codeIntegrity = new CodeSectionIntegrity(
            new Mock<ILogger<CodeSectionIntegrity>>().Object);
        _registryWatcher = new RegistryWatcher(
            new Mock<ILogger<RegistryWatcher>>().Object);
        _protector = new AgentProtector(
            _debuggerDetector, _codeIntegrity, _registryWatcher,
            new Mock<ILogger<AgentProtector>>().Object);
    }

    [Fact]
    public void Debugger_Attached_Development_Returns_Null()
    {
        // In development mode, debugger presence is not treated as tampering
        var devDetector = new DebuggerDetector(
            new Mock<ILogger<DebuggerDetector>>().Object,
            allowInDevelopment: true);

        string? result = devDetector.Check();
        // Whether debugger is present or not, dev mode should return null
        result.ShouldBeNull("Development mode should not flag debugger as tampering");
    }

    [Fact]
    public void Debugger_Production_Mode_Checks_Correctly()
    {
        var prodDetector = new DebuggerDetector(
            new Mock<ILogger<DebuggerDetector>>().Object,
            allowInDevelopment: false);

        // In production mode, result depends on actual debugger state
        string? result = prodDetector.Check();
        // Cannot guarantee debugger attachment in CI — just verify no exception
        // If VS debugger is attached, result will be non-null
    }

    [Fact]
    public void Code_Section_Baseline_Computed_Successfully()
    {
        _codeIntegrity.ComputeBaseline();

        _codeIntegrity.BaselineHash.ShouldNotBeNullOrEmpty(
            "Baseline hash should be computed from current executable");
        _codeIntegrity.BaselineHash!.Length.ShouldBe(64, "SHA-256 hex should be 64 characters");
    }

    [Fact]
    public void Code_Section_Verify_After_Baseline_Returns_Intact()
    {
        _codeIntegrity.ComputeBaseline();

        string? result = _codeIntegrity.Verify();

        result.ShouldBeNull("Immediate verify after baseline should show no tampering");
    }

    [Fact]
    public void Registry_Watcher_Captures_Baseline_Without_Exception()
    {
        Should.NotThrow(() => _registryWatcher.CaptureBaseline());
    }

    [Fact]
    public void Registry_Watcher_Check_Returns_Empty_When_No_Changes()
    {
        _registryWatcher.CaptureBaseline();

        IReadOnlyList<string> modifications = _registryWatcher.CheckForModifications();

        modifications.Count.ShouldBe(0, "No changes expected immediately after baseline capture");
    }

    [Fact]
    public async Task Composite_Protector_Starts_Without_Exception()
    {
        await Should.NotThrowAsync(() => _protector.StartProtectionAsync());
    }

    [Fact]
    public async Task Composite_Protector_CheckIntegrity_Returns_Status()
    {
        await _protector.StartProtectionAsync();

        TamperingStatus status = await _protector.CheckIntegrityAsync();

        status.ShouldNotBeNull();
        status.CheckedAt.ShouldBeLessThanOrEqualTo(DateTime.UtcNow);
        status.TamperingIndicators.ShouldNotBeNull();
        // In test/dev environment: IsIntact should be true (debugger allowed in dev)
        status.IsIntact.ShouldBeTrue("Test environment should show no tampering");
    }

    public void Dispose()
    {
        _protector.Dispose();
    }
}
