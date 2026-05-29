"""Audit log ingestion endpoint — mTLS required, Ed25519 verified, sequence checked."""

import base64
from uuid import uuid4

from fastapi import APIRouter, Depends, HTTPException
from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from ransomguard_grid.api.v1.dependencies.auth import get_authenticated_agent
from ransomguard_grid.api.v1.schemas.audit_log import AuditLogBatchRequest, AuditLogBatchResponse
from ransomguard_grid.core.ed25519 import verify_ed25519_signature
from ransomguard_grid.core.logging import get_logger
from ransomguard_grid.db.models.agent import Agent, AgentCertificate
from ransomguard_grid.db.models.alerts import AuditLog
from ransomguard_grid.db.repositories.audit_log_repository import AuditLogRepository
from ransomguard_grid.db.session import get_db

logger = get_logger("audit_log")
router = APIRouter(prefix="/agents/{agent_id}", tags=["audit-log"])


async def _get_agent_ed25519_public_key(db: AsyncSession, agent_id: str) -> str | None:
    """Get the Ed25519 public key PEM for an agent from its active certificate."""
    stmt = (
        select(AgentCertificate.ed25519_public_key_pem)
        .where(
            AgentCertificate.agent_id == agent_id,
            AgentCertificate.revoked_at.is_(None),
            AgentCertificate.ed25519_public_key_pem.isnot(None),
        )
        .limit(1)
    )
    result = await db.execute(stmt)
    return result.scalar_one_or_none()


@router.post("/audit-logs", response_model=AuditLogBatchResponse)
async def ingest_audit_logs(
    agent_id: str,
    body: AuditLogBatchRequest,
    agent: Agent = Depends(get_authenticated_agent),
    db: AsyncSession = Depends(get_db),
) -> AuditLogBatchResponse:
    """Ingest Ed25519-signed audit log entries with signature and sequence verification."""
    if agent.id != agent_id:
        raise HTTPException(403, "Agent ID mismatch")

    # Get agent's Ed25519 public key for signature verification
    public_key_pem = await _get_agent_ed25519_public_key(db, agent.id)
    if not public_key_pem:
        raise HTTPException(401, "No Ed25519 public key registered for agent")

    repo = AuditLogRepository(db, agent.tenant_id)
    accepted = 0
    rejected = 0
    rejected_reasons: list[str] = []

    for entry in body.entries:
        # Decode base64 signature
        try:
            base64.b64decode(entry.ed25519_signature_base64)
        except Exception:
            rejected += 1
            rejected_reasons.append(f"seq={entry.sequence_number}: invalid base64 signature")
            continue

        # CRITICAL: Verify Ed25519 signature over canonical JSON payload
        if not verify_ed25519_signature(
            payload=entry.payload,
            signature_base64=entry.ed25519_signature_base64,
            public_key_pem=public_key_pem,
        ):
            rejected += 1
            rejected_reasons.append(f"seq={entry.sequence_number}: Invalid Ed25519 signature")
            logger.warning(
                "Audit log signature verification FAILED",
                agent_id=agent_id,
                sequence_number=entry.sequence_number,
            )
            continue

        sig_bytes = base64.b64decode(entry.ed25519_signature_base64)

        # Persist with sequence check
        audit_entry = AuditLog(
            id=str(uuid4()),
            tenant_id=agent.tenant_id,
            agent_id=agent.id,
            sequence_number=entry.sequence_number,
            payload_json=entry.payload,
            ed25519_signature=sig_bytes,
            signing_key_id=entry.signing_key_id,
        )

        try:
            await repo.append_with_sequence_check(audit_entry)
            accepted += 1
        except ValueError as e:
            rejected += 1
            rejected_reasons.append(str(e))
            logger.warning("Audit log sequence gap", agent_id=agent_id, error=str(e))

    if rejected > 0 and accepted == 0:
        raise HTTPException(409, detail={"message": "All entries rejected", "reasons": rejected_reasons})

    return AuditLogBatchResponse(
        accepted_count=accepted,
        rejected_count=rejected,
        rejected_reasons=rejected_reasons,
    )
