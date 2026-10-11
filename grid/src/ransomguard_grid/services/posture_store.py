"""Storing what a heartbeat says, and reading it back for the verdict.

Every write and every read is keyed on agent_id AND tenant_id. The agent comes from the
authenticated certificate (never from the request body), and its tenant from that agent.
"""

from collections.abc import Sequence
from datetime import datetime
from uuid import uuid4

from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from ransomguard_grid.api.v1.schemas.agent import HeartbeatRequest
from ransomguard_grid.core.hourly_system_log import record_hourly
from ransomguard_grid.db.models.agent import Agent, AgentModuleStatus, AgentPosture, AgentPostureChange
from ransomguard_grid.services.agent_posture import MODULE_CODES, ModuleStatus, PostureSnapshot

_HEARTBEAT_COMPONENT = "heartbeat"
NOT_REPORTED = "not_reported"


async def record_heartbeat(db: AsyncSession, agent: Agent, body: HeartbeatRequest, now: datetime) -> None:
    """Upsert the posture and the module states; write a transition only when a module changes."""
    posture = (
        await db.execute(
            select(AgentPosture).where(AgentPosture.agent_id == agent.id, AgentPosture.tenant_id == agent.tenant_id)
        )
    ).scalar_one_or_none()
    if posture is None:
        posture = AgentPosture(agent_id=agent.id, tenant_id=agent.tenant_id)
        db.add(posture)

    posture.schema_version = body.schema_version
    posture.agent_uptime_seconds = body.agent_uptime_seconds
    posture.threat_intel_version = body.threat_intel_version
    posture.received_at = now

    disk, transmission = body.disk, body.transmission
    if body.schema_version < 2:
        # v1: no posture. Its free-form modules_status dict is ignored, without a log: a v1 agent
        # heartbeats every minute, and "agent update required" is already what the verdict says.
        disk = transmission = None

    posture.last_detection_at = body.last_detection_at if body.schema_version >= 2 else None
    posture.system_free_bytes = disk.system_free_bytes if disk else None
    posture.db_volume_free_bytes = disk.db_volume_free_bytes if disk else None
    posture.pending_alerts = transmission.pending_alerts if transmission else None
    posture.last_upload_ok_at = transmission.last_upload_ok_at if transmission else None
    posture.last_upload_ok_age_seconds = transmission.last_upload_ok_age_seconds if transmission else None
    posture.last_upload_error_code = transmission.last_upload_error_code if transmission else None
    posture.rejected_alerts = transmission.rejected_alerts if transmission else None

    if body.schema_version < 2:
        return

    # Identity of the endpoint: Windows changes version after updates, the agent after upgrades.
    if body.agent_version:
        agent.agent_version = body.agent_version
    if body.os_version:
        agent.os_version = body.os_version

    stored = {
        row.module_code: row
        for row in (
            await db.execute(
                select(AgentModuleStatus).where(
                    AgentModuleStatus.agent_id == agent.id, AgentModuleStatus.tenant_id == agent.tenant_id
                )
            )
        ).scalars()
    }

    reported: set[str] = set()
    for module in body.modules:
        if module.code not in MODULE_CODES:
            await _record_unknown_module(db, agent, module.code, now)
            continue
        reported.add(module.code)
        _apply(db, agent, stored.get(module.code), module.code, module.state, module.reason_code, module.changed_at, now)

    # A module stored before and missing now is not "still as it was": it is not reported.
    for code, row in stored.items():
        if code not in reported:
            _apply(db, agent, row, code, "inactive", NOT_REPORTED, now, now)


def _apply(
    db: AsyncSession,
    agent: Agent,
    row: AgentModuleStatus | None,
    code: str,
    state: str,
    reason_code: str | None,
    changed_at: datetime,
    now: datetime,
) -> None:
    if row is None:
        db.add(AgentModuleStatus(
            id=str(uuid4()), tenant_id=agent.tenant_id, agent_id=agent.id, module_code=code,
            state=state, reason_code=reason_code, changed_at=changed_at, updated_at=now,
        ))
        _transition(db, agent, code, None, None, state, reason_code, now)
        return

    if row.state != state or row.reason_code != reason_code:
        _transition(db, agent, code, row.state, row.reason_code, state, reason_code, now)
        row.state, row.reason_code, row.changed_at = state, reason_code, changed_at
    row.updated_at = now


def _transition(
    db: AsyncSession,
    agent: Agent,
    code: str,
    old_state: str | None,
    old_reason: str | None,
    new_state: str,
    new_reason: str | None,
    now: datetime,
) -> None:
    db.add(AgentPostureChange(
        id=str(uuid4()), tenant_id=agent.tenant_id, agent_id=agent.id, module_code=code,
        old_state=old_state, old_reason_code=old_reason, new_state=new_state, new_reason_code=new_reason,
        created_at=now,
    ))


async def _record_unknown_module(db: AsyncSession, agent: Agent, code: str, now: datetime) -> None:
    """A module code the server does not know (an agent ahead of the server): never stored, and
    logged once per (tenant, code) and per hour -- not once per heartbeat -- with at most 20
    codes per tenant and hour (core/hourly_system_log.py)."""
    await record_hourly(
        db,
        tenant_id=agent.tenant_id,
        component=_HEARTBEAT_COMPONENT,
        family="unknown_module",
        key=f"unknown_module:{code}",
        message=lambda count, window_start: (
            f"heartbeat module code {code!r} is unknown to this server: shown as UNKNOWN, not stored "
            f"({count} heartbeat(s) since {window_start:%Y-%m-%d %H:%M} UTC)"
        ),
        first_context={"module_code": code},
        latest_context={"last_agent_id": agent.id},
        now=now,
        log_event="heartbeat_unknown_module",
    )


async def load_postures(
    db: AsyncSession, tenant_id: str, agent_ids: Sequence[str]
) -> dict[str, tuple[AgentPosture, list[AgentModuleStatus]]]:
    """Postures and module rows of these agents, read within the caller's tenant only."""
    if not agent_ids:
        return {}
    postures = (
        await db.execute(
            select(AgentPosture).where(AgentPosture.tenant_id == tenant_id, AgentPosture.agent_id.in_(agent_ids))
        )
    ).scalars().all()
    modules = (
        await db.execute(
            select(AgentModuleStatus).where(
                AgentModuleStatus.tenant_id == tenant_id, AgentModuleStatus.agent_id.in_(agent_ids)
            )
        )
    ).scalars().all()
    by_agent: dict[str, tuple[AgentPosture, list[AgentModuleStatus]]] = {p.agent_id: (p, []) for p in postures}
    for module in modules:
        if module.agent_id in by_agent:
            by_agent[module.agent_id][1].append(module)
    return by_agent


def snapshot(posture: AgentPosture, modules: Sequence[AgentModuleStatus]) -> PostureSnapshot:
    return PostureSnapshot(
        schema_version=posture.schema_version,
        received_at=posture.received_at,
        agent_uptime_seconds=posture.agent_uptime_seconds,
        modules={m.module_code: ModuleStatus(m.state, m.reason_code) for m in modules},
        pending_alerts=posture.pending_alerts,
        last_upload_ok_at=posture.last_upload_ok_at,
        last_upload_ok_age_seconds=posture.last_upload_ok_age_seconds,
        rejected_alerts=posture.rejected_alerts,
    )
