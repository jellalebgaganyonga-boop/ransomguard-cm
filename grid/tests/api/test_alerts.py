"""Tests for alert ingestion endpoints."""

from datetime import UTC, datetime
from uuid import uuid4

import pytest
from httpx import AsyncClient
from sqlalchemy.ext.asyncio import AsyncSession

from ransomguard_grid.db.models.agent import Agent, AgentCertificate
from ransomguard_grid.db.models.enums import AgentStatus, Severity, TenantStatus
from ransomguard_grid.db.models.tenant_user import Tenant


async def _setup_agent_with_cert(
    db: AsyncSession, serial: str, tenant_code: str = "alert-t"
) -> tuple[Tenant, Agent]:
    """Create tenant + agent + cert for mTLS test."""
    tenant = Tenant(id=str(uuid4()), name="T", code=tenant_code, contact_email="t@t.local", status=TenantStatus.active)
    db.add(tenant)
    await db.flush()

    agent = Agent(
        id=str(uuid4()), tenant_id=tenant.id, hostname="h", fqdn="h.local",
        os_version="10", agent_version="1.0", hardware_fingerprint=f"fp-{tenant_code}",
        status=AgentStatus.active,
    )
    db.add(agent)
    await db.flush()

    now = datetime.now(UTC)
    cert = AgentCertificate(
        id=str(uuid4()), agent_id=agent.id, serial_number=serial,
        fingerprint_sha256="f" * 64, not_before=now, not_after=datetime(2027, 1, 1, tzinfo=UTC),
    )
    db.add(cert)
    await db.flush()
    await db.commit()
    return tenant, agent


@pytest.mark.asyncio
async def test_alert_requires_mtls(client: AsyncClient) -> None:
    """No X-Client-Cert header should return 401."""
    response = await client.post("/api/v1/agents/fake-id/alerts", json={})
    assert response.status_code == 401
    assert "Mutual-TLS" in response.headers.get("WWW-Authenticate", "")


@pytest.mark.asyncio
async def test_alert_invalid_cert_returns_401(client: AsyncClient) -> None:
    """Invalid cert PEM should return 401."""
    response = await client.post(
        "/api/v1/agents/fake-id/alerts",
        json={},
        headers={"X-Client-Cert": "NOT-A-CERT"},
    )
    assert response.status_code == 401


@pytest.mark.asyncio
async def test_alert_unknown_cert_returns_401(
    client: AsyncClient, test_cert_pem: str,
) -> None:
    """Valid cert but unknown serial should return 401."""
    response = await client.post(
        "/api/v1/agents/fake-id/alerts",
        json={"client_message_id": "m" * 20, "alert_type": "USB", "severity": "High",
              "detected_at": datetime.now(UTC).isoformat(), "summary": "test"},
        headers={"X-Client-Cert": test_cert_pem},
    )
    assert response.status_code == 401


@pytest.mark.asyncio
async def test_valid_alert_ingested(
    client: AsyncClient, db_session: AsyncSession, test_cert_serial: str, test_cert_pem: str,
) -> None:
    """Valid alert with known cert should be ingested."""
    tenant, agent = await _setup_agent_with_cert(db_session, test_cert_serial, f"ingest-{uuid4().hex[:6]}")

    response = await client.post(
        f"/api/v1/agents/{agent.id}/alerts",
        json={
            "client_message_id": f"msg-{uuid4().hex[:20]}",
            "alert_type": "SENTINEL",
            "severity": "Critical",
            "detected_at": datetime.now(UTC).isoformat(),
            "summary": "Canary file tampered",
        },
        headers={"X-Client-Cert": test_cert_pem},
    )
    assert response.status_code == 200
    data = response.json()
    assert data["status"] == "ingested"
    assert "alert_id" in data


@pytest.mark.asyncio
async def test_duplicate_alert_returns_duplicate(
    client: AsyncClient, db_session: AsyncSession, test_cert_serial: str, test_cert_pem: str,
) -> None:
    """Duplicate client_message_id should return existing alert."""
    tenant, agent = await _setup_agent_with_cert(db_session, test_cert_serial, f"dup-{uuid4().hex[:6]}")
    msg_id = f"dup-msg-{uuid4().hex[:16]}"

    r1 = await client.post(
        f"/api/v1/agents/{agent.id}/alerts",
        json={"client_message_id": msg_id, "alert_type": "USB", "severity": "High",
              "detected_at": datetime.now(UTC).isoformat(), "summary": "test"},
        headers={"X-Client-Cert": test_cert_pem},
    )
    assert r1.status_code == 200
    assert r1.json()["status"] == "ingested"

    r2 = await client.post(
        f"/api/v1/agents/{agent.id}/alerts",
        json={"client_message_id": msg_id, "alert_type": "USB", "severity": "High",
              "detected_at": datetime.now(UTC).isoformat(), "summary": "test again"},
        headers={"X-Client-Cert": test_cert_pem},
    )
    assert r2.status_code == 200
    assert r2.json()["status"] == "duplicate"
    assert r2.json()["alert_id"] == r1.json()["alert_id"]


@pytest.mark.asyncio
async def test_alert_invalid_severity_returns_422(
    client: AsyncClient, db_session: AsyncSession, test_cert_serial: str, test_cert_pem: str,
) -> None:
    """Invalid severity value should return 422."""
    tenant, agent = await _setup_agent_with_cert(db_session, test_cert_serial, f"sev-{uuid4().hex[:6]}")

    response = await client.post(
        f"/api/v1/agents/{agent.id}/alerts",
        json={"client_message_id": "m" * 20, "alert_type": "USB", "severity": "Extreme",
              "detected_at": datetime.now(UTC).isoformat(), "summary": "test"},
        headers={"X-Client-Cert": test_cert_pem},
    )
    assert response.status_code == 422


@pytest.mark.asyncio
async def test_agent_id_mismatch_returns_403(
    client: AsyncClient, db_session: AsyncSession, test_cert_serial: str, test_cert_pem: str,
) -> None:
    """Agent pushing to another agent's path should return 403."""
    tenant, agent = await _setup_agent_with_cert(db_session, test_cert_serial, f"mis-{uuid4().hex[:6]}")

    response = await client.post(
        "/api/v1/agents/wrong-agent-id/alerts",
        json={"client_message_id": "m" * 20, "alert_type": "USB", "severity": "High",
              "detected_at": datetime.now(UTC).isoformat(), "summary": "test"},
        headers={"X-Client-Cert": test_cert_pem},
    )
    assert response.status_code == 403
