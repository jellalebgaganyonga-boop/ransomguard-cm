"""Heartbeat v2: posture storage, transitions, v1 compatibility, body validation, and the agent page."""

from datetime import UTC, datetime, timedelta
from typing import Any
from uuid import uuid4

import pytest
from httpx import AsyncClient
from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from ransomguard_grid.core.security import hash_password
from ransomguard_grid.db.models.agent import Agent, AgentModuleStatus, AgentPosture, AgentPostureChange
from ransomguard_grid.db.models.enums import AgentStatus
from ransomguard_grid.db.models.operations import SystemLog
from ransomguard_grid.db.models.tenant_user import Role, Tenant, User, UserRole
from tests.api.test_heartbeat import _setup
from tests.api.test_tenant_isolation import _PASSWORD, _login

MODULES = ("SENTINEL", "ENTROPY", "GENEALOGY", "USB_GUARD", "EXFIL_WATCH", "ANTI_TAMPERING")


def _v2(**overrides: Any) -> dict[str, Any]:
    """A v2 heartbeat as the agent serialises it (GridModels.HeartbeatRequest)."""
    changed = datetime.now(UTC).isoformat()
    states = {
        "SENTINEL": ("active", None), "ENTROPY": ("active", None), "USB_GUARD": ("active", None),
        "EXFIL_WATCH": ("active", None), "GENEALOGY": ("degraded", "attribution_unreliable"),
        "ANTI_TAMPERING": ("inactive", "not_implemented"),
    }
    states.update(overrides.pop("states", {}))
    body: dict[str, Any] = {
        "schema_version": 2,
        "agent_uptime_seconds": 3600,
        "agent_version": "0.4.0",
        "os_version": "Microsoft Windows NT 10.0.26300.0",
        "threat_intel_version": "2026-10-11-001",
        "last_detection_at": None,
        "disk": {"system_free_bytes": 50_000_000_000, "db_volume_free_bytes": 40_000_000_000},
        "transmission": {"pending_alerts": 0, "last_upload_ok_at": changed, "last_upload_error_code": None},
        "modules": [
            {"code": code, "state": state, "reason_code": reason, "changed_at": changed}
            for code, (state, reason) in states.items()
        ],
    }
    body.update(overrides)
    return body


async def _beat(client: AsyncClient, agent: Agent, cert: str, body: dict[str, Any]) -> int:
    response = await client.post(f"/api/v1/agents/{agent.id}/heartbeat", json=body, headers={"X-Client-Cert": cert})
    return response.status_code


async def _rows(db: AsyncSession, model: Any, agent_id: str) -> list[Any]:
    # The API wrote through another session: reload these rows, without expiring the test's own objects.
    stmt = select(model).where(model.agent_id == agent_id).execution_options(populate_existing=True)
    return list((await db.execute(stmt)).scalars().all())


# ── Storage ──

@pytest.mark.asyncio
async def test_v2_heartbeat_stores_the_posture_and_the_six_modules(
    client: AsyncClient, db_session: AsyncSession, test_cert_serial: str, test_cert_pem: str,
) -> None:
    _, agent = await _setup(db_session, test_cert_serial)

    assert await _beat(client, agent, test_cert_pem, _v2()) == 200

    [posture] = await _rows(db_session, AgentPosture, agent.id)
    assert (posture.schema_version, posture.pending_alerts, posture.system_free_bytes) == (2, 0, 50_000_000_000)
    assert posture.tenant_id == agent.tenant_id
    modules = {m.module_code: (m.state, m.reason_code) for m in await _rows(db_session, AgentModuleStatus, agent.id)}
    assert set(modules) == set(MODULES)
    assert modules["GENEALOGY"] == ("degraded", "attribution_unreliable")


@pytest.mark.asyncio
async def test_v2_heartbeat_updates_the_agent_and_os_versions(
    client: AsyncClient, db_session: AsyncSession, test_cert_serial: str, test_cert_pem: str,
) -> None:
    _, agent = await _setup(db_session, test_cert_serial)

    await _beat(client, agent, test_cert_pem, _v2())

    refreshed = await db_session.get(Agent, agent.id, populate_existing=True)
    assert refreshed is not None
    assert (refreshed.agent_version, refreshed.os_version) == ("0.4.0", "Microsoft Windows NT 10.0.26300.0")


