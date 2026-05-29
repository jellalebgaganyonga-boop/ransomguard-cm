"""AlienVault OTX pulse fetcher (requires API key)."""

import httpx

from ransomguard_grid.services.threat_intel_sources import ThreatIntelEntry, ThreatIntelSource


class AlienVaultOtxSource(ThreatIntelSource):
    """Fetches IOCs from AlienVault OTX pulses. Skips if no API key."""

    source_name = "alienvault_otx"
    URL = "https://otx.alienvault.com/api/v1/pulses/subscribed"

    def __init__(self, http_client: httpx.AsyncClient, api_key: str | None) -> None:
        self.client = http_client
        self.api_key = api_key

    async def fetch(self) -> list[ThreatIntelEntry]:
        """Fetch IOCs from subscribed pulses. Returns empty if no API key."""
        if not self.api_key:
            return []

        response = await self.client.get(
            self.URL, headers={"X-OTX-API-KEY": self.api_key}, timeout=30.0,
        )
        response.raise_for_status()
        data = response.json()

        entries: list[ThreatIntelEntry] = []
        for pulse in data.get("results", []):
            for indicator in pulse.get("indicators", []):
                ind_type = self._map_type(indicator.get("type", ""))
                if not ind_type:
                    continue
                entries.append(ThreatIntelEntry(
                    indicator=indicator.get("indicator", ""),
                    indicator_type=ind_type,
                    source=self.source_name,
                    confidence=80,
                    tags=[pulse.get("name", "")],
                ))
        return entries

    @staticmethod
    def _map_type(otx_type: str) -> str | None:
        return {"IPv4": "ipv4", "IPv6": "ipv6", "domain": "domain",
                "hostname": "domain", "URL": "url", "FileHash-SHA256": "sha256"}.get(otx_type)
