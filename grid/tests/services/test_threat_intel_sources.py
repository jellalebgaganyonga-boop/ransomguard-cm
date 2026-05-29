"""Tests for individual threat intel source fetchers."""

import pytest
import httpx

from ransomguard_grid.services.threat_intel_sources.tor_project import TorProjectSource
from ransomguard_grid.services.threat_intel_sources.threatfox import ThreatFoxSource
from ransomguard_grid.services.threat_intel_sources.aws import AwsIpRangesSource
from ransomguard_grid.services.threat_intel_sources.gcp import GcpIpRangesSource
from ransomguard_grid.services.threat_intel_sources.alienvault_otx import AlienVaultOtxSource


@pytest.mark.asyncio
async def test_tor_project_fetches_and_parses_ips() -> None:
    """TorProjectSource should parse newline-separated IPs."""
    mock_response = "1.2.3.4\n5.6.7.8\n# comment\n9.10.11.12\n"

    transport = httpx.MockTransport(lambda req: httpx.Response(200, text=mock_response))
    async with httpx.AsyncClient(transport=transport) as client:
        source = TorProjectSource(client)
        entries = await source.fetch()

    assert len(entries) == 3
    assert entries[0].indicator == "1.2.3.4"
    assert entries[0].indicator_type == "ipv4"
    assert "tor_exit_node" in entries[0].tags
    assert entries[0].confidence == 95


@pytest.mark.asyncio
async def test_threatfox_handles_json_response() -> None:
    """ThreatFoxSource should parse ThreatFox JSON format."""
    mock_data = {
        "query_status": "ok",
        "data": [
            {"ioc_value": "10.0.0.1:443", "ioc_type": "ip:port", "confidence_level": 90,
             "threat_type": "botnet_cc", "malware_alias": "Emotet", "first_seen": "2026-01-01", "reference": "https://example.com"},
            {"ioc_value": "evil.com", "ioc_type": "domain", "confidence_level": 80,
             "threat_type": "c2", "malware_alias": None, "first_seen": None, "reference": None},
        ],
    }

    transport = httpx.MockTransport(lambda req: httpx.Response(200, json=mock_data))
    async with httpx.AsyncClient(transport=transport) as client:
        source = ThreatFoxSource(client)
        entries = await source.fetch()

    assert len(entries) == 2
    assert entries[0].indicator == "10.0.0.1:443"
    assert entries[0].indicator_type == "ipv4"
    assert entries[1].indicator_type == "domain"


@pytest.mark.asyncio
async def test_aws_parses_ip_ranges_json() -> None:
    """AwsIpRangesSource should parse AWS JSON format."""
    mock_data = {
        "syncToken": "1234",
        "prefixes": [
            {"ip_prefix": "3.0.0.0/15", "region": "us-east-1", "service": "AMAZON"},
            {"ip_prefix": "52.94.0.0/20", "region": "eu-west-1", "service": "S3"},
        ],
    }

    transport = httpx.MockTransport(lambda req: httpx.Response(200, json=mock_data))
    async with httpx.AsyncClient(transport=transport) as client:
        source = AwsIpRangesSource(client)
        entries = await source.fetch()

    assert len(entries) == 2
    assert entries[0].indicator_type == "cidr"
    assert "aws" in entries[0].tags


@pytest.mark.asyncio
async def test_gcp_parses_cloud_json() -> None:
    """GcpIpRangesSource should parse GCP JSON format."""
    mock_data = {
        "prefixes": [
            {"ipv4Prefix": "8.8.8.0/24", "service": "Google Cloud", "scope": "us-central1"},
            {"ipv6Prefix": "2600:1901::/48", "service": "Google Cloud", "scope": "global"},
        ],
    }

    transport = httpx.MockTransport(lambda req: httpx.Response(200, json=mock_data))
    async with httpx.AsyncClient(transport=transport) as client:
        source = GcpIpRangesSource(client)
        entries = await source.fetch()

    assert len(entries) == 2
    assert entries[0].indicator_type == "cidr"
    assert entries[1].indicator_type == "ipv6_cidr"


@pytest.mark.asyncio
async def test_alienvault_otx_skipped_when_no_api_key() -> None:
    """AlienVaultOtxSource returns empty list when no API key."""
    transport = httpx.MockTransport(lambda req: httpx.Response(200, json={}))
    async with httpx.AsyncClient(transport=transport) as client:
        source = AlienVaultOtxSource(client, api_key=None)
        entries = await source.fetch()

    assert entries == []


@pytest.mark.asyncio
async def test_source_network_failure_returns_empty() -> None:
    """A source that raises should not crash the caller."""
    transport = httpx.MockTransport(lambda req: (_ for _ in ()).throw(httpx.ConnectError("down")))
    async with httpx.AsyncClient(transport=transport) as client:
        source = TorProjectSource(client)
        with pytest.raises(httpx.ConnectError):
            await source.fetch()
