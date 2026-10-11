"""The 90-day purge of module transitions: who runs it, when, and what it deletes."""

from datetime import UTC, datetime, timedelta
from uuid import uuid4

import pytest
from sqlalchemy import select

from ransomguard_grid.db.models.agent import Agent, AgentPostureChange
from ransomguard_grid.db.models.enums import AgentStatus, TenantStatus
from ransomguard_grid.db.models.tenant_user import Tenant
from ransomguard_grid.workers.agent_disconnect_worker import AgentDisconnectWorker
from tests.conftest import _test_session_factory

NOW = datetime(2026, 10, 11, 12, 0, tzinfo=UTC)


async def _changes_aged(*ages: timedelta) -> list[str]:
    ids: list[str] = []
    async with _test_session_factory() as session:
        tenant = Tenant(id=str(uuid4()), name="T", code=f"purge-{uuid4().hex[:6]}", contact_email="t@t.local", status=TenantStatus.active)
        session.add(tenant)
        agent = Agent(id=str(uuid4()), tenant_id=tenant.id, hostname="h", fqdn="h.local", os_version="10",
                      agent_version="1.0", hardware_fingerprint=f"fp-{uuid4().hex[:6]}", status=AgentStatus.active)
        session.add(agent)
        await session.flush()
        for age in ages:
            change_id = str(uuid4())
            ids.append(change_id)
            session.add(AgentPostureChange(
                id=change_id, tenant_id=tenant.id, agent_id=agent.id, module_code="SENTINEL",
                old_state="active", new_state="inactive", new_reason_code="stopped_unexpectedly", created_at=NOW - age,
            ))
        await session.commit()
    return ids


async def _remaining(ids: list[str]) -> set[str]:
    async with _test_session_factory() as session:
        rows = await session.execute(select(AgentPostureChange.id).where(AgentPostureChange.id.in_(ids)))
        return {row[0] for row in rows}


@pytest.mark.asyncio
async def test_transitions_older_than_90_days_are_purged_the_rest_kept() -> None:
    old, edge, recent = await _changes_aged(timedelta(days=91), timedelta(days=89, hours=23), timedelta(days=1))
    worker = AgentDisconnectWorker(_test_session_factory)

    assert await worker.purge_posture_history(NOW) >= 1

    assert await _remaining([old, edge, recent]) == {edge, recent}


@pytest.mark.asyncio
async def test_the_purge_runs_at_most_once_per_24_hours() -> None:
    worker = AgentDisconnectWorker(_test_session_factory)
    await worker.purge_posture_history(NOW)
    [old] = await _changes_aged(timedelta(days=200))

    assert await worker.purge_posture_history(NOW + timedelta(hours=23)) == 0
    assert await _remaining([old]) == {old}

    assert await worker.purge_posture_history(NOW + timedelta(hours=24)) == 1
    assert await _remaining([old]) == set()
