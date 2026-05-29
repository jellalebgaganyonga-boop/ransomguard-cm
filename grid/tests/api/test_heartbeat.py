"""Tests for agent heartbeat endpoint."""

from datetime import UTC, datetime
from uuid import uuid4

import pytest
from httpx import AsyncClient
from sqlalchemy.ext.asyncio import AsyncSession

from ransomguard_grid.db.models.agent import Agent, AgentCertificate
from ransomguard_grid.db.models.enums import AgentStatus, TenantStatus
from ransomguard_grid.db.models.tenant_user import Tenant


async def _setup(db: AsyncSession, serial: str) -> tuple[Tenant, Agent]:
    """Create tenant + agent + cert."""
    tenant = Tenant(id=str(uuid4()), name="T", code=f"hb-{uuid4().hex[:6]}", contact_email="t@t.local", status=TenantStatus.active)
    db.add(tenant)
    agent = Agent(id=str(uuid4()), tenant_id=tenant.id, hostname="h", fqdn="h.local", os_version="10", agent_version="1.0", hardware_fingerprint=f"fp-hb-{uuid4().hex[:6]}", status=AgentStatus.active)
    db.add(agent)
    cert = AgentCertificate(id=str(uuid4()), agent_id=agent.id, serial_number=serial, fingerprint_sha256="f" * 64, not_before=datetime.now(UTC), not_after=datetime(2027, 1, 1, tzinfo=UTC))
    db.add(cert)
    await db.flush()
    await db.commit()
    return tenant, agent


@pytest.mark.asyncio
async def test_heartbeat_returns_200(
    client: AsyncClient, db_session: AsyncSession, test_cert_serial: str, test_cert_pem: str,
) -> None:
    """Valid heartbeat should return 200 with next interval."""
    _, agent = await _setup(db_session, test_cert_serial)

    response = await client.post(
        f"/api/v1/agents/{agent.id}/heartbeat",
        json={"agent_uptime_seconds": 3600, "modules_status": {"SENTINEL": "active"}},
        headers={"X-Client-Cert": test_cert_pem},
    )
    assert response.status_code == 200
    data = response.json()
    assert data["next_heartbeat_in_seconds"] == 60
    assert "server_time" in data


@pytest.mark.asyncio
async def test_heartbeat_returns_empty_pending_commands(
    client: AsyncClient, db_session: AsyncSession, test_cert_serial: str, test_cert_pem: str,
) -> None:
    """Heartbeat with no pending commands should return empty list."""
    _, agent = await _setup(db_session, test_cert_serial)

    response = await client.post(
        f"/api/v1/agents/{agent.id}/heartbeat",
        json={"agent_uptime_seconds": 100, "modules_status": {}},
        headers={"X-Client-Cert": test_cert_pem},
    )
    assert response.status_code == 200
    assert response.json()["pending_commands"] == []


@pytest.mark.asyncio
async def test_heartbeat_captures_threat_intel_version(
    client: AsyncClient, db_session: AsyncSession, test_cert_serial: str, test_cert_pem: str,
) -> None:
    """Heartbeat should accept and store threat_intel_version."""
    _, agent = await _setup(db_session, test_cert_serial)

    response = await client.post(
        f"/api/v1/agents/{agent.id}/heartbeat",
        json={"agent_uptime_seconds": 100, "threat_intel_version": "2026-05-29-001", "modules_status": {}},
        headers={"X-Client-Cert": test_cert_pem},
    )
    assert response.status_code == 200


@pytest.mark.asyncio
async def test_heartbeat_requires_mtls(client: AsyncClient) -> None:
    """No cert should return 401."""
    response = await client.post("/api/v1/agents/fake/heartbeat", json={"agent_uptime_seconds": 0, "modules_status": {}})
    assert response.status_code == 401


@pytest.mark.asyncio
async def test_heartbeat_agent_id_mismatch(
    client: AsyncClient, db_session: AsyncSession, test_cert_serial: str, test_cert_pem: str,
) -> None:
    """Wrong agent_id in path should return 403."""
    await _setup(db_session, test_cert_serial)

    response = await client.post(
        "/api/v1/agents/wrong-id/heartbeat",
        json={"agent_uptime_seconds": 100, "modules_status": {}},
        headers={"X-Client-Cert": test_cert_pem},
    )
    assert response.status_code == 403


@pytest.mark.asyncio
async def test_multiple_heartbeats_accepted(
    client: AsyncClient, db_session: AsyncSession, test_cert_serial: str, test_cert_pem: str,
) -> None:
    """Multiple heartbeats should all succeed (append history)."""
    _, agent = await _setup(db_session, test_cert_serial)

    for i in range(3):
        r = await client.post(
            f"/api/v1/agents/{agent.id}/heartbeat",
            json={"agent_uptime_seconds": i * 60, "modules_status": {"iter": str(i)}},
            headers={"X-Client-Cert": test_cert_pem},
        )
        assert r.status_code == 200
