"""The genealogy complement of an alert: the chain of processes behind a write.

POST /api/v1/agents/{agent_id}/alerts/{client_message_id}/genealogy -- partitioned like the
attribution complement it sits beside: the authenticated agent only, its own alert only
(client_message_id AND agent_id AND tenant_id), and a bounded body.

The chain arrives after the alert because it is built while the writer is still running, which is
necessarily later: an alert that waited for its tree would be delayed for every case, including
the one where no tree can be built at all.
"""

from dataclasses import dataclass
from datetime import UTC, datetime, timedelta
from typing import Any
from uuid import uuid4

import pytest
from httpx import AsyncClient, Response
from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from ransomguard_grid.db.models.agent import Agent, AgentCertificate
from ransomguard_grid.db.models.alerts import Alert, AlertDetail
from ransomguard_grid.db.models.enums import AgentStatus, Severity, TenantStatus
from ransomguard_grid.db.models.tenant_user import Tenant

# powershell, started by cmd, started by explorer: what the bench trigger actually produces.
CHAIN: dict[str, Any] = {
    "writer": {
        "pid": 6860,
        "name": "powershell",
        "path": "C:\\Windows\\System32\\WindowsPowerShell\\v1.0\\powershell.exe",
        "started_at": "2026-10-25T17:23:40+00:00",
        "command_line": "powershell -NoProfile -File D:\\trigger-sentinel.ps1",
    },
    "ancestors": [
        {"pid": 4120, "name": "cmd", "path": "C:\\Windows\\System32\\cmd.exe"},
        {"pid": 3100, "name": "explorer", "path": "C:\\Windows\\explorer.exe"},
    ],
    "patterns": [
        {
            "technique_id": "T1486",
            "technique_name": "Data Encrypted for Impact",
            "description": "A script interpreter wrote to a decoy file.",
        }
    ],
}


@dataclass(frozen=True)
class Ids:
    tenant_id: str
    agent_id: str


async def _tenant_and_agent(db: AsyncSession, serial: str | None = None) -> Ids:
    tenant = Tenant(
        id=str(uuid4()), name="Yaounde Regional Hospital", code=f"t{uuid4().hex[:8]}",
        contact_email="admin@yde-hospital.cm", status=TenantStatus.active,
    )
    db.add(tenant)
    agent_id = str(uuid4())
    db.add(Agent(
        id=agent_id, tenant_id=tenant.id, hostname="PC-LAB", fqdn="pc-lab.local",
        os_version="10", agent_version="1.0.0", hardware_fingerprint=uuid4().hex,
        status=AgentStatus.active, enrolled_at=datetime.now(UTC),
    ))
    if serial:
        now = datetime.now(UTC)
        db.add(AgentCertificate(
            id=str(uuid4()), agent_id=agent_id, serial_number=serial, fingerprint_sha256="f" * 64,
            not_before=now - timedelta(days=1), not_after=now + timedelta(days=365),
        ))
    await db.commit()
    return Ids(tenant.id, agent_id)


async def _alert(db: AsyncSession, ids: Ids, client_message_id: str) -> str:
    alert_id = str(uuid4())
    db.add(Alert(
        id=alert_id, tenant_id=ids.tenant_id, agent_id=ids.agent_id,
        client_message_id=client_message_id, alert_type="SentinelCanaryModified",
        severity=Severity.Critical, detected_at=datetime.now(UTC),
        ingested_at=datetime.now(UTC), summary="Canary modified", raw_payload_json={},
    ))
    await db.commit()
    return alert_id


async def _details(db: AsyncSession, alert_id: str) -> dict[str, str]:
    db.expire_all()
    rows = (await db.execute(select(AlertDetail).where(AlertDetail.alert_id == alert_id))).scalars().all()
    return {row.key: row.value for row in rows}


async def _post(client: AsyncClient, agent_id: str, cmid: str, body: dict[str, Any], cert: str) -> Response:
    return await client.post(
        f"/api/v1/agents/{agent_id}/alerts/{cmid}/genealogy",
        json=body, headers={"X-Client-Cert": cert},
    )


