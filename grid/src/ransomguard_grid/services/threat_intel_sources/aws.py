"""AWS IP ranges fetcher for cloud provider identification."""

import httpx

from ransomguard_grid.services.threat_intel_sources import ThreatIntelEntry, ThreatIntelSource


class AwsIpRangesSource(ThreatIntelSource):
    """Fetches AWS IP ranges from official JSON endpoint."""

    source_name = "aws_ip_ranges"
    URL = "https://ip-ranges.amazonaws.com/ip-ranges.json"

    def __init__(self, http_client: httpx.AsyncClient) -> None:
        self.client = http_client

    async def fetch(self) -> list[ThreatIntelEntry]:
        """Fetch AWS IP prefixes."""
        response = await self.client.get(self.URL, timeout=30.0)
        response.raise_for_status()
        data = response.json()

        entries: list[ThreatIntelEntry] = []
        for prefix in data.get("prefixes", []):
            entries.append(ThreatIntelEntry(
                indicator=prefix["ip_prefix"],
                indicator_type="cidr",
                source=self.source_name,
                confidence=100,
                tags=["aws", prefix.get("service", "").lower()],
                metadata={"region": prefix.get("region")},
            ))
        return entries
