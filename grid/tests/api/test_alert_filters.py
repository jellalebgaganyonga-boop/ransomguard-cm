"""The Alerts page filters, end to end.

The owner reported on 2026-10-27 that choosing a status did not filter the list. The status
filter works; the search box does not. It says "Search (hostname...)" and sends what is typed as
``agent_id``, which is a UUID, so a hostname matches nothing and the list empties -- which from
the outside looks exactly like a filter that does nothing.
"""

from datetime import UTC, datetime
from uuid import uuid4

import pytest
from httpx import AsyncClient

from ransomguard_grid.core.security import hash_password
from ransomguard_grid.db.models.agent import Agent
from ransomguard_grid.db.models.alerts import Alert
from ransomguard_grid.db.models.enums import AgentStatus, AlertStatus, Severity, TenantStatus
from ransomguard_grid.db.models.tenant_user import Role, Tenant, User, UserRole
from tests.conftest import _test_session_factory

_PASSWORD = "FilterTest!2026"


async def _env() -> dict[str, str]:
    """One tenant, two workstations, one alert per status on the first of them."""
    suffix = uuid4().hex[:6]
    out: dict[str, str] = {}
    async with _test_session_factory() as session:
        from sqlalchemy import select

        tenant = Tenant(id=str(uuid4()), name="Filter Tenant", code=f"flt-{suffix}",
                        contact_email="f@t.local", status=TenantStatus.active)
        session.add(tenant)
        out["tenant_code"] = tenant.code

        role = (await session.execute(select(Role).where(Role.name == "tenant_admin"))).scalar_one_or_none()
        if not role:
            role = Role(id=str(uuid4()), name="tenant_admin", description="tenant_admin")
            session.add(role)
            await session.flush()

        user = User(id=str(uuid4()), tenant_id=tenant.id, email=f"admin-{suffix}@test.local",
                    hashed_password=hash_password(_PASSWORD), full_name="Admin", is_active=True,
                    created_at=datetime.now(UTC))
        session.add(user)
        await session.flush()
        session.add(UserRole(user_id=user.id, role_id=role.id, granted_at=datetime.now(UTC),
                             granted_by_user_id=user.id))
        out["email"] = user.email

        ward = Agent(id=str(uuid4()), tenant_id=tenant.id, hostname="PC-WARD-01", fqdn="ward.local",
                     os_version="10", agent_version="1.0", hardware_fingerprint=f"fp-w-{suffix}",
                     status=AgentStatus.active)
        lab = Agent(id=str(uuid4()), tenant_id=tenant.id, hostname="PC-LABORATORY", fqdn="lab.local",
                    os_version="10", agent_version="1.0", hardware_fingerprint=f"fp-l-{suffix}",
                    status=AgentStatus.active)
        session.add_all([ward, lab])
        await session.flush()
        out["ward_id"] = ward.id

        for i, status in enumerate(AlertStatus):
            session.add(Alert(id=str(uuid4()), tenant_id=tenant.id, agent_id=ward.id,
                              client_message_id=f"flt-{suffix}-{i}", alert_type="SentinelCanaryModified",
                              severity=Severity.High if i % 2 else Severity.Critical,
                              status=status, detected_at=datetime.now(UTC), summary=f"alert {status.value}"))
        session.add(Alert(id=str(uuid4()), tenant_id=tenant.id, agent_id=lab.id,
                          client_message_id=f"flt-{suffix}-lab", alert_type="UsbGuard",
                          severity=Severity.Low, status=AlertStatus.New,
                          detected_at=datetime.now(UTC), summary="alert on the laboratory"))
        await session.commit()
    return out


async def _token(client: AsyncClient, env: dict[str, str]) -> str:
    resp = await client.post("/api/v1/auth/login",
                             json={"tenant_code": env["tenant_code"], "email": env["email"], "password": _PASSWORD})
    return resp.json()["access_token"]


@pytest.mark.asyncio
@pytest.mark.parametrize("status", [s.value for s in AlertStatus])
async def test_each_status_returns_only_that_status(client: AsyncClient, status: str) -> None:
    env = await _env()
    token = await _token(client, env)

    resp = await client.get(f"/api/v1/dashboard/alerts?status={status}&limit=200",
                            headers={"Authorization": f"Bearer {token}"})

    assert resp.status_code == 200
    items = resp.json()["items"]
    assert items, f"no alert came back for {status}"
    assert {i["status"] for i in items} == {status}


@pytest.mark.asyncio
async def test_the_search_box_finds_a_workstation_by_its_name(client: AsyncClient) -> None:
    """What the box says it takes: a hostname, not a UUID nobody can type."""
    env = await _env()
    token = await _token(client, env)

    resp = await client.get("/api/v1/dashboard/alerts?hostname=LABORAT&limit=200",
                            headers={"Authorization": f"Bearer {token}"})

    assert resp.status_code == 200
    items = resp.json()["items"]
    assert items, "a partial hostname found nothing"
    assert {i["agent_hostname"] for i in items} == {"PC-LABORATORY"}


@pytest.mark.asyncio
async def test_status_and_severity_and_hostname_narrow_together(client: AsyncClient) -> None:
    env = await _env()
    token = await _token(client, env)

    resp = await client.get(
        "/api/v1/dashboard/alerts?status=New&severity=Low&hostname=PC-LABORATORY&limit=200",
        headers={"Authorization": f"Bearer {token}"},
    )

    assert resp.status_code == 200
    items = resp.json()["items"]
    assert len(items) == 1
    assert items[0]["status"] == "New"
    assert items[0]["severity"] == "Low"
    assert items[0]["agent_hostname"] == "PC-LABORATORY"


@pytest.mark.asyncio
async def test_a_hostname_from_another_hospital_finds_nothing(client: AsyncClient) -> None:
    """The search joins through the tenant, like every other query here."""
    mine = await _env()
    theirs = await _env()
    token = await _token(client, mine)

    resp = await client.get("/api/v1/dashboard/alerts?hostname=PC-LABORATORY&limit=200",
                            headers={"Authorization": f"Bearer {token}"})

    assert resp.status_code == 200
    # Both tenants have a PC-LABORATORY; only one of them is mine.
    ids = {i["agent_id"] for i in resp.json()["items"]}
    assert theirs["ward_id"] not in ids
    assert len(ids) == 1
