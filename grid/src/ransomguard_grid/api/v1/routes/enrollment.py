"""Agent enrollment endpoint — NO mTLS required (agent doesn't have cert yet)."""

from datetime import UTC, datetime
from uuid import uuid4

from cryptography.hazmat.primitives.asymmetric.ed25519 import Ed25519PrivateKey
from cryptography.hazmat.primitives.serialization import Encoding, NoEncryption, PrivateFormat, PublicFormat
from fastapi import APIRouter, Depends, HTTPException
from sqlalchemy.ext.asyncio import AsyncSession

from ransomguard_grid.api.v1.schemas.agent import EnrollmentRequest, EnrollmentResponse
from ransomguard_grid.core.logging import get_logger
from ransomguard_grid.db.models.agent import Agent, AgentCertificate
from ransomguard_grid.db.models.enums import AgentStatus
from ransomguard_grid.db.repositories.agent_repository import AgentRepository
from ransomguard_grid.db.session import get_db
from ransomguard_grid.services.enrollment_otp_service import EnrollmentOtpService

logger = get_logger("enrollment")
router = APIRouter(prefix="/agents", tags=["enrollment"])

_otp_service = EnrollmentOtpService()


def get_otp_service() -> EnrollmentOtpService:
    """FastAPI dependency for OTP service."""
    return _otp_service


@router.post("/enroll", response_model=EnrollmentResponse)
async def enroll_agent(
    body: EnrollmentRequest,
    db: AsyncSession = Depends(get_db),
    otp_service: EnrollmentOtpService = Depends(get_otp_service),
) -> EnrollmentResponse:
    """Enroll a new agent using a one-time enrollment token."""
    otp = otp_service.validate_and_consume(body.otp)
    if otp is None:
        raise HTTPException(401, "Invalid or expired enrollment token")

    tenant_id = otp.tenant_id
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

    # Generate Ed25519 key pair for audit log signing
    ed25519_private_key = Ed25519PrivateKey.generate()
    ed25519_public_pem = ed25519_private_key.public_key().public_bytes(Encoding.PEM, PublicFormat.SubjectPublicKeyInfo).decode()
    ed25519_private_pem = ed25519_private_key.private_bytes(Encoding.PEM, PrivateFormat.PKCS8, NoEncryption()).decode()

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

    return EnrollmentResponse(
        agent_id=agent_id,
        tenant_id=tenant_id,
        message="Agent enrolled successfully",
        ed25519_private_key_pem=ed25519_private_pem,
    )
