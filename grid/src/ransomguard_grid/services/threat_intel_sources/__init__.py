"""Threat intel source fetchers and base types."""

from __future__ import annotations

from abc import ABC, abstractmethod
from dataclasses import dataclass, field


@dataclass
class ThreatIntelEntry:
    """Single IOC from a threat intel source."""

    indicator: str
    indicator_type: str  # ipv4, ipv6, domain, sha256, cidr, ipv6_cidr
    source: str
    confidence: int  # 0-100
    first_seen: str | None = None
    tags: list[str] = field(default_factory=list)
    metadata: dict[str, object] = field(default_factory=dict)


class ThreatIntelSource(ABC):
    """Base class for threat intel source fetchers."""

    source_name: str

    @abstractmethod
    async def fetch(self) -> list[ThreatIntelEntry]:
        """Fetch latest IOCs. May raise on network failure."""
        ...
