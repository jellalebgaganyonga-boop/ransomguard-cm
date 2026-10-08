using RansomGuard.Agent.Core.Diagnostics;
using Shouldly;

namespace RansomGuard.Agent.Tests.Diagnostics;

/// <summary>
/// The registry is what replaces the old config-derived status. These tests pin
/// its contract: the six entries are always present, honest defaults, real
/// transitions, and a stable ChangedAt when nothing changes.
/// </summary>
public sealed class ModuleStateRegistryTests
{
    [Fact]
    public void Seeds_all_six_modules()
    {
        var registry = new InMemoryModuleStateRegistry();

        var codes = registry.Snapshot().Select(e => e.Code).ToList();

        codes.ShouldBe(ModuleCode.All.ToList(), ignoreOrder: false);
    }

    [Fact]
    public void Detection_modules_default_to_inactive_until_they_report()
    {
        // Safe default for a security product: not protecting until proven so.
        var registry = new InMemoryModuleStateRegistry();

        foreach (var code in new[] { ModuleCode.Sentinel, ModuleCode.Entropy, ModuleCode.UsbGuard, ModuleCode.ExfilWatch })
        {
            ModuleStateEntry entry = registry.Snapshot().Single(e => e.Code == code);
            entry.State.ShouldBe(ModuleState.Inactive);
            entry.ReasonCode.ShouldBe(ModuleReasonCode.InitFailed);
        }
    }

    [Fact]
    public void Genealogy_is_degraded_and_anti_tampering_not_implemented()
    {
        var registry = new InMemoryModuleStateRegistry();

        ModuleStateEntry genealogy = registry.Snapshot().Single(e => e.Code == ModuleCode.Genealogy);
        genealogy.State.ShouldBe(ModuleState.Degraded);
        genealogy.ReasonCode.ShouldBe(ModuleReasonCode.AttributionUnreliable);

        ModuleStateEntry antiTampering = registry.Snapshot().Single(e => e.Code == ModuleCode.AntiTampering);
        antiTampering.State.ShouldBe(ModuleState.Inactive);
        antiTampering.ReasonCode.ShouldBe(ModuleReasonCode.NotImplemented);
    }

    [Fact]
    public void Publish_records_a_transition()
    {
        var registry = new InMemoryModuleStateRegistry();

        registry.Publish(ModuleCode.Sentinel, ModuleState.Active);

        ModuleStateEntry entry = registry.Snapshot().Single(e => e.Code == ModuleCode.Sentinel);
        entry.State.ShouldBe(ModuleState.Active);
        entry.ReasonCode.ShouldBeNull();
    }

    [Fact]
    public void Publishing_the_same_state_keeps_the_original_changed_at()
    {
        int tick = 0;
        var registry = new InMemoryModuleStateRegistry(() => new DateTime(2026, 1, 1).AddMinutes(tick++));

        registry.Publish(ModuleCode.Sentinel, ModuleState.Active);
        DateTime first = registry.Snapshot().Single(e => e.Code == ModuleCode.Sentinel).ChangedAt;
        registry.Publish(ModuleCode.Sentinel, ModuleState.Active);
        DateTime second = registry.Snapshot().Single(e => e.Code == ModuleCode.Sentinel).ChangedAt;

        second.ShouldBe(first); // no real change → ChangedAt frozen
    }

    [Fact]
    public void A_real_change_moves_changed_at()
    {
        int tick = 0;
        var registry = new InMemoryModuleStateRegistry(() => new DateTime(2026, 1, 1).AddMinutes(tick++));

        registry.Publish(ModuleCode.Sentinel, ModuleState.Active);
        DateTime active = registry.Snapshot().Single(e => e.Code == ModuleCode.Sentinel).ChangedAt;
        registry.Publish(ModuleCode.Sentinel, ModuleState.Inactive, ModuleReasonCode.StoppedUnexpectedly);
        DateTime stopped = registry.Snapshot().Single(e => e.Code == ModuleCode.Sentinel).ChangedAt;

        stopped.ShouldBeGreaterThan(active);
    }
}
