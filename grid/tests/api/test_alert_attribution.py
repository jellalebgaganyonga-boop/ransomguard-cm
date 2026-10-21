"""The attribution complement of an alert (ADR-025, section 3, commit 5).

POST /api/v1/agents/{agent_id}/alerts/{client_message_id}/attribution -- partitioned like the
alerts: the authenticated agent only, its own alert only (client_message_id AND agent_id AND
tenant_id), a bounded body, and an attribution that never changes once final.
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
from ransomguard_grid.db.models.operations import SystemLog
from ransomguard_grid.db.models.tenant_user import Tenant

RESOLVED: dict[str, Any] = {
    "state": "resolved",
    "pid": 4242,
    "process_created_at": "2026-10-21T09:57:00+00:00",
    "process_name": "evil",
    "image_path": "C:\\Users\\x\\AppData\\Local\\Temp\\evil.exe",
}


@dataclass(frozen=True)
class Ids:
    tenant_id: str
    agent_id: str


async def _tenant_and_agent(db: AsyncSession, serial: str | None = None) -> Ids:
    """A tenant and one agent (with a certificate when a serial is given); plain ids only, so that
    nothing expired by a later commit is ever read."""
    tenant_id, agent_id, code = str(uuid4()), str(uuid4()), f"attr-{uuid4().hex[:8]}"
    db.add(Tenant(id=tenant_id, name="T", code=code, contact_email="t@t.local", status=TenantStatus.active))
    await db.flush()
    await _agent(db, tenant_id, agent_id)
    if serial is not None:
        now = datetime.now(UTC)
        db.add(AgentCertificate(
            id=str(uuid4()), agent_id=agent_id, serial_number=serial, fingerprint_sha256="f" * 64,
            not_before=now - timedelta(days=1), not_after=now + timedelta(days=365),
        ))
    await db.commit()
    return Ids(tenant_id, agent_id)


async def _agent(db: AsyncSession, tenant_id: str, agent_id: str) -> None:
    db.add(Agent(
        id=agent_id, tenant_id=tenant_id, hostname="h", fqdn="h.local", os_version="10",
        agent_version="1.0", hardware_fingerprint=f"fp-{uuid4().hex[:10]}", status=AgentStatus.active,
    ))
    await db.flush()


async def _alert(db: AsyncSession, ids: Ids, client_message_id: str, state: str | None = "pending") -> str:
    alert_id = str(uuid4())
    db.add(Alert(
        id=alert_id, tenant_id=ids.tenant_id, agent_id=ids.agent_id, client_message_id=client_message_id,
        alert_type="SentinelCanaryModified", severity=Severity.Critical, detected_at=datetime.now(UTC),
        ingested_at=datetime.now(UTC), summary="Canary modified", raw_payload_json={},
    ))
    await db.flush()
    if state is not None:
        db.add(AlertDetail(id=str(uuid4()), tenant_id=ids.tenant_id, alert_id=alert_id,
                           key="attribution_state", value=state))
    await db.commit()
    return alert_id


async def _details(db: AsyncSession, alert_id: str) -> dict[str, str]:
    db.expire_all()
    rows = (await db.execute(select(AlertDetail).where(AlertDetail.alert_id == alert_id))).scalars().all()
    return {d.key: d.value for d in rows}


async def _events(db: AsyncSession) -> list[str]:
    db.expire_all()
    logs = (await db.execute(select(SystemLog).where(SystemLog.component == "alert-attribution"))).scalars().all()
    return sorted(str(log.context_json["event"]) for log in logs if log.context_json)


async def _post(client: AsyncClient, agent_id: str, client_message_id: str, body: dict[str, Any], cert: str) -> Response:
    return await client.post(
        f"/api/v1/agents/{agent_id}/alerts/{client_message_id}/attribution",
        json=body, headers={"X-Client-Cert": cert},
    )


@pytest.mark.asyncio
async def test_a_resolved_complement_names_the_writer_on_the_alert_and_is_logged(
    client: AsyncClient, db_session: AsyncSession, test_cert_serial: str, test_cert_pem: str,
) -> None:
    ids = await _tenant_and_agent(db_session, test_cert_serial)
    alert_id = await _alert(db_session, ids, "cmid-resolved-0001")

    response = await _post(client, ids.agent_id, "cmid-resolved-0001", RESOLVED, test_cert_pem)

    assert response.status_code == 200
    assert response.json() == {"alert_id": alert_id, "status": "applied"}
    details = await _details(db_session, alert_id)
    assert details["attribution_state"] == "resolved"
    assert details["process_id"] == "4242"
    assert details["process_name"] == "evil"
    assert details["process_path"].endswith("evil.exe")
    assert details["process_created_at"].startswith("2026-10-21T09:57:00")
    assert "attribution_reason" not in details
    assert await _events(db_session) == ["AlertAttributionApplied"]


@pytest.mark.asyncio
async def test_an_unavailable_complement_names_nobody_and_keeps_its_reason(
    client: AsyncClient, db_session: AsyncSession, test_cert_serial: str, test_cert_pem: str,
) -> None:
    ids = await _tenant_and_agent(db_session, test_cert_serial)
    alert_id = await _alert(db_session, ids, "cmid-unavail-0001")

    response = await _post(client, ids.agent_id, "cmid-unavail-0001",
                           {"state": "unavailable", "reason": "system_pid"}, test_cert_pem)

    assert response.status_code == 200
    assert await _details(db_session, alert_id) == {"attribution_state": "unavailable", "attribution_reason": "system_pid"}


@pytest.mark.asyncio
async def test_a_second_complement_on_a_final_attribution_changes_nothing_and_is_logged(
    client: AsyncClient, db_session: AsyncSession, test_cert_serial: str, test_cert_pem: str,
) -> None:
    ids = await _tenant_and_agent(db_session, test_cert_serial)
    alert_id = await _alert(db_session, ids, "cmid-twice-00001")
    assert (await _post(client, ids.agent_id, "cmid-twice-00001", RESOLVED, test_cert_pem)).status_code == 200

    again = await _post(client, ids.agent_id, "cmid-twice-00001", {"state": "ambiguous"}, test_cert_pem)

    assert again.status_code == 200
    assert again.json()["status"] == "unchanged"
    assert (await _details(db_session, alert_id))["attribution_state"] == "resolved"
    assert await _events(db_session) == ["AlertAttributionApplied", "AlertAttributionIgnored"]


@pytest.mark.asyncio
async def test_an_agent_cannot_attribute_in_the_name_of_another_agent_403(
    client: AsyncClient, db_session: AsyncSession, test_cert_serial: str, test_cert_pem: str,
) -> None:
    ids = await _tenant_and_agent(db_session, test_cert_serial)
    alert_id = await _alert(db_session, ids, "cmid-mismatch-01")

    response = await _post(client, str(uuid4()), "cmid-mismatch-01", RESOLVED, test_cert_pem)

    assert response.status_code == 403
    assert await _details(db_session, alert_id) == {"attribution_state": "pending"}


@pytest.mark.asyncio
async def test_the_alert_of_another_tenant_with_the_same_client_message_id_is_404_and_untouched(
    client: AsyncClient, db_session: AsyncSession, test_cert_serial: str, test_cert_pem: str,
) -> None:
    """Isolation, as for HOST: the only alert carrying this client_message_id belongs to another
    tenant. A lookup on client_message_id alone would find it and answer 200: this test would fail."""
    ids = await _tenant_and_agent(db_session, test_cert_serial)
    foreign = await _tenant_and_agent(db_session)
    foreign_alert = await _alert(db_session, foreign, "cmid-shared-00001")

    response = await _post(client, ids.agent_id, "cmid-shared-00001", RESOLVED, test_cert_pem)

    assert response.status_code == 404
    assert response.json()["detail"] == "Alert not found"
    assert await _details(db_session, foreign_alert) == {"attribution_state": "pending"}
    assert await _events(db_session) == []


@pytest.mark.asyncio
async def test_the_alert_of_another_agent_of_the_same_tenant_is_404_and_untouched(
    client: AsyncClient, db_session: AsyncSession, test_cert_serial: str, test_cert_pem: str,
) -> None:
    ids = await _tenant_and_agent(db_session, test_cert_serial)
    neighbour = Ids(ids.tenant_id, str(uuid4()))
    await _agent(db_session, neighbour.tenant_id, neighbour.agent_id)
    await db_session.commit()
    theirs = await _alert(db_session, neighbour, "cmid-neighbour-01")

    response = await _post(client, ids.agent_id, "cmid-neighbour-01", RESOLVED, test_cert_pem)

    assert response.status_code == 404
    assert await _details(db_session, theirs) == {"attribution_state": "pending"}


@pytest.mark.asyncio
async def test_an_unknown_alert_is_404_with_the_same_answer(
    client: AsyncClient, db_session: AsyncSession, test_cert_serial: str, test_cert_pem: str,
) -> None:
    ids = await _tenant_and_agent(db_session, test_cert_serial)

    response = await _post(client, ids.agent_id, "cmid-nowhere-0001", RESOLVED, test_cert_pem)

    assert response.status_code == 404
    assert response.json()["detail"] == "Alert not found"


@pytest.mark.parametrize(
    "body",
    [
        {**RESOLVED, "extra": "field"},                                            # unknown field
        {"state": "maybe"},                                                        # state outside the closed list
        {"state": "resolved", "pid": 4242},                                        # resolved without its creation time
        {"state": "resolved", "process_created_at": "2026-10-21T09:57:00+00:00"},  # resolved without a pid
        {**RESOLVED, "reason": "system_pid"},                                     # resolved with a reason
        {"state": "unavailable"},                                                  # unavailable without its reason
        {"state": "unavailable", "reason": "because"},                            # reason outside the closed list
        {"state": "unavailable", "reason": "system_pid", "pid": 4},               # unavailable naming a process
        {"state": "ambiguous", "pid": 4242},                                       # ambiguous naming a process
        {**RESOLVED, "pid": 0},                                                    # pid out of range
        {**RESOLVED, "process_name": "x" * 261},                                   # name too long
        {**RESOLVED, "image_path": "C:\\" + "x" * 1022},                           # image path too long
    ],
)
@pytest.mark.asyncio
async def test_the_body_is_bounded(
    client: AsyncClient, db_session: AsyncSession, test_cert_serial: str, test_cert_pem: str, body: dict[str, Any],
) -> None:
    ids = await _tenant_and_agent(db_session, test_cert_serial)
    client_message_id = f"cmid-bound-{uuid4().hex[:8]}"
    alert_id = await _alert(db_session, ids, client_message_id)

    response = await _post(client, ids.agent_id, client_message_id, body, test_cert_pem)

    assert response.status_code == 422
    assert await _details(db_session, alert_id) == {"attribution_state": "pending"}


@pytest.mark.asyncio
async def test_the_console_reads_the_attribution_of_its_own_tenants_alert(client: AsyncClient) -> None:
    from tests.api.test_tenant_isolation import _create_two_tenants, _login
    from tests.conftest import _test_session_factory

    setup = await _create_two_tenants()
    async with _test_session_factory() as session:
        for key, value in {"attribution_state": "resolved", "process_id": "4242", "process_name": "evil",
                           "process_created_at": "2026-10-21T09:57:00+00:00"}.items():
            session.add(AlertDetail(id=str(uuid4()), tenant_id=setup["tenant_a_id"], alert_id=setup["alert_a_id"],
                                    key=key, value=value))
        await session.commit()
    token = await _login(client, setup["tenant_a_code"], setup["user_a_email"])

    detail = await client.get(f"/api/v1/dashboard/alerts/{setup['alert_a_id']}",
                              headers={"Authorization": f"Bearer {token}"})
    other = await client.get(f"/api/v1/dashboard/alerts/{setup['alert_b_id']}",
                             headers={"Authorization": f"Bearer {token}"})

    assert detail.status_code == 200
    assert detail.json()["attribution"] == {
        "state": "resolved", "reason": None, "process_id": 4242, "process_name": "evil",
        "process_path": None, "process_created_at": "2026-10-21T09:57:00+00:00",
    }
    assert other.status_code == 404


@pytest.mark.asyncio
async def test_an_alert_without_attribution_details_has_none(client: AsyncClient) -> None:
    from tests.api.test_tenant_isolation import _create_two_tenants, _login

    setup = await _create_two_tenants()
    token = await _login(client, setup["tenant_a_code"], setup["user_a_email"])

    detail = await client.get(f"/api/v1/dashboard/alerts/{setup['alert_a_id']}",
                              headers={"Authorization": f"Bearer {token}"})

    assert detail.json()["attribution"] is None
