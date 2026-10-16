"""The threat intel sources this GRID fetches from, decided once at startup.

A source that needs a key and has none is not built at all: it makes no call, and the operator
reads one Information line at startup saying so -- not a 401 Warning on every run (LOT 2 demo).
"""

import httpx

from ransomguard_grid.core.config import Settings
from ransomguard_grid.core.logging import get_logger
from ransomguard_grid.services.threat_intel_sources import ThreatIntelSource
from ransomguard_grid.services.threat_intel_sources.alienvault_otx import AlienVaultOtxSource
from ransomguard_grid.services.threat_intel_sources.aws import AwsIpRangesSource
from ransomguard_grid.services.threat_intel_sources.gcp import GcpIpRangesSource
from ransomguard_grid.services.threat_intel_sources.threatfox import ThreatFoxSource
from ransomguard_grid.services.threat_intel_sources.tor_project import TorProjectSource

logger = get_logger("threat_intel.catalog")


def build_sources(settings: Settings, http_client: httpx.AsyncClient) -> list[ThreatIntelSource]:
    """The enabled sources; one Information log per source disabled for want of a key."""
    sources: list[ThreatIntelSource] = [
        TorProjectSource(http_client),
        AwsIpRangesSource(http_client),
        GcpIpRangesSource(http_client),
    ]
    if settings.threatfox_api_key:
        sources.append(ThreatFoxSource(http_client, settings.threatfox_api_key))
    else:
        logger.info(
            "Threat intel source disabled: no API key (set GRID_THREATFOX_API_KEY)",
            source=ThreatFoxSource.source_name,
        )
    if settings.alienvault_otx_api_key:
        sources.append(AlienVaultOtxSource(http_client, settings.alienvault_otx_api_key))
    return sources
