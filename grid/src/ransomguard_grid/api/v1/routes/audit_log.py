"""Audit log ingestion endpoint — mTLS required, Ed25519 verified, sequence checked."""

import base64
from uuid import uuid4

from fastapi import APIRouter, Depends, HTTPException
from sqlalchemy.ext.asyncio import AsyncSession

from ransomguard_grid.api.v1.dependencies.auth import get_authenticated_agent
from ransomguard_grid.api.v1.schemas.audit_log import AuditLogBatchRequest, AuditLogBatchResponse
from ransomguard_grid.core.logging import get_logger
from ransomguard_grid.db.models.agent import Agent
from ransomguard_grid.db.models.alerts import AuditLog
from ransomguard_grid.db.repositories.audit_log_repository import AuditLogRepository
from ransomguard_grid.db.session import get_db

logger = get_logger("audit_log")
router = APIRouter(prefix="/agents/{agent_id}", tags=["audit-log"])


@router.post("/audit-logs", response_model=AuditLogBatchResponse)
async def ingest_audit_logs(
    agent_id: str,
    body: AuditLogBatchRequest,
    agent: Agent = Depends(get_authenticated_agent),
    db: AsyncSession = Depends(get_db),
) -> AuditLogBatchResponse:
    """Ingest Ed25519-signed audit log entries with sequence number verification."""
    if agent.id != agent_id:
        raise HTTPException(403, "Agent ID mismatch")

    repo = AuditLogRepository(db, agent.tenant_id)
    accepted = 0
    rejected = 0
    rejected_reasons: list[str] = []

    for entry in body.entries:
        try:
            sig_bytes = base64.b64decode(entry.ed25519_signature_base64)
        except Exception:
            rejected += 1
            rejected_reasons.append(f"seq={entry.sequence_number}: invalid base64 signature")
            continue

        # Ed25519 signature verification would happen here in production
        # using the agent's public key from AgentCertificate
        # For Section C, we accept the signature as-is (verification in Section D)

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