@pytest.mark.asyncio
async def test_null_measurements_are_stored_as_null_never_zero(
    client: AsyncClient, db_session: AsyncSession, test_cert_serial: str, test_cert_pem: str,
) -> None:
    _, agent = await _setup(db_session, test_cert_serial)

    body = _v2(
        disk={"system_free_bytes": None, "db_volume_free_bytes": None},
        transmission={"pending_alerts": None, "last_upload_ok_at": None, "last_upload_error_code": "TIMEOUT"},
    )
    assert await _beat(client, agent, test_cert_pem, body) == 200

    [posture] = await _rows(db_session, AgentPosture, agent.id)
    assert (posture.system_free_bytes, posture.db_volume_free_bytes, posture.pending_alerts) == (None, None, None)
    assert posture.rejected_alerts is None  # absent from the payload: not reported, not 0
    assert posture.last_upload_error_code == "TIMEOUT"


@pytest.mark.asyncio
async def test_rejected_alerts_is_stored(
    client: AsyncClient, db_session: AsyncSession, test_cert_serial: str, test_cert_pem: str,
) -> None:
    _, agent = await _setup(db_session, test_cert_serial)

    body = _v2(transmission={"pending_alerts": 0, "rejected_alerts": 3, "last_upload_ok_age_seconds": 42})
    assert await _beat(client, agent, test_cert_pem, body) == 200

    [posture] = await _rows(db_session, AgentPosture, agent.id)
    assert (posture.rejected_alerts, posture.last_upload_ok_age_seconds) == (3, 42)


@pytest.mark.asyncio
async def test_audit_write_failures_is_stored_and_absent_means_not_reported(
    client: AsyncClient, db_session: AsyncSession, test_cert_serial: str, test_cert_pem: str,
) -> None:
    _, agent = await _setup(db_session, test_cert_serial)

    assert await _beat(client, agent, test_cert_pem, _v2(transmission={"pending_alerts": 0})) == 200
    [posture] = await _rows(db_session, AgentPosture, agent.id)
    assert posture.audit_write_failures is None  # an older agent: not reported, not 0

    body = _v2(transmission={"pending_alerts": 0, "audit_write_failures": 4})
    assert await _beat(client, agent, test_cert_pem, body) == 200
    await db_session.refresh(posture)
    assert posture.audit_write_failures == 4


# ── Transitions: one row per change, not per beat ──

@pytest.mark.asyncio
async def test_a_transition_is_written_only_when_a_module_changes(
    client: AsyncClient, db_session: AsyncSession, test_cert_serial: str, test_cert_pem: str,
) -> None:
    _, agent = await _setup(db_session, test_cert_serial)

    await _beat(client, agent, test_cert_pem, _v2())  # 6 first states
    await _beat(client, agent, test_cert_pem, _v2())  # nothing changed
    await _beat(client, agent, test_cert_pem, _v2(states={"EXFIL_WATCH": ("inactive", "admin_rights_missing")}))
    await _beat(client, agent, test_cert_pem, _v2(states={"EXFIL_WATCH": ("inactive", "admin_rights_missing")}))

    changes = await _rows(db_session, AgentPostureChange, agent.id)
    assert len(changes) == 7
    [exfil] = [c for c in changes if c.module_code == "EXFIL_WATCH" and c.old_state is not None]
    assert (exfil.old_state, exfil.new_state, exfil.new_reason_code) == ("active", "inactive", "admin_rights_missing")


@pytest.mark.asyncio
async def test_a_reason_change_alone_is_a_transition(
    client: AsyncClient, db_session: AsyncSession, test_cert_serial: str, test_cert_pem: str,
) -> None:
    _, agent = await _setup(db_session, test_cert_serial)

    await _beat(client, agent, test_cert_pem, _v2(states={"ENTROPY": ("inactive", "starting")}))
    await _beat(client, agent, test_cert_pem, _v2(states={"ENTROPY": ("inactive", "init_failed")}))

    [change] = [c for c in await _rows(db_session, AgentPostureChange, agent.id) if c.old_state is not None]
    assert (change.old_reason_code, change.new_reason_code) == ("starting", "init_failed")


@pytest.mark.asyncio
async def test_a_module_missing_from_a_later_heartbeat_becomes_not_reported(
    client: AsyncClient, db_session: AsyncSession, test_cert_serial: str, test_cert_pem: str,
) -> None:
    _, agent = await _setup(db_session, test_cert_serial)
    await _beat(client, agent, test_cert_pem, _v2())

    body = _v2()
    body["modules"] = [m for m in body["modules"] if m["code"] != "SENTINEL"]
    await _beat(client, agent, test_cert_pem, body)

    sentinel = {m.module_code: m for m in await _rows(db_session, AgentModuleStatus, agent.id)}["SENTINEL"]
    assert (sentinel.state, sentinel.reason_code) == ("inactive", "not_reported")


