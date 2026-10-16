"""ThreatFox (abuse.ch) IOC fetcher.

abuse.ch requires an Auth-Key on every ThreatFox API call: without one the API answers 401. The
source is therefore only built when GRID_THREATFOX_API_KEY is set (catalog.build_sources); a key
the API refuses is reported as a Warning at most once an hour, never on every run.
"""

import time
from collections.abc import Callable

import httpx

from ransomguard_grid.core.logging import get_logger
from ransomguard_grid.services.threat_intel_sources import ThreatIntelEntry, ThreatIntelSource

logger = get_logger("threat_intel.threatfox")

# A refused key is an operator matter, not news on every run: one Warning an hour at most.
REFUSED_KEY_WARNING_INTERVAL_SECONDS = 3600.0


class ThreatFoxSource(ThreatIntelSource):
    """Fetches recent IOCs from ThreatFox API."""

    source_name = "threatfox"
    URL = "https://threatfox-api.abuse.ch/api/v1/"

    def __init__(
        self,
        http_client: httpx.AsyncClient,
        api_key: str,
        clock: Callable[[], float] = time.monotonic,
    ) -> None:
        self.client = http_client
        self.api_key = api_key
        self._clock = clock
        self._last_refusal_warning: float | None = None

    async def fetch(self) -> list[ThreatIntelEntry]:
        """Fetch recent C2 server and malware IOCs. A refused key yields no entries."""
        response = await self.client.post(
            self.URL,
            json={"query": "get_iocs", "days": 1},
            headers={"Auth-Key": self.api_key},
            timeout=30.0,
        )
        if response.status_code in (401, 403):
            self._warn_refused(response.status_code)
            return []
        response.raise_for_status()
        data = response.json()

        entries: list[ThreatIntelEntry] = []
        for ioc in data.get("data", []) or []:
            ind_type = self._map_type(ioc.get("ioc_type", ""))
            if not ind_type:
                continue
            entries.append(ThreatIntelEntry(
                indicator=ioc.get("ioc_value", ""),
                indicator_type=ind_type,
                source=self.source_name,
                confidence=ioc.get("confidence_level", 75),
                first_seen=ioc.get("first_seen"),
                tags=[t for t in [ioc.get("threat_type"), ioc.get("malware_alias")] if t],
                metadata={"reference": ioc.get("reference")},
            ))
        return entries

    def _warn_refused(self, status_code: int) -> None:
        now = self._clock()
        if (
            self._last_refusal_warning is not None
            and now - self._last_refusal_warning < REFUSED_KEY_WARNING_INTERVAL_SECONDS
        ):
            return
        self._last_refusal_warning = now
        logger.warning(
            "ThreatFox refused the API key; source returns no entries",
            source=self.source_name,
            status_code=status_code,
        )

    @staticmethod
    def _map_type(threatfox_type: str) -> str | None:
        return {"ip:port": "ipv4", "domain": "domain", "url": "url",
                "md5_hash": "md5", "sha256_hash": "sha256"}.get(threatfox_type)
