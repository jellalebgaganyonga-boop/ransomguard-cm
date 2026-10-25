"""Alert ingestion endpoints — mTLS required."""

import json
from collections.abc import Callable
from datetime import UTC, datetime
from uuid import uuid4

from fastapi import APIRouter, Depends, HTTPException
from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from ransomguard_grid.api.v1.dependencies.auth import get_authenticated_agent
from ransomguard_grid.api.v1.schemas.alert import (
    ATTRIBUTION_FINAL_STATES,
    AlertIngestRequest,
    AlertIngestResponse,
    AttributionComplementRequest,
    AttributionComplementResponse,
    GenealogyComplementRequest,
    GenealogyComplementResponse,
    BatchAlertIngestRequest,
    BatchAlertIngestResponse,
)
from ransomguard_grid.core.alert_modules import UNKNOWN, module_for
from ransomguard_grid.core.hourly_system_log import hourly_log_id, record_hourly
from ransomguard_grid.core.logging import get_logger
from ransomguard_grid.core.rate_limit import InMemoryRateLimiter, get_rate_limiter
from ransomguard_grid.db.models.agent import Agent
from ransomguard_grid.db.models.alerts import Alert, AlertArtifact, AlertDetail
from ransomguard_grid.db.models.enums import LogLevel
from ransomguard_grid.db.models.notifications import NotificationPreference
from ransomguard_grid.db.models.operations import SystemLog
from ransomguard_grid.db.models.tenant_user import User
from ransomguard_grid.db.repositories.alert_repository import AlertRepository
from ransomguard_grid.db.session import get_db
from ransomguard_grid.services.notification_queue import NotificationQueue, job_from_alert

logger = get_logger("alerts")
router = APIRouter(prefix="/agents/{agent_id}", tags=["alerts"])

_UNMAPPED_COMPONENT = "alert-ingest"


def unmapped_alert_type_log_id(tenant_id: str, alert_type: str, window_start: datetime) -> str:
    """Id of THE SystemLog entry for (tenant, alert_type, hour): one entry, whatever the rate."""
    return hourly_log_id(_UNMAPPED_COMPONENT, tenant_id, alert_type, window_start)


def _unmapped_message(alert_type: str) -> Callable[[int, datetime], str]:
    # repr(): an agent-supplied value never puts a raw newline or control character in the log.
    return lambda count, window_start: (
        f"alert_type {alert_type!r} maps to no module: shown as UNKNOWN "
        f"({count} alert(s) since {window_start:%Y-%m-%d %H:%M} UTC)"
    )


async def record_unmapped_alert_type(
    db: AsyncSession, tenant_id: str, agent_id: str, alert_type: str, alert_id: str, now: datetime,
) -> None:
    """An alert_type with no module gets in (UNKNOWN), never silently -- and never one log per alert:
    one SystemLog entry per (tenant, alert_type) and per UTC hour, carrying the count, and at most
    20 such entries per tenant and hour, all types together (core/hourly_system_log.py).
    """
    await record_hourly(
        db,
        tenant_id=tenant_id,
        component=_UNMAPPED_COMPONENT,
        family="unknown_alert_type",
        key=alert_type,
        message=_unmapped_message(alert_type),
        first_context={"alert_type": alert_type, "first_alert_id": alert_id},
        latest_context={"last_alert_id": alert_id, "last_agent_id": agent_id},
        now=now,
        log_event="alert_type_without_module",
    )


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

    # An alert_type with no module still gets in (UNKNOWN), but never silently.
    if module_for(body.alert_type) == UNKNOWN:
        await record_unmapped_alert_type(db, agent.tenant_id, agent.id, body.alert_type, alert_id, now)

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


# Detail keys the attribution complement writes on the alert (ADR-025, section 3).
_ATTRIBUTION_KEYS = ("attribution_state", "attribution_reason", "process_id", "process_created_at",
                     "process_name", "process_path")
_ATTRIBUTION_COMPONENT = "alert-attribution"


@router.post(
    "/alerts/{client_message_id}/attribution",
    response_model=AttributionComplementResponse,
)
async def attribute_alert(
    agent_id: str,
    client_message_id: str,
    body: AttributionComplementRequest,
    agent: Agent = Depends(get_authenticated_agent),
    db: AsyncSession = Depends(get_db),
    limiter: InMemoryRateLimiter = Depends(get_rate_limiter),
) -> AttributionComplementResponse:
    """The attribution complement of an alert this agent sent (ADR-025, section 3).

    Partitioned like the alerts themselves:
      * the agent is the authenticated one (get_authenticated_agent: its certificate in mtls mode);
        a path id that is not its own is 403;
      * the alert is looked up by client_message_id AND agent_id AND tenant_id; an alert of another
        agent or another tenant is 404, exactly as one that does not exist -- nothing is revealed;
      * once the attribution is final, a later complement changes nothing (idempotent) and is logged.
    """
    if agent.id != agent_id:
        raise HTTPException(403, "Agent ID mismatch")

    if not await limiter.check_and_increment(f"attribution:{agent.id}", limit=60, window_seconds=60):
        raise HTTPException(429, "Rate limit exceeded: 60 attributions per minute")

    alert = await AlertRepository(db, agent.tenant_id).find_by_client_message_id(agent.id, client_message_id)
    if alert is None:
        raise HTTPException(404, "Alert not found")

    details = (await db.execute(select(AlertDetail).where(
        AlertDetail.tenant_id == agent.tenant_id,
        AlertDetail.alert_id == alert.id,
        AlertDetail.key.in_(_ATTRIBUTION_KEYS),
    ))).scalars().all()
    current = next((d.value for d in details if d.key == "attribution_state"), None)
    now = datetime.now(UTC)

    if current in ATTRIBUTION_FINAL_STATES:
        _log_attribution(db, agent, alert.id, "AlertAttributionIgnored", body.state, now, already=current)
        return AttributionComplementResponse(alert_id=alert.id, status="unchanged")

    values = {
        "attribution_state": body.state,
        "attribution_reason": body.reason,
        "process_id": str(body.pid) if body.pid is not None else None,
        "process_created_at": body.process_created_at.isoformat() if body.process_created_at else None,
        "process_name": body.process_name,
        "process_path": body.image_path,
    }
    for detail in details:
        await db.delete(detail)
    for key, value in values.items():
        if value is not None:
            db.add(AlertDetail(id=str(uuid4()), tenant_id=agent.tenant_id, alert_id=alert.id, key=key, value=value))

    _log_attribution(db, agent, alert.id, "AlertAttributionApplied", body.state, now, previous=current)
    await db.flush()
    return AttributionComplementResponse(alert_id=alert.id, status="applied")


