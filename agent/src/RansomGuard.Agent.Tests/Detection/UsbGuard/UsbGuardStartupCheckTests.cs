using Microsoft.Extensions.Logging;
using Moq;
using RansomGuard.Agent.Core.Detection.UsbGuard;
using RansomGuard.Agent.Core.Diagnostics;
using RansomGuard.Agent.Core.Persistence.Entities;
using Shouldly;

namespace RansomGuard.Agent.Tests.Detection.UsbGuard;

/// <summary>
/// Tests for <see cref="UsbGuardStartupCheck"/> — Strict mode with an empty whitelist
/// must be announced at startup, as a warning, never a block.
/// </summary>
public sealed class UsbGuardStartupCheckTests
{
    private readonly Mock<IUsbWhitelistService> _whitelist = new();
    private readonly Mock<ILogger> _logger = new();

    [Fact]
    public async Task Strict_with_empty_whitelist_logs_a_warning()
    {
        _whitelist.Setup(w => w.ListActiveAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);

        bool warned = await UsbGuardStartupCheck.WarnIfStrictWithEmptyWhitelistAsync(
            UsbOperatingMode.Strict, _whitelist.Object, _logger.Object);

        warned.ShouldBeTrue();
        VerifyWarnings(Times.Once());
    }

    [Fact]
    public async Task Strict_with_populated_whitelist_does_not_warn()
    {
        _whitelist.Setup(w => w.ListActiveAsync(It.IsAny<CancellationToken>())).ReturnsAsync([Entry()]);

        bool warned = await UsbGuardStartupCheck.WarnIfStrictWithEmptyWhitelistAsync(
            UsbOperatingMode.Strict, _whitelist.Object, _logger.Object);

        warned.ShouldBeFalse();
        VerifyWarnings(Times.Never());
    }

    [Fact]
    public async Task Strict_with_unreadable_whitelist_warns_that_devices_are_let_through()
    {
        _whitelist.Setup(w => w.ListActiveAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("database unavailable"));

        bool warned = await UsbGuardStartupCheck.WarnIfStrictWithEmptyWhitelistAsync(
            UsbOperatingMode.Strict, _whitelist.Object, _logger.Object);

        warned.ShouldBeTrue();
        VerifyWarnings(Times.Once());
        UsbGuardStartupCheck.UnreadableWhitelistWarning.ShouldContain("LET THROUGH");
    }

    [Theory]
    [InlineData(UsbOperatingMode.Permissive)]
    [InlineData(UsbOperatingMode.Audit)]
    public async Task Other_modes_do_not_warn_and_do_not_read_the_whitelist(UsbOperatingMode mode)
    {
        bool warned = await UsbGuardStartupCheck.WarnIfStrictWithEmptyWhitelistAsync(
            mode, _whitelist.Object, _logger.Object);

        warned.ShouldBeFalse();
        VerifyWarnings(Times.Never());
        _whitelist.Verify(w => w.ListActiveAsync(It.IsAny<CancellationToken>()), Times.Never());
    }

    // Since 2026-10-22 the actions exist (AGT-USB-001, after V7): Audit promises the alert,
    // Permissive promises read-only and quarantine, and they happen. Strict promises the ejection,
    // which happens, and the block on re-insertion, which does not: it alone stays degraded.
    [Theory]
    [InlineData(UsbOperatingMode.Permissive)]
    [InlineData(UsbOperatingMode.Audit)]
    public void Permissive_and_Audit_run_active_now_that_their_actions_exist(UsbOperatingMode mode)
    {
        var (state, reason) = UsbGuardStartupCheck.RunningPosture(mode);

        state.ShouldBe(ModuleState.Active);
        reason.ShouldBeNull();
    }

    [Fact]
    public void Strict_stays_degraded_while_the_block_on_reinsertion_does_not_exist()
    {
        var (state, reason) = UsbGuardStartupCheck.RunningPosture(UsbOperatingMode.Strict);

        state.ShouldBe(ModuleState.Degraded);
        reason.ShouldBe(ModuleReasonCode.ActionsNotImplemented);
        reason.ShouldBe("actions_not_implemented");
    }

    private void VerifyWarnings(Times times) =>
        _logger.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            times);

    private static UsbWhitelistEntry Entry() => new()
    {
        Id = Guid.NewGuid(),
        SerialNumberHash = "hash",
        Description = "Radiology key",
        AddedByUser = "admin",
        AddedAt = DateTime.UtcNow,
        IsActive = true,
        PolicyLevel = UsbPolicyLevel.Trusted,
    };
}
