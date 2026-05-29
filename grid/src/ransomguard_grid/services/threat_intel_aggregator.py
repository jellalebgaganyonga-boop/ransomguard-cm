"""Aggregates IOCs from all sources in parallel, deduplicates, scores."""

import asyncio
from dataclasses import dataclass, field
from datetime import UTC, datetime

from ransomguard_grid.core.logging import get_logger
from ransomguard_grid.services.threat_intel_sources import ThreatIntelEntry, ThreatIntelSource

logger = get_logger("threat_intel_aggregator")


@dataclass
class AggregatedThreatIntel:
    """Result of aggregating all threat intel sources."""

    entries: list[ThreatIntelEntry]
    source_status: dict[str, str]
    total_entries: int
    generated_at: datetime = field(default_factory=lambda: datetime.now(UTC))


class ThreatIntelAggregator:
    """Aggregates IOCs from all sources in parallel, deduplicates, scores."""

    def __init__(self, sources: list[ThreatIntelSource]) -> None:
        self.sources = sources

    async def fetch_all(self) -> AggregatedThreatIntel:
        """Parallel fetch all sources. Failures don't stop other sources."""
        results = await asyncio.gather(
            *[self._fetch_safe(s) for s in self.sources],
            return_exceptions=False,
        )

        all_entries: list[ThreatIntelEntry] = []
        source_status: dict[str, str] = {}

        for source, result in zip(self.sources, results, strict=True):
            if isinstance(result, list):
                source_status[source.source_name] = f"ok: {len(result)} entries"
                all_entries.extend(result)
            else:
                source_status[source.source_name] = "failed"

        deduplicated = self._deduplicate(all_entries)

        return AggregatedThreatIntel(
            entries=deduplicated,
            source_status=source_status,
            total_entries=len(deduplicated),
        )

    async def _fetch_safe(self, source: ThreatIntelSource) -> list[ThreatIntelEntry]:
        """Fetch with exception swallowing."""
        try:
            return await source.fetch()
        except Exception as e:
            logger.warning("Source fetch failed", source=source.source_name, error=str(e))
            return []

    @staticmethod
    def _deduplicate(entries: list[ThreatIntelEntry]) -> list[ThreatIntelEntry]:
        """Deduplicate by (indicator, indicator_type), merge tags + confidence."""
        seen: dict[tuple[str, str], ThreatIntelEntry] = {}
        for entry in entries:
            key = (entry.indicator, entry.indicator_type)
            if key in seen:
                existing = seen[key]
                merged_tags = list(set(existing.tags + entry.tags))
                seen[key] = ThreatIntelEntry(
                    indicator=entry.indicator,
                    indicator_type=entry.indicator_type,
                    source=f"{existing.source}+{entry.source}",
                    confidence=max(existing.confidence, entry.confidence),
                    first_seen=existing.first_seen or entry.first_seen,
                    tags=merged_tags,
                    metadata={**existing.metadata, **entry.metadata},
                )
            else:
                seen[key] = entry
        return list(seen.values())
