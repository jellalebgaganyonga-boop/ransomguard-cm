"""ThreatFox (abuse.ch) IOC fetcher."""

import httpx

from ransomguard_grid.services.threat_intel_sources import ThreatIntelEntry, ThreatIntelSource


class ThreatFoxSource(ThreatIntelSource):
    """Fetches recent IOCs from ThreatFox API."""

    source_name = "threatfox"
    URL = "https://threatfox-api.abuse.ch/api/v1/"

    def __init__(self, http_client: httpx.AsyncClient) -> None:
        self.client = http_client

    async def fetch(self) -> list[ThreatIntelEntry]:
        """Fetch recent C2 server and malware IOCs."""
        response = await self.client.post(
            self.URL, json={"query": "get_iocs", "days": 1}, timeout=30.0,
        )
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

    @staticmethod
    def _map_type(threatfox_type: str) -> str | None:
        return {"ip:port": "ipv4", "domain": "domain", "url": "url",
                "md5_hash": "md5", "sha256_hash": "sha256"}.get(threatfox_type)
