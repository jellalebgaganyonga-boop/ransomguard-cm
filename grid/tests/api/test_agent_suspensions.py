"""GET /api/v1/dashboard/agents/{agent_id}/suspensions (ADR-026, B1.6).

What the console lists under "Suspended Processes" on the agent page: the processes this agent
says are frozen right now. Built from what the agent reported, never from what the console asked.
A suspension leaves the list when the agent reports it closed -- resumed on request, or gone. A
resume that failed keeps it in the list, flagged: the process is still frozen and there is no way
back until the machine restarts.
"""

from datetime import UTC, datetime, timedelta
from uuid import uuid4

import pytest
from httpx import AsyncClient
from sqlalchemy.ext.asyncio import AsyncSession

from ransomguard_grid.db.models.alerts import Alert, AlertDetail
from ransomguard_grid.db.models.enums import Severity
from tests.api.test_alert_attribution import Ids, _alert, _tenant_and_agent

SUSPENSION = "11111111-2222-3333-4444-555555555555"


async def _response_alert(
    db: AsyncSession,
    ids: Ids,
    *,
    alert_type: str,
    response: str,
    response_to: str,
    suspension_id: str = SUSPENSION,
    when: datetime | None = None,
    extra: dict[str, str] | None = None,
) -> str:
    """One response alert of the agent, as AlertMapper composes it."""
    alert_id = str(uuid4())
    db.add(Alert(
        id=alert_id, tenant_id=ids.tenant_id, agent_id=ids.agent_id,
        client_message_id=f"rg-{uuid4().hex}", alert_type=alert_type, severity=Severity.Critical,
        detected_at=when or datetime.now(UTC), ingested_at=datetime.now(UTC),
        summary=response, raw_payload_json={},
    ))
    await db.flush()
    details = {"response": response, "suspension_id": suspension_id, "response_to": response_to}
    details.update(extra or {})
    for key, value in details.items():
        db.add(AlertDetail(id=str(uuid4()), tenant_id=ids.tenant_id, alert_id=alert_id, key=key, value=value))
    await db.commit()
    return alert_id


@pytest.mark.asyncio
async def test_a_suspension_the_agent_reported_open_is_listed(
    client: AsyncClient, db_session: AsyncSession,
) -> None:
    ids = await _tenant_and_agent(db_session)
    message_id = f"rg-{uuid4().hex}"
    await _alert(db_session, ids, message_id, state="resolved")
    await _response_alert(
        db_session, ids, alert_type="SentinelProcessSuspended", response="process_suspended",
        response_to=message_id,
        extra={"process_id": "4242", "process_name": "evil", "canary_path": r"C:\Users\a\0001_x.txt"},
    )

    headers = await _admin_of(client, db_session, ids)
    listed = await client.get(f"/api/v1/dashboard/agents/{ids.agent_id}/suspensions", headers=headers)

    assert listed.status_code == 200, listed.text
    [item] = listed.json()
    assert item["suspension_id"] == SUSPENSION
    assert item["process_id"] == 4242
    assert item["process_name"] == "evil"
    assert item["canary_path"].endswith("0001_x.txt")
    assert item["resume_failed"] is False
    assert item["escalations"] == 0


@pytest.mark.asyncio
@pytest.mark.parametrize(
    ("alert_type", "response"),
    [("SentinelProcessResumed", "process_resumed"), ("SentinelSuspensionEnded", "suspension_ended")],
)
async def test_a_suspension_the_agent_reported_closed_is_not_listed(
    client: AsyncClient, db_session: AsyncSession, alert_type: str, response: str,
) -> None:
    ids = await _tenant_and_agent(db_session)
    message_id = f"rg-{uuid4().hex}"
    await _alert(db_session, ids, message_id, state="resolved")
    opened = datetime.now(UTC) - timedelta(minutes=10)
    await _response_alert(
        db_session, ids, alert_type="SentinelProcessSuspended", response="process_suspended",
        response_to=message_id, when=opened, extra={"process_id": "4242"},
    )
    await _response_alert(
        db_session, ids, alert_type=alert_type, response=response, response_to=message_id,
    )

    headers = await _admin_of(client, db_session, ids)
    listed = await client.get(f"/api/v1/dashboard/agents/{ids.agent_id}/suspensions", headers=headers)

    assert listed.status_code == 200, listed.text
    assert listed.json() == []  # the console stops claiming a process is frozen