@pytest.mark.asyncio
async def test_the_chain_is_written_on_the_alert_oldest_first(
    client: AsyncClient, db_session: AsyncSession, test_cert_serial: str, test_cert_pem: str,
) -> None:
    """The console shows "who started what", so the chain reads from the oldest ancestor."""
    ids = await _tenant_and_agent(db_session, test_cert_serial)
    alert_id = await _alert(db_session, ids, "cmid-chain")

    response = await _post(client, ids.agent_id, "cmid-chain", CHAIN, test_cert_pem)

    assert response.status_code == 200
    assert response.json() == {"alert_id": alert_id, "status": "applied"}

    details = await _details(db_session, alert_id)
    assert details["genealogy_chain"] == "explorer -> cmd -> powershell"

    import json
    tree = json.loads(details["genealogy_tree"])
    assert tree["writer"]["pid"] == 6860
    assert tree["writer"]["name"] == "powershell"
    # Ancestors stay nearest-first on the wire; the display order is the console's business.
    assert [a["name"] for a in tree["ancestors"]] == ["cmd", "explorer"]

    patterns = json.loads(details["genealogy_patterns"])
    assert patterns[0]["technique_id"] == "T1486"


@pytest.mark.asyncio
async def test_a_writer_with_no_ancestors_is_still_a_chain(
    client: AsyncClient, db_session: AsyncSession, test_cert_serial: str, test_cert_pem: str,
) -> None:
    """A process whose parent has already exited is the whole chain, not an error."""
    ids = await _tenant_and_agent(db_session, test_cert_serial)
    alert_id = await _alert(db_session, ids, "cmid-alone")

    body = {"writer": CHAIN["writer"], "ancestors": [], "patterns": []}
    response = await _post(client, ids.agent_id, "cmid-alone", body, test_cert_pem)

    assert response.status_code == 200
    details = await _details(db_session, alert_id)
    assert details["genealogy_chain"] == "powershell"
    assert details["genealogy_patterns"] == "[]"


@pytest.mark.asyncio
async def test_a_second_complement_replaces_the_first(
    client: AsyncClient, db_session: AsyncSession, test_cert_serial: str, test_cert_pem: str,
) -> None:
    """Unlike the attribution, a later chain is the better reading and replaces the earlier one."""
    ids = await _tenant_and_agent(db_session, test_cert_serial)
    alert_id = await _alert(db_session, ids, "cmid-twice")

    await _post(client, ids.agent_id, "cmid-twice", {"writer": CHAIN["writer"], "ancestors": []}, test_cert_pem)
    await _post(client, ids.agent_id, "cmid-twice", CHAIN, test_cert_pem)

    details = await _details(db_session, alert_id)
    assert details["genealogy_chain"] == "explorer -> cmd -> powershell"
    # One row per key, not two: the old details are removed, never left to pile up.
    rows = (await db_session.execute(select(AlertDetail).where(
        AlertDetail.alert_id == alert_id, AlertDetail.key == "genealogy_chain"))).scalars().all()
    assert len(rows) == 1


@pytest.mark.asyncio
async def test_an_agent_cannot_send_a_chain_for_another_agent(
    client: AsyncClient, db_session: AsyncSession, test_cert_serial: str, test_cert_pem: str,
) -> None:
    ids = await _tenant_and_agent(db_session, test_cert_serial)
    await _alert(db_session, ids, "cmid-other")

    response = await _post(client, str(uuid4()), "cmid-other", CHAIN, test_cert_pem)

    assert response.status_code == 403


@pytest.mark.asyncio
async def test_the_alert_of_another_tenant_is_404_and_untouched(
    client: AsyncClient, db_session: AsyncSession, test_cert_serial: str, test_cert_pem: str,
) -> None:
    """Same client_message_id in another tenant: not found, and nothing written to it."""
    mine = await _tenant_and_agent(db_session, test_cert_serial)
    theirs = await _tenant_and_agent(db_session)
    their_alert = await _alert(db_session, theirs, "shared-cmid")

    response = await _post(client, mine.agent_id, "shared-cmid", CHAIN, test_cert_pem)

    assert response.status_code == 404
    assert await _details(db_session, their_alert) == {}


@pytest.mark.asyncio
async def test_a_chain_longer_than_the_bound_is_refused(
    client: AsyncClient, db_session: AsyncSession, test_cert_serial: str, test_cert_pem: str,
) -> None:
    """The body is bounded: an agent cannot push an unbounded list into the alert's details."""
    ids = await _tenant_and_agent(db_session, test_cert_serial)
    await _alert(db_session, ids, "cmid-long")

    body = {
        "writer": CHAIN["writer"],
        "ancestors": [{"pid": 100 + i, "name": f"p{i}"} for i in range(11)],
    }
    response = await _post(client, ids.agent_id, "cmid-long", body, test_cert_pem)

    assert response.status_code == 422


