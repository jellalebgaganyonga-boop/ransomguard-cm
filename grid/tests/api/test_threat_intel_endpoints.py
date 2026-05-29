"""Tests for threat intel public endpoints."""

from datetime import UTC, datetime
from uuid import uuid4

import pytest
from httpx import AsyncClient
from sqlalchemy.ext.asyncio import AsyncSession

from ransomguard_grid.db.models.enums import ThreatIntelStatus
from ransomguard_grid.db.models.threat_intel import ThreatIntelVersion
from tests.conftest import AgentFixture, _test_session_factory


async def _insert_version(version_string: str = "2026-05-29-001") -> None:
    """Insert a test ThreatIntelVersion directly."""
    async with _test_session_factory() as session:
        session.add(ThreatIntelVersion(
            id=str(uuid4()),
            version_string=version_string,
            published_at=datetime.now(UTC),
            package_size_bytes=1024,
            package_sha256="a" * 64,
            ed25519_signature=b"test-sig",
            status=ThreatIntelStatus.Published,
        ))
        await session.commit()


@pytest.mark.asyncio
async def test_manifest_requires_mtls(client: AsyncClient) -> None:
    """GET /threat-intel/manifest without cert should return 401."""
    response = await client.get("/api/v1/threat-intel/manifest")
    assert response.status_code == 401


@pytest.mark.asyncio
async def test_manifest_returns_404_when_no_version(client: AsyncClient, test_agent: AgentFixture) -> None:
    """GET /threat-intel/manifest with no published version should return 404."""
    response = await client.get(
        "/api/v1/threat-intel/manifest",
        headers={"X-Client-Cert": test_agent.cert_pem},
    )
    assert response.status_code == 404


@pytest.mark.asyncio
async def test_manifest_returns_latest_published(client: AsyncClient, test_agent: AgentFixture) -> None:
    """GET /threat-intel/manifest should return latest published version."""
    await _insert_version("2026-05-29-001")

    response = await client.get(
        "/api/v1/threat-intel/manifest",
        headers={"X-Client-Cert": test_agent.cert_pem},
    )
    assert response.status_code == 200
    data = response.json()
    assert data["version"] == "2026-05-29-001"
    assert "package_url" in data
    assert "ed25519_signature_base64" in data


@pytest.mark.asyncio
async def test_report_applied_version_creates_record(client: AsyncClient, test_agent: AgentFixture) -> None:
    """POST /agents/{id}/threat-intel-version should create tracking record."""
    await _insert_version("2026-05-29-002")

    response = await client.post(
        f"/api/v1/threat-intel/agents/{test_agent.agent_id}/threat-intel-version",
        json={
            "applied_version": "2026-05-29-002",
            "applied_at": datetime.now(UTC).isoformat(),
            "applied_status": "success",
        },
        headers={"X-Client-Cert": test_agent.cert_pem},
    )
    assert response.status_code == 200
    assert response.json()["success"] is True


@pytest.mark.asyncio
async def test_report_unknown_version_returns_404(client: AsyncClient, test_agent: AgentFixture) -> None:
    """POST with unknown version string should return 404."""
    response = await client.post(
        f"/api/v1/threat-intel/agents/{test_agent.agent_id}/threat-intel-version",
        json={
            "applied_version": "nonexistent-version",
            "applied_at": datetime.now(UTC).isoformat(),
            "applied_status": "success",
        },
        headers={"X-Client-Cert": test_agent.cert_pem},
    )
    assert response.status_code == 404