@pytest.mark.asyncio
async def test_a_resume_that_failed_keeps_the_process_listed_and_says_so(
    client: AsyncClient, db_session: AsyncSession,
) -> None:
    ids = await _tenant_and_agent(db_session)
    message_id = f"rg-{uuid4().hex}"
    await _alert(db_session, ids, message_id, state="resolved")
    await _response_alert(
        db_session, ids, alert_type="SentinelProcessSuspended", response="process_suspended",
        response_to=message_id, when=datetime.now(UTC) - timedelta(minutes=5), extra={"process_id": "4242"},
    )
    await _response_alert(
        db_session, ids, alert_type="SentinelProcessResumeFailed", response="resume_failed",
        response_to=message_id,
    )

    headers = await _admin_of(client, db_session, ids)
    [item] = (await client.get(f"/api/v1/dashboard/agents/{ids.agent_id}/suspensions", headers=headers)).json()

    assert item["resume_failed"] is True  # still frozen, and nothing can unfreeze it


@pytest.mark.asyncio
async def test_escalations_are_counted_for_the_suspension_they_belong_to(
    client: AsyncClient, db_session: AsyncSession,
) -> None:
    ids = await _tenant_and_agent(db_session)
    message_id = f"rg-{uuid4().hex}"
    await _alert(db_session, ids, message_id, state="resolved")
    await _response_alert(
        db_session, ids, alert_type="SentinelProcessSuspended", response="process_suspended",
        response_to=message_id, when=datetime.now(UTC) - timedelta(hours=3), extra={"process_id": "4242"},
    )
    for _ in range(2):
        await _response_alert(
            db_session, ids, alert_type="SentinelProcessSuspendedNoDecision",
            response="suspended_no_decision", response_to=message_id,
        )

    headers = await _admin_of(client, db_session, ids)
    [item] = (await client.get(f"/api/v1/dashboard/agents/{ids.agent_id}/suspensions", headers=headers)).json()

    assert item["escalations"] == 2


@pytest.mark.asyncio
async def test_an_agent_of_another_tenant_is_not_found(
    client: AsyncClient, db_session: AsyncSession,
) -> None:
    mine = await _tenant_and_agent(db_session)
    theirs = await _tenant_and_agent(db_session)
    headers = await _admin_of(client, db_session, mine)

    refused = await client.get(f"/api/v1/dashboard/agents/{theirs.agent_id}/suspensions", headers=headers)

    assert refused.status_code == 404


@pytest.mark.asyncio
async def test_every_role_may_read_the_list(client: AsyncClient, db_session: AsyncSession) -> None:
    ids = await _tenant_and_agent(db_session)
    for role in ("security_analyst", "read_only_auditor"):
        headers = await _admin_of(client, db_session, ids, role=role)
        answered = await client.get(f"/api/v1/dashboard/agents/{ids.agent_id}/suspensions", headers=headers)
        assert answered.status_code == 200, f"{role}: {answered.text}"


async def _admin_of(
    client: AsyncClient, db: AsyncSession, ids: Ids, role: str = "tenant_admin",
) -> dict[str, str]:
    """A logged-in user of the tenant that owns this agent, with the given role."""
    from sqlalchemy import select

    from ransomguard_grid.core.security import hash_password
    from ransomguard_grid.db.models.tenant_user import Role, Tenant, User, UserRole

    tenant = (await db.execute(select(Tenant).where(Tenant.id == ids.tenant_id))).scalar_one()
    email = f"u-{uuid4().hex[:8]}@t.local"
    password = "MotDePasseTest123!"
    user_id = str(uuid4())
    db.add(User(id=user_id, tenant_id=ids.tenant_id, email=email, full_name="u",
                hashed_password=hash_password(password), is_active=True, created_at=datetime.now(UTC)))
    role_row = (await db.execute(select(Role).where(Role.name == role))).scalars().first()
    if role_row is None:
        role_row = Role(id=str(uuid4()), name=role, description=role)
        db.add(role_row)
        await db.flush()
    db.add(UserRole(user_id=user_id, role_id=role_row.id, granted_by_user_id=user_id, granted_at=datetime.now(UTC)))
    await db.commit()

    logged = await client.post("/api/v1/auth/login", json={
        "tenant_code": tenant.code, "email": email, "password": password,
    })
    assert logged.status_code == 200, logged.text
    return {"Authorization": f"Bearer {logged.json()['access_token']}"}
