"""GRID-USR-001 (one account per email), GRID-AUD-001 (role changes logged), GRID-ALR-001 (alert
transitions)."""

import os
import sqlite3
import subprocess
import sys
from pathlib import Path
from uuid import uuid4

import pytest
from httpx import AsyncClient
from sqlalchemy import select, update

from ransomguard_grid.core.alert_transitions import ALLOWED_TRANSITIONS
from ransomguard_grid.db.models.alerts import Alert
from ransomguard_grid.db.models.enums import AlertStatus
from ransomguard_grid.db.models.notifications import UserActionLog
from ransomguard_grid.db.models.tenant_user import Role
from tests.api.test_tenant_isolation import _create_two_tenants, _login
from tests.conftest import _test_session_factory

GRID = Path(__file__).resolve().parents[2]


async def _admin_headers(client: AsyncClient) -> tuple[dict[str, str], dict[str, str]]:
    setup = await _create_two_tenants()
    token = await _login(client, setup["tenant_a_code"], setup["user_a_email"])
    return {"Authorization": f"Bearer {token}"}, setup


async def _ensure_role(name: str) -> None:
    async with _test_session_factory() as session:
        if (await session.execute(select(Role).where(Role.name == name))).scalar_one_or_none() is None:
            session.add(Role(id=str(uuid4()), name=name, description=name))
            await session.commit()


# ── GRID-USR-001 ──

@pytest.mark.asyncio
async def test_a_second_account_with_the_same_email_is_refused_whatever_the_case(client: AsyncClient) -> None:
    headers, _ = await _admin_headers(client)
    body = {"email": "nurse.one@hopital.cm", "full_name": "Nurse One", "password": "Long-Enough-1!"}

    assert (await client.post("/api/v1/dashboard/users", headers=headers, json=body)).status_code == 201
    again = await client.post("/api/v1/dashboard/users", headers=headers, json={**body, "email": "  Nurse.ONE@Hopital.cm "})

    assert again.status_code == 409


@pytest.mark.asyncio
async def test_emails_are_stored_lower_case_and_login_ignores_the_case(client: AsyncClient) -> None:
    headers, setup = await _admin_headers(client)
    created = await client.post("/api/v1/dashboard/users", headers=headers,
                                json={"email": "Mixed.Case@Hopital.CM", "full_name": "Mixed", "password": "Long-Enough-1!"})
    assert created.json()["email"] == "mixed.case@hopital.cm"

    login = await client.post("/api/v1/auth/login", json={
        "tenant_code": setup["tenant_a_code"], "email": "MIXED.case@hopital.cm", "password": "Long-Enough-1!",
    })
    assert login.status_code == 200


def _alembic(db_path: Path, *args: str) -> subprocess.CompletedProcess[str]:
    env = {**os.environ, "GRID_DATABASE_URL": f"sqlite+aiosqlite:///{db_path.as_posix()}"}
    return subprocess.run([sys.executable, "-m", "alembic", *args], cwd=GRID, env=env,
                          capture_output=True, text=True, timeout=120)


def _insert_user(db_path: Path, tenant_id: str, email: str) -> None:
    with sqlite3.connect(db_path) as conn:
        conn.execute(
            "INSERT INTO users (id, tenant_id, email, hashed_password, full_name, is_active, created_at) "
            "VALUES (?, ?, ?, 'x', 'u', 1, '2026-10-14 00:00:00')",
            (str(uuid4()), tenant_id, email),
        )


def _with_tenant(db_path: Path) -> str:
    tenant_id = str(uuid4())
    with sqlite3.connect(db_path) as conn:
        conn.execute(
            "INSERT INTO tenants (id, name, code, contact_email, status, created_at) "
            "VALUES (?, 'T', ?, 't@t', 'active', '2026-10-14 00:00:00')",
            (tenant_id, f"t-{tenant_id[:6]}"),
        )
    return tenant_id


def test_the_migration_refuses_when_duplicates_exist_and_merges_nothing(tmp_path: Path) -> None:
    db = tmp_path / "dup.db"
    assert _alembic(db, "upgrade", "81e3f05713a7").returncode == 0
    tenant = _with_tenant(db)
    _insert_user(db, tenant, "dup@hopital.cm")
    _insert_user(db, tenant, "DUP@hopital.cm")

    result = _alembic(db, "upgrade", "head")

    assert result.returncode != 0
    assert "1 duplicated (tenant, email) found, migration refused" in result.stderr
    with sqlite3.connect(db) as conn:
        assert conn.execute("SELECT COUNT(*) FROM users").fetchone()[0] == 2  # nothing merged


