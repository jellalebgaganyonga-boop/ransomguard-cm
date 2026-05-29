"""GCP IP ranges fetcher for cloud provider identification."""

import httpx

from ransomguard_grid.services.threat_intel_sources import ThreatIntelEntry, ThreatIntelSource


class GcpIpRangesSource(ThreatIntelSource):
    """Fetches GCP IP ranges from official JSON endpoint."""

    source_name = "gcp_ip_ranges"
    URL = "https://www.gstatic.com/ipranges/cloud.json"

    def __init__(self, http_client: httpx.AsyncClient) -> None:
        self.client = http_client

    async def fetch(self) -> list[ThreatIntelEntry]:
        """Fetch GCP IP prefixes."""
        response = await self.client.get(self.URL, timeout=30.0)
        response.raise_for_status()
        data = response.json()

        entries: list[ThreatIntelEntry] = []
        for prefix in data.get("prefixes", []):
            indicator = prefix.get("ipv4Prefix") or prefix.get("ipv6Prefix")
            if not indicator:
                continue
            ind_type = "cidr" if "ipv4Prefix" in prefix else "ipv6_cidr"
            entries.append(ThreatIntelEntry(
                indicator=indicator,
                indicator_type=ind_type,
                source=self.source_name,
                confidence=100,
                tags=["gcp", prefix.get("service", "").lower()],
                metadata={"scope": prefix.get("scope")},
            ))
        return entries
