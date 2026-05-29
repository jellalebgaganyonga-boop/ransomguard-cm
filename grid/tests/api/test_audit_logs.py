"""Tests for audit log ingestion with real Ed25519 signature verification."""

import base64
import json
from datetime import UTC, datetime
from uuid import uuid4

import pytest
from cryptography.hazmat.primitives.asymmetric.ed25519 import Ed25519PrivateKey
from httpx import AsyncClient

from tests.conftest import AgentFixture


def _sign_and_make_entry(
    seq: int,
    payload: dict[str, object],
    private_key: Ed25519PrivateKey,
) -> dict[str, object]:
    """Create an audit log entry with real Ed25519 signature."""
    canonical = json.dumps(payload, sort_keys=True, separators=(",", ":"), ensure_ascii=False)
    signature = private_key.sign(canonical.encode("utf-8"))
    return {
        "sequence_number": seq,
        "payload": payload,
        "ed25519_signature_base64": base64.b64encode(signature).decode(),
        "signing_key_id": "test-key-1",
        "timestamp": datetime.now(UTC).isoformat(),
    }


@pytest.mark.asyncio
async def test_audit_log_valid_signature_accepted(
    client: AsyncClient, test_agent: AgentFixture,
) -> None:
    """Correctly signed audit log entry is accepted."""
    payload: dict[str, object] = {"action": "alert_created", "alert_id": str(uuid4())}
    entry = _sign_and_make_entry(1, payload, test_agent.ed25519_private_key)

    response = await client.post(
        f"/api/v1/agents/{test_agent.agent_id}/audit-logs",
        json={"entries": [entry]},
        headers={"X-Client-Cert": test_agent.cert_pem},
    )
    assert response.status_code == 200
    data = response.json()
    assert data["accepted_count"] == 1
    assert data["rejected_count"] == 0


@pytest.mark.asyncio
async def test_audit_log_invalid_signature_rejected(
    client: AsyncClient, test_agent: AgentFixture,
) -> None:
    """Tampered payload (signed then modified) is rejected."""
    original_payload: dict[str, object] = {"action": "alert_created", "alert_id": str(uuid4())}
    entry = _sign_and_make_entry(1, original_payload, test_agent.ed25519_private_key)

    # Tamper with payload AFTER signing
    entry["payload"] = {"action": "data_deletion", "alert_id": "tampered"}

    response = await client.post(
        f"/api/v1/agents/{test_agent.agent_id}/audit-logs",
        json={"entries": [entry]},
        headers={"X-Client-Cert": test_agent.cert_pem},
    )
    assert response.status_code == 409
    data = response.json()
    assert "Invalid Ed25519 signature" in str(data)


@pytest.mark.asyncio
async def test_audit_log_wrong_key_rejected(
    client: AsyncClient, test_agent: AgentFixture,
) -> None:
    """Signature from a different Ed25519 key is rejected."""
    wrong_key = Ed25519PrivateKey.generate()
    payload: dict[str, object] = {"action": "test"}
    entry = _sign_and_make_entry(1, payload, wrong_key)

    response = await client.post(
        f"/api/v1/agents/{test_agent.agent_id}/audit-logs",
        json={"entries": [entry]},
        headers={"X-Client-Cert": test_agent.cert_pem},
    )
    assert response.status_code == 409
    data = response.json()
    assert "Invalid Ed25519 signature" in str(data)


@pytest.mark.asyncio
async def test_audit_log_unicode_payload_verified(
    client: AsyncClient, test_agent: AgentFixture,
) -> None:
    """Canonical JSON handles unicode and nested structures correctly."""
    payload: dict[str, object] = {
        "message": "USB detectee avec caracteres speciaux",
        "files": ["file1.docx", "file2.pdf"],
        "metadata": {"size": 1024, "user": "Dr Mballa"},
    }
    entry = _sign_and_make_entry(1, payload, test_agent.ed25519_private_key)

    response = await client.post(
        f"/api/v1/agents/{test_agent.agent_id}/audit-logs",
        json={"entries": [entry]},
        headers={"X-Client-Cert": test_agent.cert_pem},
    )
    assert response.status_code == 200
    assert response.json()["accepted_count"] == 1


@pytest.mark.asyncio
async def test_audit_log_contiguous_sequence_accepted(
    client: AsyncClient, test_agent: AgentFixture,
) -> None:
    """Multiple entries with contiguous sequence numbers are accepted."""
    entries = []
    for seq in range(1, 4):
        payload: dict[str, object] = {"action": f"event-{seq}", "seq": seq}
        entries.append(_sign_and_make_entry(seq, payload, test_agent.ed25519_private_key))

    response = await client.post(
        f"/api/v1/agents/{test_agent.agent_id}/audit-logs",
        json={"entries": entries},
        headers={"X-Client-Cert": test_agent.cert_pem},
    )
    assert response.status_code == 200
    assert response.json()["accepted_count"] == 3


@pytest.mark.asyncio
async def test_audit_log_sequence_gap_rejected(
    client: AsyncClient, test_agent: AgentFixture,
) -> None:
    """Non-contiguous sequence number is rejected."""
    payload: dict[str, object] = {"action": "gap-test"}
    entry = _sign_and_make_entry(5, payload, test_agent.ed25519_private_key)  # gap: expected 1

    response = await client.post(
        f"/api/v1/agents/{test_agent.agent_id}/audit-logs",
        json={"entries": [entry]},
        headers={"X-Client-Cert": test_agent.cert_pem},
    )
    assert response.status_code == 409


@pytest.mark.asyncio
async def test_audit_log_requires_mtls(client: AsyncClient) -> None:
    """No cert should return 401."""
    response = await client.post(
        "/api/v1/agents/fake/audit-logs",
        json={"entries": [{"sequence_number": 1, "payload": {}, "ed25519_signature_base64": "dGVzdA==", "signing_key_id": "k", "timestamp": "2026-01-01T00:00:00Z"}]},
    )
    assert response.status_code == 401


@pytest.mark.asyncio
async def test_audit_log_agent_id_mismatch(
    client: AsyncClient, test_agent: AgentFixture,
) -> None:
    """Wrong agent_id in path returns 403."""
    payload: dict[str, object] = {"action": "test"}
    entry = _sign_and_make_entry(1, payload, test_agent.ed25519_private_key)

    response = await client.post(
        "/api/v1/agents/wrong-agent-id/audit-logs",
        json={"entries": [entry]},
        headers={"X-Client-Cert": test_agent.cert_pem},
    )
    assert response.status_code == 403