def test_the_migration_lower_cases_emails_and_enforces_uniqueness(tmp_path: Path) -> None:
    db = tmp_path / "ok.db"
    assert _alembic(db, "upgrade", "81e3f05713a7").returncode == 0
    tenant = _with_tenant(db)
    _insert_user(db, tenant, " Head.Nurse@Hopital.CM ")

    result = _alembic(db, "upgrade", "head")

    assert result.returncode == 0, result.stderr
    with sqlite3.connect(db) as conn:
        assert conn.execute("SELECT email FROM users").fetchone()[0] == "head.nurse@hopital.cm"
    with pytest.raises(sqlite3.IntegrityError):
        _insert_user(db, tenant, "head.nurse@hopital.cm")


# ── GRID-AUD-001 ──

@pytest.mark.asyncio
async def test_a_role_change_is_logged_with_before_after_who_and_when(client: AsyncClient) -> None:
    await _ensure_role("security_analyst")
    await _ensure_role("read_only_auditor")
    headers, _ = await _admin_headers(client)
    created = await client.post("/api/v1/dashboard/users", headers=headers, json={
        "email": "analyst.two@hopital.cm", "full_name": "Analyst", "password": "Long-Enough-1!",
        "roles": ["security_analyst"],
    })
    user_id = created.json()["id"]

    changed = await client.put(f"/api/v1/dashboard/users/{user_id}/roles", headers=headers,
                               json={"role_names": ["read_only_auditor"]})
    assert changed.status_code == 200

    async with _test_session_factory() as session:
        [log] = (await session.execute(
            select(UserActionLog).where(UserActionLog.action_type == "user_roles_change")
        )).scalars().all()
    assert log.target_id == user_id
    assert (log.details_json or {})["before"] == ["security_analyst"]
    assert (log.details_json or {})["after"] == ["read_only_auditor"]
    assert log.actor_user_id and log.created_at


# ── GRID-ALR-001 ──

async def _set_status(alert_id: str, status: AlertStatus) -> None:
    async with _test_session_factory() as session:
        await session.execute(update(Alert).where(Alert.id == alert_id).values(status=status))
        await session.commit()


ALLOWED = [(old, new) for old, targets in ALLOWED_TRANSITIONS.items() for new in sorted(targets, key=lambda s: s.value)]


def test_the_transition_table_is_the_console_workflow() -> None:
    assert [
        (AlertStatus.New, AlertStatus.Investigating),
        (AlertStatus.Investigating, AlertStatus.FalsePositive),
        (AlertStatus.Investigating, AlertStatus.Resolved),
        (AlertStatus.Investigating, AlertStatus.Suppressed),
    ] == ALLOWED


@pytest.mark.asyncio
@pytest.mark.parametrize(("old", "new"), ALLOWED)
async def test_every_allowed_transition_is_accepted(client: AsyncClient, old: AlertStatus, new: AlertStatus) -> None:
    headers, setup = await _admin_headers(client)
    await _set_status(setup["alert_a_id"], old)

    response = await client.post(f"/api/v1/dashboard/alerts/{setup['alert_a_id']}/status", headers=headers,
                                 json={"new_status": new.value, "justification": "Workflow step in the test"})

    assert response.status_code == 200
    assert response.json()["status"] == new.value


@pytest.mark.asyncio
@pytest.mark.parametrize(
    ("old", "new"),
    [
        (AlertStatus.New, AlertStatus.Resolved),            # closing without acknowledging
        (AlertStatus.Resolved, AlertStatus.New),            # reopening a closed alert
        (AlertStatus.Investigating, AlertStatus.Investigating),  # the same status twice
        (AlertStatus.FalsePositive, AlertStatus.Investigating),
    ],
)
async def test_a_forbidden_transition_is_refused_409_and_changes_nothing(
    client: AsyncClient, old: AlertStatus, new: AlertStatus,
) -> None:
    headers, setup = await _admin_headers(client)
    await _set_status(setup["alert_a_id"], old)

    response = await client.post(f"/api/v1/dashboard/alerts/{setup['alert_a_id']}/status", headers=headers,
                                 json={"new_status": new.value, "justification": "Not a valid workflow step"})

    assert response.status_code == 409
    async with _test_session_factory() as session:
        alert = await session.get(Alert, setup["alert_a_id"])
        assert alert is not None and alert.status == old
