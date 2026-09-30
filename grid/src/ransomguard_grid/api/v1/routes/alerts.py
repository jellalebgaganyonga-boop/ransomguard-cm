"""Alert ingestion endpoints — mTLS required."""

from datetime import UTC, datetime
from uuid import uuid4

from fastapi import APIRouter, Depends, HTTPException
from sqlalchemy.ext.asyncio import AsyncSession

from ransomguard_grid.api.v1.dependencies.auth import get_authenticated_agent
from ransomguard_grid.api.v1.schemas.alert import (
    AlertIngestRequest,
    AlertIngestResponse,
    BatchAlertIngestRequest,
    BatchAlertIngestResponse,
)
from ransomguard_grid.core.logging import get_logger
from ransomguard_grid.core.rate_limit import InMemoryRateLimiter, get_rate_limiter
from ransomguard_grid.db.models.agent import Agent
from ransomguard_grid.db.models.alerts import Alert, AlertArtifact, AlertDetail
from ransomguard_grid.db.models.notifications import NotificationPreference
from ransomguard_grid.db.models.tenant_user import User
from ransomguard_grid.db.repositories.alert_repository import AlertRepository
from ransomguard_grid.db.session import get_db
from ransomguard_grid.services.notification_queue import NotificationQueue, job_from_alert

logger = get_logger("alerts")
router = APIRouter(prefix="/agents/{agent_id}", tags=["alerts"])


async def _ingest_single_alert(
    agent: Agent,
    body: AlertIngestRequest,
    db: AsyncSession,
) -> AlertIngestResponse:
    """Ingest a single alert with idempotency check."""
    repo = AlertRepository(db, agent.tenant_id)

    existing = await repo.find_by_client_message_id(agent.id, body.client_message_id)
    if existing:
        return AlertIngestResponse(
            alert_id=existing.id,
            status="duplicate",
            ingested_at=existing.ingested_at,
        )

    alert_id = str(uuid4())
    now = datetime.now(UTC)

    alert = Alert(
        id=alert_id,
        tenant_id=agent.tenant_id,
        agent_id=agent.id,
        client_message_id=body.client_message_id,
        alert_type=body.alert_type,
        mitre_technique_id=body.mitre_technique_id,
        severity=body.severity,
        detected_at=body.detected_at,
        ingested_at=now,
        summary=body.summary,
        raw_payload_json=body.raw_payload,
    )
    db.add(alert)

    for detail in body.details:
        db.add(AlertDetail(
            id=str(uuid4()),
            tenant_id=agent.tenant_id,
            alert_id=alert_id,
            key=detail.key,
            value=detail.value,
        ))

    for artifact in body.artifacts:
        db.add(AlertArtifact(
            id=str(uuid4()),
            tenant_id=agent.tenant_id,
            alert_id=alert_id,
            artifact_type=artifact.artifact_type,
            artifact_hash_sha256=artifact.artifact_hash_sha256,
            artifact_metadata_json=artifact.artifact_metadata,
        ))

    await db.flush()

    # Fire-and-forget email notifications for Critical/High alerts
    severity_str = str(body.severity.value) if hasattr(body.severity, "value") else str(body.severity)
    if severity_str in ("Critical", "High"):
        try:
            await _notify_alert_subscribers(
                db=db,
                tenant_id=agent.tenant_id,
                agent_hostname=agent.hostname,
                alert_id=alert_id,
                alert_type=body.alert_type,
                severity=severity_str,
                summary=body.summary,
                detected_at=body.detected_at,
            )
        except Exception:
            logger.warning("Notification dispatch failed", alert_id=alert_id, exc_info=True)

    return AlertIngestResponse(alert_id=alert_id, status="ingested", ingested_at=now)


async def _notify_alert_subscribers(
    *,
    db: AsyncSession,
    tenant_id: str,
    agent_hostname: str,
    alert_id: str,
    alert_type: str,
    severity: str,
    summary: str,
    detected_at: datetime,
) -> None:
    """Queue one notification per opted-in recipient.

    This only writes to the queue. Sending happens in the grid-notifier service,
    because an SMTP relay can take seconds to answer and the agent posting this
    alert must not wait for it -- least of all during an attack. The queue also
    gives the delivery a retry path and an audit trail, which the previous
    in-request send (wrapped in a bare except) did not have.
    """
    from sqlalchemy import select

    stmt = select(NotificationPreference).where(NotificationPreference.tenant_id == tenant_id)
    prefs = (await db.execute(stmt)).scalars().all()
    if not prefs:
        return

    from ransomguard_grid.db.models.tenant_user import Tenant

    tenant = (await db.execute(select(Tenant).where(Tenant.id == tenant_id))).scalar_one_or_none()
    tenant_name = tenant.name if tenant else "Unknown"

    queue = NotificationQueue()
    queued = 0
    try:
        for pref in prefs:
            if not pref.should_notify(severity):
                continue
            user = (await db.execute(select(User).where(User.id == pref.user_id))).scalar_one_or_none()
            if not user or not user.is_active:
                continue

            await queue.publish(
                job_from_alert(
                    tenant_id=tenant_id,
                    tenant_name=tenant_name,
                    user_id=user.id,
                    to_email=user.email,
                    to_name=user.full_name,
                    alert_id=alert_id,
                    alert_type=alert_type,
                    severity=severity,
                    summary=summary,
                    agent_hostname=agent_hostname,
                    detected_at=detected_at,
                )
            )
            queued += 1
    finally:
        await queue.close()

    if queued:
        logger.info("Notifications queued", alert_id=alert_id, recipients=queued, severity=severity)


@router.post("/alerts", response_model=AlertIngestResponse)
async def ingest_alert(
    agent_id: str,
    body: AlertIngestRequest,
    agent: Agent = Depends(get_authenticated_agent),
    db: AsyncSession = Depends(get_db),
    limiter: InMemoryRateLimiter = Depends(get_rate_limiter),
) -> AlertIngestResponse:
    """Ingest a single alert from an authenticated agent."""
    if agent.id != agent_id:
        raise HTTPException(403, "Agent ID mismatch")

    if not await limiter.check_and_increment(f"alerts:{agent.id}", limit=60, window_seconds=60):
        raise HTTPException(429, "Rate limit exceeded: 60 alerts per minute")

    return await _ingest_single_alert(agent, body, db)


@router.post("/alerts/batch", response_model=BatchAlertIngestResponse)
async def ingest_alerts_batch(
    agent_id: str,
    body: BatchAlertIngestRequest,
    agent: Agent = Depends(get_authenticated_agent),
    db: AsyncSession = Depends(get_db),
    limiter: InMemoryRateLimiter = Depends(get_rate_limiter),
) -> BatchAlertIngestResponse:
    """Ingest up to 100 alerts in a single request."""
    if agent.id != agent_id:
        raise HTTPException(403, "Agent ID mismatch")

    if not await limiter.check_and_increment(f"alerts_batch:{agent.id}", limit=5, window_seconds=60):
        raise HTTPException(429, "Rate limit exceeded: 5 batches per minute")

    results = []
    for alert_req in body.alerts:
        result = await _ingest_single_alert(agent, alert_req, db)
        results.append(result)

    return BatchAlertIngestResponse(results=results)