# Detail keys the genealogy complement writes on the alert. The chain and the patterns are
# JSON in one detail each rather than a row per process: the console reads the whole chain or
# none of it, and a row per process would have to be re-assembled in the right order anyway.
_GENEALOGY_KEYS = ("genealogy_chain", "genealogy_tree", "genealogy_patterns")
_GENEALOGY_COMPONENT = "alert-genealogy"


@router.post(
    "/alerts/{client_message_id}/genealogy",
    response_model=GenealogyComplementResponse,
)
async def genealogy_for_alert(
    agent_id: str,
    client_message_id: str,
    body: GenealogyComplementRequest,
    agent: Agent = Depends(get_authenticated_agent),
    db: AsyncSession = Depends(get_db),
    limiter: InMemoryRateLimiter = Depends(get_rate_limiter),
) -> GenealogyComplementResponse:
    """The chain of processes behind an alert this agent sent.

    Partitioned exactly like the attribution complement: the agent must be the authenticated one,
    and the alert is found by client_message_id AND agent_id AND tenant_id, so an alert belonging
    to another agent or another tenant is 404 and not a hint that it exists.

    A second complement for the same alert replaces the first. The chain is rebuilt from the
    ancestry the agent captured, and a later one is the better reading, not a duplicate.
    """
    if agent.id != agent_id:
        raise HTTPException(403, "Agent ID mismatch")

    if not await limiter.check_and_increment(f"genealogy:{agent.id}", limit=60, window_seconds=60):
        raise HTTPException(429, "Rate limit exceeded: 60 genealogy complements per minute")

    alert = await AlertRepository(db, agent.tenant_id).find_by_client_message_id(agent.id, client_message_id)
    if alert is None:
        raise HTTPException(404, "Alert not found")

    chain = " -> ".join([p.name for p in reversed(body.ancestors)] + [body.writer.name])
    values = {
        "genealogy_chain": chain,
        "genealogy_tree": json.dumps({
            "writer": body.writer.model_dump(mode="json"),
            "ancestors": [a.model_dump(mode="json") for a in body.ancestors],
        }),
        "genealogy_patterns": json.dumps([p.model_dump(mode="json") for p in body.patterns]),
    }

    existing = (await db.execute(select(AlertDetail).where(
        AlertDetail.tenant_id == agent.tenant_id,
        AlertDetail.alert_id == alert.id,
        AlertDetail.key.in_(_GENEALOGY_KEYS),
    ))).scalars().all()
    for detail in existing:
        await db.delete(detail)

    for key, value in values.items():
        db.add(AlertDetail(id=str(uuid4()), tenant_id=agent.tenant_id, alert_id=alert.id, key=key, value=value))

    db.add(SystemLog(
        id=str(uuid4()),
        tenant_id=agent.tenant_id,
        level=LogLevel.Info,
        component=_GENEALOGY_COMPONENT,
        message=f"AlertGenealogyApplied: alert {alert.id}, chain {chain}",
        context_json={
            "event": "AlertGenealogyApplied", "alert_id": alert.id, "agent_id": agent.id,
            "chain": chain, "patterns": len(body.patterns),
        },
        created_at=datetime.now(UTC),
    ))
    logger.info("AlertGenealogyApplied", alert_id=alert.id, agent_id=agent.id, patterns=len(body.patterns))

    await db.flush()
    return GenealogyComplementResponse(alert_id=alert.id, status="applied")


def _log_attribution(
    db: AsyncSession, agent: Agent, alert_id: str, event: str, state: str, now: datetime, **context: str | None,
) -> None:
    db.add(SystemLog(
        id=str(uuid4()),
        tenant_id=agent.tenant_id,
        level=LogLevel.Info if event == "AlertAttributionApplied" else LogLevel.Warning,
        component=_ATTRIBUTION_COMPONENT,
        message=f"{event}: alert {alert_id}, complement {state}",
        context_json={"event": event, "alert_id": alert_id, "agent_id": agent.id, "state": state, **context},
        created_at=now,
    ))
    logger.info(event, alert_id=alert_id, agent_id=agent.id, state=state)
