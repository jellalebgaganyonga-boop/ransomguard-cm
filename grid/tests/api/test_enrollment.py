"""Tests for agent enrollment endpoint."""

import asyncio
import base64

import pytest
from cryptography.hazmat.primitives.asymmetric.ed25519 import Ed25519PrivateKey
from cryptography.hazmat.primitives.serialization import Encoding, PublicFormat
from httpx import AsyncClient

from ransomguard_grid.core.session_store import SessionStore
from ransomguard_grid.services.enrollment_otp_service import EnrollmentOtpService

# The agent's own audit-signing public key, sent at enrollment (GRID-SEC-003).
_PUBLIC_KEY = base64.b64encode(
    Ed25519PrivateKey.generate().public_key().public_bytes(Encoding.Raw, PublicFormat.Raw)
).decode()


@pytest.mark.asyncio
async def test_enrollment_with_valid_otp(client: AsyncClient, session_store: SessionStore) -> None:
    """Successful enrollment with valid OTP should return agent_id."""
    otp = await EnrollmentOtpService(session_store).generate("default", validity_minutes=5)

    response = await client.post("/api/v1/agents/enroll", json={
        "otp": otp,
        "hostname": "hospital-pc-01",
        "fqdn": "hospital-pc-01.cam.local",
        "os_version": "Windows 10 Pro 10.0.19045",
        "agent_version": "0.7.0",
        "hardware_fingerprint": "a" * 64,
        "ed25519_public_key_b64": _PUBLIC_KEY,
    })
    assert response.status_code == 200
    data = response.json()
    assert "agent_id" in data
    assert data["tenant_id"] == "default"


@pytest.mark.asyncio
async def test_enrollment_with_expired_otp(client: AsyncClient, session_store: SessionStore) -> None:
    """Expired OTP should return 401 (Redis TTL)."""
    otp = "RG-CAM-2026-EXPIRD"
    await session_store.store_otp(otp, "default", ttl_seconds=1)
    await asyncio.sleep(1.2)

    response = await client.post("/api/v1/agents/enroll", json={
        "otp": otp,
        "hostname": "h",
        "fqdn": "h.local",
        "os_version": "10",
        "agent_version": "1.0.0",
        "hardware_fingerprint": "b" * 64,
        "ed25519_public_key_b64": _PUBLIC_KEY,
    })
    assert response.status_code == 401


@pytest.mark.asyncio
async def test_enrollment_with_invalid_otp(client: AsyncClient) -> None:
    """Invalid OTP should return 401."""
    response = await client.post("/api/v1/agents/enroll", json={
        "otp": "RG-FAKE-2026-XXXXXX",
        "hostname": "h",
        "fqdn": "h.local",
        "os_version": "10",
        "agent_version": "1.0.0",
        "hardware_fingerprint": "c" * 64,
        "ed25519_public_key_b64": _PUBLIC_KEY,
    })
    assert response.status_code == 401


@pytest.mark.asyncio
async def test_enrollment_duplicate_fingerprint_rejected(client: AsyncClient, session_store: SessionStore) -> None:
    """Re-enrolling same fingerprint should return 409."""
    _otp_service = EnrollmentOtpService(session_store)

    otp1 = await _otp_service.generate("default", validity_minutes=5)
    fp = "d" * 64

    r1 = await client.post("/api/v1/agents/enroll", json={
        "otp": otp1, "hostname": "h1", "fqdn": "h1.local",
        "os_version": "10", "agent_version": "1.0.0", "hardware_fingerprint": fp,
        "ed25519_public_key_b64": _PUBLIC_KEY,
    })
    assert r1.status_code == 200

    otp2 = await _otp_service.generate("default", validity_minutes=5)
    r2 = await client.post("/api/v1/agents/enroll", json={
        "otp": otp2, "hostname": "h2", "fqdn": "h2.local",
        "os_version": "10", "agent_version": "1.0.0", "hardware_fingerprint": fp,
        "ed25519_public_key_b64": _PUBLIC_KEY,
    })
    assert r2.status_code == 409


@pytest.mark.asyncio
async def test_enrollment_invalid_body_returns_422(client: AsyncClient) -> None:
    """Missing required fields should return 422."""
    response = await client.post("/api/v1/agents/enroll", json={"otp": "short"})
    assert response.status_code == 422