# ── v1 compatibility ──

@pytest.mark.asyncio
async def test_v1_heartbeat_is_accepted_ignored_and_never_logged(
    client: AsyncClient, db_session: AsyncSession, test_cert_serial: str, test_cert_pem: str,
) -> None:
    _, agent = await _setup(db_session, test_cert_serial)

    # What a v1 agent (DESKTOP-3EKNJFI) really sends: lowercase codes, free-form states.
    v1 = {"agent_uptime_seconds": 100, "modules_status": {"sentinel": "disabled", "entropy": "enabled"}}
    for _ in range(3):
        assert await _beat(client, agent, test_cert_pem, v1) == 200

    [posture] = await _rows(db_session, AgentPosture, agent.id)
    assert posture.schema_version == 1
    assert await _rows(db_session, AgentModuleStatus, agent.id) == []
    logs = (await db_session.execute(select(SystemLog).where(SystemLog.tenant_id == agent.tenant_id))).scalars().all()
    assert logs == []


# ── Body validation ──

@pytest.mark.asyncio
@pytest.mark.parametrize(
    "body",
    [
        _v2(states={"SENTINEL": ("running", None)}),  # state outside the closed set
        _v2(states={"SENTINEL": ("inactive", "Admin Rights Missing")}),  # a sentence, not a code
        _v2(transmission={"last_upload_error_code": "Connection refused (grid.local:443)"}),  # exception text
        _v2(transmission={"pending_alerts": -1}),
        _v2(transmission={"last_upload_ok_age_seconds": -1}),
        _v2(transmission={"last_upload_ok_age_seconds": 10 * 365 * 24 * 3600 + 1}),
        _v2(disk={"system_free_bytes": -5}),
        _v2(agent_uptime_seconds=-1),
        _v2(schema_version=3),
        _v2(unexpected_field=1),
        {**_v2(), "modules": [*_v2()["modules"], _v2()["modules"][0]]},  # duplicate module
        {**_v2(), "modules": _v2()["modules"] * 3},  # more than 16 entries
        {"agent_uptime_seconds": 1, "modules_status": {"x" * 65: "on"}},  # v1 key too long
    ],
)
async def test_an_invalid_body_is_refused_and_nothing_is_stored(
    client: AsyncClient, db_session: AsyncSession, test_cert_serial: str, test_cert_pem: str, body: dict[str, Any],
) -> None:
    _, agent = await _setup(db_session, test_cert_serial)

    assert await _beat(client, agent, test_cert_pem, body) == 422
    assert await _rows(db_session, AgentPosture, agent.id) == []


@pytest.mark.asyncio
async def test_an_unknown_module_is_not_stored_and_logged_once_per_hour_with_a_count(
    client: AsyncClient, db_session: AsyncSession, test_cert_serial: str, test_cert_pem: str,
) -> None:
    _, agent = await _setup(db_session, test_cert_serial)
    body = _v2()
    body["modules"].append({"code": "QUANTUM_SHIELD", "state": "active", "reason_code": None,
                            "changed_at": datetime.now(UTC).isoformat()})

    for _ in range(4):
        assert await _beat(client, agent, test_cert_pem, body) == 200

    assert "QUANTUM_SHIELD" not in {m.module_code for m in await _rows(db_session, AgentModuleStatus, agent.id)}
    [log] = (await db_session.execute(
        select(SystemLog).where(SystemLog.component == "heartbeat").execution_options(populate_existing=True)
    )).scalars().all()
    assert (log.context_json or {})["count"] == 4
    assert (log.context_json or {})["family"] == "unknown_module"  # capped at 20 codes per tenant and hour
    assert "'QUANTUM_SHIELD'" in log.message


@pytest.mark.asyncio
async def test_the_body_cannot_name_another_agent(
    client: AsyncClient, db_session: AsyncSession, test_cert_serial: str, test_cert_pem: str,
) -> None:
    _, agent = await _setup(db_session, test_cert_serial)

    # The certificate identifies the agent; a path naming another agent is refused.
    other = str(uuid4())
    response = await client.post(f"/api/v1/agents/{other}/heartbeat", json=_v2(), headers={"X-Client-Cert": test_cert_pem})
    assert response.status_code == 403
    assert await _rows(db_session, AgentPosture, other) == []


# ── The agent page ──

