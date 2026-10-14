"""GRID-SEC-003: the audit-signing key belongs to the agent; the GRID never generates nor sends one."""

import base64
import json
from datetime import UTC, datetime
from uuid import uuid4

import pytest
from cryptography.hazmat.primitives.asymmetric.ed25519 import Ed25519PrivateKey
from cryptography.hazmat.primitives.serialization import Encoding, PublicFormat
from httpx import AsyncClient

from ransomguard_grid.api.v1.schemas.agent import EnrollmentResponse
from ransomguard_grid.core.session_store import SessionStore
from ransomguard_grid.services.enrollment_otp_service import EnrollmentOtpService


def _raw_public_b64(key: Ed25519PrivateKey) -> str:
    return base64.b64encode(key.public_key().public_bytes(Encoding.Raw, PublicFormat.Raw)).decode()


async def _enroll(client: AsyncClient, store: SessionStore, public_key_b64: str) -> dict[str, object]:
    otp = await EnrollmentOtpService(store).generate("default", validity_minutes=5)
    response = await client.post("/api/v1/agents/enroll", json={
        "otp": otp, "hostname": "key-pc", "fqdn": "key-pc.local", "os_version": "10",
        "agent_version": "0.4.0", "hardware_fingerprint": uuid4().hex + uuid4().hex,
        "ed25519_public_key_b64": public_key_b64,
    })
    assert response.status_code == 200, response.text
    body: dict[str, object] = response.json()
    return body


def _entry(seq: int, key: Ed25519PrivateKey) -> dict[str, object]:
    payload: dict[str, object] = {"action": "CanaryAlert", "id": str(uuid4())}
    canonical = json.dumps(payload, sort_keys=True, separators=(",", ":"), ensure_ascii=False)
    return {
        "sequence_number": seq,
        "payload": payload,
        "ed25519_signature_base64": base64.b64encode(key.sign(canonical.encode())).decode(),
        "signing_key_id": "agent-audit-key",
        "timestamp": datetime.now(UTC).isoformat(),
    }


def test_the_enrollment_response_schema_carries_no_key_material() -> None:
    fields = set(EnrollmentResponse.model_fields)
    assert fields == {"agent_id", "tenant_id", "message"}
    assert not any("key" in name or "private" in name for name in fields)


@pytest.mark.asyncio
async def test_the_enrollment_response_contains_no_private_key(client: AsyncClient, session_store: SessionStore) -> None:
    body = await _enroll(client, session_store, _raw_public_b64(Ed25519PrivateKey.generate()))

    assert set(body) == {"agent_id", "tenant_id", "message"}
    assert "PRIVATE KEY" not in json.dumps(body)


@pytest.mark.asyncio
async def test_an_entry_signed_by_the_agent_is_verified_with_the_public_key_it_sent(client: AsyncClient, session_store: SessionStore) -> None:
    agent_key = Ed25519PrivateKey.generate()  # generated on the endpoint, never sent
    agent_id = (await _enroll(client, session_store, _raw_public_b64(agent_key)))["agent_id"]

    response = await client.post(f"/api/v1/agents/{agent_id}/audit-logs", json={"entries": [_entry(1, agent_key)]})

    assert response.status_code == 200
    assert (response.json()["accepted_count"], response.json()["rejected_count"]) == (1, 0)


@pytest.mark.asyncio
async def test_an_entry_signed_by_another_key_is_refused(client: AsyncClient, session_store: SessionStore) -> None:
    agent_key = Ed25519PrivateKey.generate()
    agent_id = (await _enroll(client, session_store, _raw_public_b64(agent_key)))["agent_id"]

    forged = _entry(1, Ed25519PrivateKey.generate())  # e.g. a key the GRID would have made up
    response = await client.post(f"/api/v1/agents/{agent_id}/audit-logs", json={"entries": [forged]})

    # Every entry refused: the route answers 409 with the reasons.
    assert response.status_code == 409
    assert "Invalid Ed25519 signature" in response.json()["detail"]["reasons"][0]


@pytest.mark.asyncio
@pytest.mark.parametrize(
    "bad_key",
    [
        "",
        "not base64 at all !!!!!!!!!!!!!!!!!!!!!!!!!!",
        base64.b64encode(b"x" * 31).decode().ljust(44, "="),  # 31 bytes
        base64.b64encode(b"x" * 33).decode()[:44],
    ],
)
async def test_an_enrollment_without_a_valid_ed25519_public_key_is_refused(
    client: AsyncClient, session_store: SessionStore, bad_key: str,
) -> None:
    otp = await EnrollmentOtpService(session_store).generate("default", validity_minutes=5)
    response = await client.post("/api/v1/agents/enroll", json={
        "otp": otp, "hostname": "h", "fqdn": "h.local", "os_version": "10", "agent_version": "0.4.0",
        "hardware_fingerprint": "9" * 64, "ed25519_public_key_b64": bad_key,
    })

    assert response.status_code == 422
