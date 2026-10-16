"""Agent enrollment endpoint — NO mTLS required (agent doesn't have cert yet)."""

import base64
from datetime import UTC, datetime
from uuid import uuid4

from cryptography.hazmat.primitives.asymmetric.ed25519 import Ed25519PublicKey
from cryptography.hazmat.primitives.serialization import Encoding, PublicFormat
from fastapi import APIRouter, Depends, HTTPException
from sqlalchemy.ext.asyncio import AsyncSession

from ransomguard_grid.api.v1.schemas.agent import EnrollmentRequest, EnrollmentResponse
from ransomguard_grid.core.logging import get_logger
from ransomguard_grid.core.session_store import SessionStore, SessionStoreUnavailableError, get_session_store
from ransomguard_grid.db.models.agent import Agent, AgentCertificate
from ransomguard_grid.db.models.enums import AgentStatus
from ransomguard_grid.db.repositories.agent_repository import AgentRepository
from ransomguard_grid.db.session import get_db
from ransomguard_grid.services.enrollment_otp_service import EnrollmentOtpService

logger = get_logger("enrollment")
router = APIRouter(prefix="/agents", tags=["enrollment"])


def get_otp_service(store: SessionStore = Depends(get_session_store)) -> EnrollmentOtpService:
    """FastAPI dependency: OTPs live in the shared session store (Redis), seen by every worker."""
    return EnrollmentOtpService(store)


@router.post("/enroll", response_model=EnrollmentResponse)
async def enroll_agent(
    body: EnrollmentRequest,
    db: AsyncSession = Depends(get_db),
    otp_service: EnrollmentOtpService = Depends(get_otp_service),
) -> EnrollmentResponse:
    """Enroll a new agent using a one-time enrollment token."""
    try:
        tenant_id = await otp_service.validate_and_consume(body.otp)
    except SessionStoreUnavailableError:
        raise HTTPException(503, "Session store unavailable") from None
    if tenant_id is None:
        raise HTTPException(401, "Invalid or expired enrollment token")

    agent_repo = AgentRepository(db, tenant_id)

    existing = await agent_repo.find_by_fingerprint(body.hardware_fingerprint)
    if existing:
        raise HTTPException(409, f"Agent with fingerprint already enrolled: {existing.id}")

    agent_id = str(uuid4())
    now = datetime.now(UTC)

    agent = Agent(
        id=agent_id,
        tenant_id=tenant_id,
        hostname=body.hostname,
        fqdn=body.fqdn,
        os_version=body.os_version,
        agent_version=body.agent_version,
        hardware_fingerprint=body.hardware_fingerprint,
        status=AgentStatus.active,
        enrolled_at=now,
        created_at=now,
    )
    db.add(agent)

    # The agent's own audit-signing public key, stored as PEM for the audit-log verification. The
    # GRID generates no key for the agent and never holds a private key (GRID-SEC-003).
    ed25519_public_pem = (
        Ed25519PublicKey.from_public_bytes(base64.b64decode(body.ed25519_public_key_b64))
        .public_bytes(Encoding.PEM, PublicFormat.SubjectPublicKeyInfo)
        .decode()
    )

    cert = AgentCertificate(
        id=str(uuid4()),
        agent_id=agent_id,
        serial_number=uuid4().hex.upper(),
        fingerprint_sha256=uuid4().hex + uuid4().hex,
        not_before=now,
        not_after=datetime(now.year + 1, now.month, now.day, tzinfo=UTC),
        ed25519_public_key_pem=ed25519_public_pem,
    )
    db.add(cert)
    await db.flush()

    logger.info("Agent enrolled", agent_id=agent_id, tenant_id=tenant_id, hostname=body.hostname)

    return EnrollmentResponse(agent_id=agent_id, tenant_id=tenant_id, message="Agent enrolled successfully")
