"""One definition of the fleet, for the dashboard and for the Agents page.

The owner opened the console on 2026-10-28 and read "Active endpoints 1/14" on the dashboard
while the Agents page listed two workstations. Both numbers were honest about their own query and
they did not describe the same fleet: the summary counted every agent row the tenant had ever
had, the page left out the decommissioned ones. The bench had rebuilt one workstation a dozen
times, so the gap was twelve dead rows -- and a figure like 1/14 in front of a hospital says
thirteen machines are unprotected.

A decommissioned agent is not part of the fleet. It is a machine that was reinstalled or
retired; its row stays for its alerts and its audit history, and it is still reachable by id and
still returned when it is asked for, but it is never counted as something that could be
protected. That is one rule, and both numbers come from it.
"""

from datetime import UTC, datetime
from uuid import uuid4

import pytest
from httpx import AsyncClient
from sqlalchemy import select

from ransomguard_grid.core.security import hash_password
from ransomguard_grid.db.models.agent import Agent
from ransomguard_grid.db.models.enums import AgentStatus, TenantStatus
from ransomguard_grid.db.models.tenant_user import Role, Tenant, User, UserRole
from tests.conftest import _test_session_factory

_PASSWORD = "TestPass123!"


async def _tenant_with_agents(*statuses: AgentStatus) -> tuple[str, str]:
    """A tenant whose agents have exactly these statuses. Returns (tenant code, admin email)."""
    suffix = uuid4().hex[:6]
    async with _test_session_factory() as session:
        tenant = Tenant(
            id=str(uuid4()), name="Hopital de Yaounde", code=f"yde-{suffix}",
            contact_email=f"admin-{suffix}@test.local", status=TenantStatus.active,
        )
        session.add(tenant)

        role = (await session.execute(
            select(Role).where(Role.name == "tenant_admin"))).scalar_one_or_none()
        if role is None:
            role = Role(id=str(uuid4()), name="tenant_admin", description="Admin")
            session.add(role)
            await session.flush()

        user = User(
            id=str(uuid4()), tenant_id=tenant.id, email=f"admin-{suffix}@test.local",
            hashed_password=hash_password(_PASSWORD), full_name="Admin", is_active=True,
            created_at=datetime.now(UTC),
        )
        session.add(user)
        await session.flush()
        session.add(UserRole(
            user_id=user.id, role_id=role.id,
            granted_at=datetime.now(UTC), granted_by_user_id=user.id,
        ))

        for index, status in enumerate(statuses):
            session.add(Agent(
                id=str(uuid4()), tenant_id=tenant.id, hostname=f"pc-{index}",
                fqdn=f"pc-{index}.local", os_version="10", agent_version="1.0",
                hardware_fingerprint=f"fp-{suffix}-{index}", status=status,
            ))

        await session.commit()
        return tenant.code, user.email


async def _login(client: AsyncClient, tenant_code: str, email: str) -> str:
    resp = await client.post("/api/v1/auth/login", json={
        "tenant_code": tenant_code, "email": email, "password": _PASSWORD,
    })
    assert resp.status_code == 200, f"login failed: {resp.json()}"
    return resp.json()["access_token"]


# The bench fleet as the owner saw it: two workstations, and a pile of rows from rebuilds.
_FLEET = (
    AgentStatus.active,
    AgentStatus.disconnected,
    AgentStatus.decommissioned,
    AgentStatus.decommissioned,
    AgentStatus.decommissioned,
)


@pytest.mark.asyncio
async def test_the_dashboard_and_the_agents_page_count_the_same_fleet(client: AsyncClient) -> None:
    """The two numbers the owner compared must be the same number."""
    code, email = await _tenant_with_agents(*_FLEET)
    token = await _login(client, code, email)
    auth = {"Authorization": f"Bearer {token}"}

    summary = (await client.get("/api/v1/dashboard/metrics/summary", headers=auth)).json()
    listing = (await client.get("/api/v1/dashboard/agents", headers=auth)).json()

    assert summary["total_agents"] == listing["total"], (
        "the dashboard's denominator and the Agents page must describe one fleet"
    )
    assert summary["total_agents"] == 2, "three decommissioned rows are not workstations"
    assert summary["active_agents"] == 1
    assert len(listing["items"]) == 2


@pytest.mark.asyncio
async def test_a_decommissioned_agent_is_still_there_when_it_is_asked_for(
    client: AsyncClient,
) -> None:
    """Not counted is not deleted: the rows keep their alerts and their audit history."""
    code, email = await _tenant_with_agents(*_FLEET)
    token = await _login(client, code, email)
    auth = {"Authorization": f"Bearer {token}"}

    everything = (await client.get(
        "/api/v1/dashboard/agents?include_decommissioned=true", headers=auth)).json()
    only_dead = (await client.get(
        "/api/v1/dashboard/agents?status=decommissioned", headers=auth)).json()

    assert everything["total"] == 5
    assert only_dead["total"] == 3


@pytest.mark.asyncio
async def test_an_enrolled_workstation_is_active_not_provisioned(client: AsyncClient) -> None:
    """Why the Agents page no longer offers a Provisioned filter.

    `provisioned` is the column default and nothing ever writes it: enrolment creates the agent
    `active` in the same statement, and the only other writers are the heartbeat (`active`), the
    disconnect worker (`disconnected`) and the console's decommission button. So the filter was a
    choice that could only ever return an empty page, and it is gone from the console rather than
    left there to be tried once and distrusted.
    """
    code, email = await _tenant_with_agents(*_FLEET)
    token = await _login(client, code, email)
    auth = {"Authorization": f"Bearer {token}"}

    provisioned = (await client.get(
        "/api/v1/dashboard/agents?status=provisioned", headers=auth)).json()

    assert provisioned["total"] == 0

    async with _test_session_factory() as session:
        statuses = set((await session.execute(select(Agent.status))).scalars().all())
    assert AgentStatus.provisioned not in statuses
