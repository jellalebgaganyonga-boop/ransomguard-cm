"""Tests for threat intel aggregator."""

import pytest

from ransomguard_grid.services.threat_intel_aggregator import ThreatIntelAggregator
from ransomguard_grid.services.threat_intel_sources import ThreatIntelEntry, ThreatIntelSource


class FakeSource(ThreatIntelSource):
    """Fake source for testing."""

    def __init__(self, name: str, entries: list[ThreatIntelEntry], *, fail: bool = False) -> None:
        self.source_name = name
        self._entries = entries
        self._fail = fail

    async def fetch(self) -> list[ThreatIntelEntry]:
        if self._fail:
            raise RuntimeError("Network error")
        return self._entries


@pytest.mark.asyncio
async def test_aggregator_parallel_fetch_all_sources() -> None:
    """All sources fetched and combined."""
    s1 = FakeSource("s1", [ThreatIntelEntry("1.1.1.1", "ipv4", "s1", 90)])
    s2 = FakeSource("s2", [ThreatIntelEntry("2.2.2.2", "ipv4", "s2", 80)])

    agg = ThreatIntelAggregator([s1, s2])
    result = await agg.fetch_all()

    assert result.total_entries == 2
    assert "s1" in result.source_status
    assert "s2" in result.source_status


@pytest.mark.asyncio
async def test_aggregator_deduplicates_same_indicator() -> None:
    """Same indicator from two sources should merge."""
    entry1 = ThreatIntelEntry("1.1.1.1", "ipv4", "s1", 80, tags=["tor"])
    entry2 = ThreatIntelEntry("1.1.1.1", "ipv4", "s2", 95, tags=["c2"])

    s1 = FakeSource("s1", [entry1])
    s2 = FakeSource("s2", [entry2])

    agg = ThreatIntelAggregator([s1, s2])
    result = await agg.fetch_all()

    assert result.total_entries == 1
    merged = result.entries[0]
    assert merged.confidence == 95  # max
    assert "tor" in merged.tags
    assert "c2" in merged.tags


@pytest.mark.asyncio
async def test_aggregator_continues_when_one_source_fails() -> None:
    """Failing source should not prevent others from being aggregated."""
    good = FakeSource("good", [ThreatIntelEntry("3.3.3.3", "ipv4", "good", 90)])
    bad = FakeSource("bad", [], fail=True)

    agg = ThreatIntelAggregator([good, bad])
    result = await agg.fetch_all()

    assert result.total_entries == 1
    assert "good" in result.source_status
    assert "bad" in result.source_status