@pytest.mark.asyncio
async def test_the_console_reads_the_chain_back_on_the_alert_detail(
    client: AsyncClient, db_session: AsyncSession, test_cert_serial: str, test_cert_pem: str,
) -> None:
    """The point of sending it: the chain has to come back out on the page a human looks at."""
    from ransomguard_grid.core.security import hash_password
    from ransomguard_grid.db.models.tenant_user import Role, User, UserRole

    ids = await _tenant_and_agent(db_session, test_cert_serial)
    alert_id = await _alert(db_session, ids, "cmid-console")
    await _post(client, ids.agent_id, "cmid-console", CHAIN, test_cert_pem)

    tenant = await db_session.get(Tenant, ids.tenant_id)
    assert tenant is not None
    user = User(
        id=str(uuid4()), tenant_id=ids.tenant_id, email=f"{uuid4().hex[:8]}@yde-hospital.cm",
        hashed_password=hash_password("Passw0rd!2026"), full_name="Admin", is_active=True,
        created_at=datetime.now(UTC),
    )
    db_session.add(user)
    role = (await db_session.execute(select(Role).where(Role.name == "tenant_admin"))).scalar_one_or_none()
    if role is None:
        role = Role(id=str(uuid4()), name="tenant_admin", description="admin")
        db_session.add(role)
        await db_session.flush()
    await db_session.flush()
    db_session.add(UserRole(user_id=user.id, role_id=role.id, granted_at=datetime.now(UTC),
                            granted_by_user_id=user.id))
    await db_session.commit()

    login = await client.post("/api/v1/auth/login", json={
        "email": user.email, "password": "Passw0rd!2026", "tenant_code": tenant.code})
    assert login.status_code == 200
    headers = {"Authorization": f"Bearer {login.json()['access_token']}"}

    detail = (await client.get(f"/api/v1/dashboard/alerts/{alert_id}", headers=headers)).json()

    genealogy = detail["genealogy"]
    assert genealogy is not None, "the console must be able to show what the agent sent"
    assert genealogy["chain"] == "explorer -> cmd -> powershell"
    assert genealogy["writer"]["name"] == "powershell"
    assert genealogy["writer"]["pid"] == 6860
    assert [a["name"] for a in genealogy["ancestors"]] == ["cmd", "explorer"]
    assert genealogy["patterns"][0]["technique_id"] == "T1486"


@pytest.mark.asyncio
async def test_an_alert_with_no_chain_says_so_rather_than_showing_an_empty_tree(
    client: AsyncClient, db_session: AsyncSession, test_cert_serial: str, test_cert_pem: str,
) -> None:
    from ransomguard_grid.core.security import hash_password
    from ransomguard_grid.db.models.tenant_user import Role, User, UserRole

    ids = await _tenant_and_agent(db_session, test_cert_serial)
    alert_id = await _alert(db_session, ids, "cmid-nochain")

    tenant = await db_session.get(Tenant, ids.tenant_id)
    assert tenant is not None
    user = User(
        id=str(uuid4()), tenant_id=ids.tenant_id, email=f"{uuid4().hex[:8]}@yde-hospital.cm",
        hashed_password=hash_password("Passw0rd!2026"), full_name="Admin", is_active=True,
        created_at=datetime.now(UTC),
    )
    db_session.add(user)
    role = (await db_session.execute(select(Role).where(Role.name == "tenant_admin"))).scalar_one_or_none()
    if role is None:
        role = Role(id=str(uuid4()), name="tenant_admin", description="admin")
        db_session.add(role)
        await db_session.flush()
    await db_session.flush()
    db_session.add(UserRole(user_id=user.id, role_id=role.id, granted_at=datetime.now(UTC),
                            granted_by_user_id=user.id))
    await db_session.commit()

    login = await client.post("/api/v1/auth/login", json={
        "email": user.email, "password": "Passw0rd!2026", "tenant_code": tenant.code})
    headers = {"Authorization": f"Bearer {login.json()['access_token']}"}

    detail = (await client.get(f"/api/v1/dashboard/alerts/{alert_id}", headers=headers)).json()

    assert detail["genealogy"] is None