async def _admin_headers(client: AsyncClient, db: AsyncSession, tenant: Tenant) -> dict[str, str]:
    role = (await db.execute(select(Role).where(Role.name == "tenant_admin"))).scalar_one_or_none()
    if role is None:
        role = Role(id=str(uuid4()), name="tenant_admin", description="Admin")
        db.add(role)
        await db.flush()
    user = User(
        id=str(uuid4()), tenant_id=tenant.id, email=f"admin-{uuid4().hex[:6]}@t.local",
        hashed_password=hash_password(_PASSWORD), full_name="Admin", is_active=True, created_at=datetime.now(UTC),
    )
    db.add(user)
    await db.flush()
    db.add(UserRole(user_id=user.id, role_id=role.id, granted_at=datetime.now(UTC), granted_by_user_id=user.id))
    await db.commit()
    return {"Authorization": f"Bearer {await _login(client, tenant.code, user.email)}"}


@pytest.mark.asyncio
async def test_agent_page_carries_verdict_reasons_and_posture_in_display_order(
    client: AsyncClient, db_session: AsyncSession, test_cert_serial: str, test_cert_pem: str,
) -> None:
    tenant, agent = await _setup(db_session, test_cert_serial)
    await _beat(client, agent, test_cert_pem, _v2(states={"EXFIL_WATCH": ("inactive", "admin_rights_missing")}))
    headers = await _admin_headers(client, db_session, tenant)

    detail = (await client.get(f"/api/v1/dashboard/agents/{agent.id}", headers=headers)).json()

    assert detail["protection_state"] == "partial"
    assert detail["reason_codes"] == ["EXFIL_WATCH:inactive:admin_rights_missing"]
    assert detail["signal_age_seconds"] < 60
    # The heartbeat carried every module, ANTI_TAMPERING among them, and was accepted in full --
    # an agent older than 2026-10-26 still sends it and must not have its whole heartbeat
    # refused over one row. The console simply never shows it: a line on every workstation
    # reading "inactive, not implemented" is true and useless.
    shown = [m["code"] for m in detail["posture"]["modules"]]
    assert shown == [code for code in MODULES if code != "ANTI_TAMPERING"]
    assert "ANTI_TAMPERING" not in shown
    assert detail["posture"]["pending_alerts"] == 0
    assert detail["posture"]["rejected_alerts"] is None
    assert 0 <= detail["posture"]["last_upload_age_seconds"] < 60

    listed = (await client.get("/api/v1/dashboard/agents", headers=headers)).json()["items"]
    [row] = [a for a in listed if a["id"] == agent.id]
    assert (row["protection_state"], row["reason_codes"]) == ("partial", ["EXFIL_WATCH:inactive:admin_rights_missing"])
    assert "posture" not in row


@pytest.mark.asyncio
async def test_agent_page_of_a_never_connected_agent(
    client: AsyncClient, db_session: AsyncSession, test_cert_serial: str,
) -> None:
    tenant, agent = await _setup(db_session, test_cert_serial)
    agent_row = await db_session.get(Agent, agent.id)
    assert agent_row is not None
    agent_row.last_heartbeat_at = None
    await db_session.commit()
    headers = await _admin_headers(client, db_session, tenant)

    detail = (await client.get(f"/api/v1/dashboard/agents/{agent.id}", headers=headers)).json()

    assert (detail["protection_state"], detail["reason_codes"], detail["posture"]) == ("unknown", ["never_connected"], None)


@pytest.mark.asyncio
async def test_the_verdict_ages_without_any_new_heartbeat(
    client: AsyncClient, db_session: AsyncSession, test_cert_serial: str, test_cert_pem: str,
) -> None:
    tenant, agent = await _setup(db_session, test_cert_serial)
    await _beat(client, agent, test_cert_pem, _v2())
    # Ten minutes pass, nothing arrives: computed at read time, the verdict must follow.
    agent_row = await db_session.get(Agent, agent.id)
    assert agent_row is not None
    agent_row.last_heartbeat_at = datetime.now(UTC) - timedelta(minutes=10)
    await db_session.commit()
    headers = await _admin_headers(client, db_session, tenant)

    detail = (await client.get(f"/api/v1/dashboard/agents/{agent.id}", headers=headers)).json()

    assert (detail["protection_state"], detail["reason_codes"]) == ("partial", ["signal_lost"])


# ── Restart loop ──

async def _agent_rows(db: AsyncSession, agent_id: str) -> list[AgentPostureChange]:
    return [c for c in await _rows(db, AgentPostureChange, agent_id) if c.module_code == "AGENT"]


