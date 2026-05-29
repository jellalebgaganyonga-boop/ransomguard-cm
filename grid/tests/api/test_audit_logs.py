"""Tests for audit log ingestion endpoint."""

import base64
from datetime import UTC, datetime
from uuid import uuid4

import pytest
from httpx import AsyncClient
from sqlalchemy.ext.asyncio import AsyncSession

from ransomguard_grid.db.models.agent import Agent, AgentCertificate
from ransomguard_grid.db.models.enums import AgentStatus, TenantStatus
from ransomguard_grid.db.models.tenant_user import Tenant


async def _setup(db: AsyncSession, serial: str) -> tuple[Tenant, Agent]:
    """Create tenant + agent + cert for test."""
    tenant = Tenant(id=str(uuid4()), name="T", code=f"al-{uuid4().hex[:6]}", contact_email="t@t.local", status=TenantStatus.active)
    db.add(tenant)
    agent = Agent(id=str(uuid4()), tenant_id=tenant.id, hostname="h", fqdn="h.local", os_version="10", agent_version="1.0", hardware_fingerprint=f"fp-al-{uuid4().hex[:6]}", status=AgentStatus.active)
    db.add(agent)
    cert = AgentCertificate(id=str(uuid4()), agent_id=agent.id, serial_number=serial, fingerprint_sha256="f" * 64, not_before=datetime.now(UTC), not_after=datetime(2027, 1, 1, tzinfo=UTC))
    db.add(cert)
    await db.flush()
    await db.commit()
    return tenant, agent


def _make_entry(seq: int) -> dict:
    """Create a valid audit log entry dict."""
    sig = base64.b64encode(b"test-signature-placeholder").decode()
    return {
        "sequence_number": seq,
        "payload": {"action": "test", "details": f"entry-{seq}"},
        "ed25519_signature_base64": sig,
        "signing_key_id": "key-001",
        "timestamp": datetime.now(UTC).isoformat(),
    }


@pytest.mark.asyncio
async def test_audit_log_accepted_with_valid_sequence(
    client: AsyncClient, db_session: AsyncSession, test_cert_serial: str, test_cert_pem: str,
) -> None:
    """Contiguous sequence numbers should be accepted."""
    _, agent = await _setup(db_session, test_cert_serial)

    response = await client.post(
        f"/api/v1/agents/{agent.id}/audit-logs",
        json={"entries": [_make_entry(1), _make_entry(2), _make_entry(3)]},
        headers={"X-Client-Cert": test_cert_pem},
    )
    assert response.status_code == 200
    data = response.json()
    assert data["accepted_count"] == 3
    assert data["rejected_count"] == 0


@pytest.mark.asyncio
async def test_audit_log_sequence_gap_returns_409(
    client: AsyncClient, db_session: AsyncSession, test_cert_serial: str, test_cert_pem: str,
) -> None:
    """Non-contiguous sequence number should be rejected."""
    _, agent = await _setup(db_session, test_cert_serial)

    response = await client.post(
        f"/api/v1/agents/{agent.id}/audit-logs",
        json={"entries": [_make_entry(5)]},  # gap: expected 1, got 5
        headers={"X-Client-Cert": test_cert_pem},
    )
    assert response.status_code == 409


@pytest.mark.asyncio
async def test_audit_log_invalid_signature_rejected(
    client: AsyncClient, db_session: AsyncSession, test_cert_serial: str, test_cert_pem: str,
) -> None:
    """Invalid base64 signature should be rejected."""
    _, agent = await _setup(db_session, test_cert_serial)

    bad_entry = _make_entry(1)
    bad_entry["ed25519_signature_base64"] = "NOT-VALID-BASE64!!!"

    response = await client.post(
        f"/api/v1/agents/{agent.id}/audit-logs",
        json={"entries": [bad_entry]},
        headers={"X-Client-Cert": test_cert_pem},
    )
    assert response.status_code == 409
    data = response.json()
    assert "invalid base64" in str(data).lower()


@pytest.mark.asyncio
async def test_audit_log_requires_mtls(client: AsyncClient) -> None:
    """No cert should return 401."""
    response = await client.post("/api/v1/agents/fake/audit-logs", json={"entries": []})
    assert response.status_code in (401, 422)


@pytest.mark.asyncio
async def test_audit_log_agent_id_mismatch(
    client: AsyncClient, db_session: AsyncSession, test_cert_serial: str, test_cert_pem: str,
) -> None:
    """Wrong agent_id in path should return 403."""
    await _setup(db_session, test_cert_serial)

    response = await client.post(
        "/api/v1/agents/wrong-agent/audit-logs",
        json={"entries": [_make_entry(1)]},
        headers={"X-Client-Cert": test_cert_pem},
    )
    assert response.status_code == 403
