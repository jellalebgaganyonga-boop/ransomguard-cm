"""Tor Project bulk exit node list fetcher."""

import httpx

from ransomguard_grid.services.threat_intel_sources import ThreatIntelEntry, ThreatIntelSource


class TorProjectSource(ThreatIntelSource):
    """Fetches Tor exit node IPs from the Tor Project."""

    source_name = "tor_project"
    URL = "https://check.torproject.org/torbulkexitlist"

    def __init__(self, http_client: httpx.AsyncClient) -> None:
        self.client = http_client

    async def fetch(self) -> list[ThreatIntelEntry]:
        """Fetch Tor exit node IP list."""
        response = await self.client.get(self.URL, timeout=30.0)
        response.raise_for_status()

        entries: list[ThreatIntelEntry] = []
        for line in response.text.strip().split("\n"):
            ip = line.strip()
            if not ip or ip.startswith("#"):
                continue
            entries.append(ThreatIntelEntry(
                indicator=ip, indicator_type="ipv4",
                source=self.source_name, confidence=95, tags=["tor_exit_node"],
            ))
        return entries