@pytest.mark.asyncio
async def test_a_restart_is_recorded_jitter_is_not(
    client: AsyncClient, db_session: AsyncSession, test_cert_serial: str, test_cert_pem: str,
) -> None:
    _, agent = await _setup(db_session, test_cert_serial)

    await _beat(client, agent, test_cert_pem, _v2(agent_uptime_seconds=1000))
    await _beat(client, agent, test_cert_pem, _v2(agent_uptime_seconds=950))  # 50 s back: jitter
    assert await _agent_rows(db_session, agent.id) == []

    await _beat(client, agent, test_cert_pem, _v2(agent_uptime_seconds=100))  # restarted
    [restart] = await _agent_rows(db_session, agent.id)
    assert (restart.old_state, restart.new_state) == (None, "restarted")


async def _plant_restarts(db: AsyncSession, agent: Agent, *ages: timedelta) -> None:
    for age in ages:
        db.add(AgentPostureChange(
            id=str(uuid4()), tenant_id=agent.tenant_id, agent_id=agent.id, module_code="AGENT",
            old_state=None, new_state="restarted", created_at=datetime.now(UTC) - age,
        ))
    await db.commit()


@pytest.mark.asyncio
@pytest.mark.parametrize(
    ("ages", "state", "reasons"),
    [
        # 3 restarts in 60 min, SENTINEL starting at 100 s: red, restart_loop.
        ((timedelta(minutes=5), timedelta(minutes=20), timedelta(minutes=40)),
         "unprotected", ["SENTINEL:inactive:init_failed", "restart_loop"]),
        # 2 restarts: still the start-up grace.
        ((timedelta(minutes=5), timedelta(minutes=20)), "unknown", ["SENTINEL:starting", "starting"]),
        # 3 restarts, one of them 61 min old: 2 in the hour, grey.
        ((timedelta(minutes=5), timedelta(minutes=20), timedelta(minutes=61)),
         "unknown", ["SENTINEL:starting", "starting"]),
    ],
)
async def test_restarts_of_the_last_hour_decide_whether_starting_keeps_its_grace(
    client: AsyncClient, db_session: AsyncSession, test_cert_serial: str, test_cert_pem: str,
    ages: tuple[timedelta, ...], state: str, reasons: list[str],
) -> None:
    tenant, agent = await _setup(db_session, test_cert_serial)
    await _beat(client, agent, test_cert_pem, _v2(agent_uptime_seconds=100, states={"SENTINEL": ("inactive", "starting")}))
    await _plant_restarts(db_session, agent, *ages)
    headers = await _admin_headers(client, db_session, tenant)

    detail = (await client.get(f"/api/v1/dashboard/agents/{agent.id}", headers=headers)).json()

    assert (detail["protection_state"], detail["reason_codes"]) == (state, reasons)


@pytest.mark.asyncio
async def test_a_decommissioned_agent_is_not_in_the_list_unless_it_is_asked_for(
    client: AsyncClient, db_session: AsyncSession, test_cert_serial: str,
) -> None:
    """A rebuilt workstation must not fill the page with copies of itself.

    Reinstalling brings a workstation back as a new agent and the old row stays, with its alerts
    and its audit history. On the bench one workstation ended up listed five times, four of them
    dead, and nothing could be done about it from the console. The dead ones are hidden now --
    hidden, not deleted: they are still reachable by id and still returned when asked for.
    """
    tenant, agent = await _setup(db_session, test_cert_serial)
    headers = await _admin_headers(client, db_session, tenant)

    listed = (await client.get("/api/v1/dashboard/agents", headers=headers)).json()
    assert [i["id"] for i in listed["items"]] == [agent.id]
    assert listed["total"] == 1

    row = await db_session.get(Agent, agent.id)
    assert row is not None
    row.status = AgentStatus.decommissioned
    await db_session.commit()

    hidden = (await client.get("/api/v1/dashboard/agents", headers=headers)).json()
    assert hidden["items"] == []
    assert hidden["total"] == 0, "the count must agree with the list, or the page shows 1 of 0"

    asked_for = (
        await client.get("/api/v1/dashboard/agents?include_decommissioned=true", headers=headers)
    ).json()
    assert [i["id"] for i in asked_for["items"]] == [agent.id]

    by_status = (
        await client.get("/api/v1/dashboard/agents?status=decommissioned", headers=headers)
    ).json()
    assert [i["id"] for i in by_status["items"]] == [agent.id]

    # Still reachable on its own page: the history behind it did not go anywhere.
    page = await client.get(f"/api/v1/dashboard/agents/{agent.id}", headers=headers)
    assert page.status_code == 200
