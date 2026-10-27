"""RBAC role distinction tests: analyst vs auditor vs admin separation of duties."""

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

_PASSWORD = "TestPass123!"


async def _setup_rbac_env() -> dict[str, str]:
    """Create tenant with admin, analyst, auditor users, an agent, and an alert."""
    suffix = uuid4().hex[:6]
    result: dict[str, str] = {}

    async with _test_session_factory() as session:
        tenant = Tenant(id=str(uuid4()), name="RBAC Tenant", code=f"rbac-{suffix}", contact_email="r@t.local", status=TenantStatus.active)
        session.add(tenant)
        result["tenant_code"] = tenant.code
        result["tenant_id"] = tenant.id

        # Ensure roles exist
        from sqlalchemy import select
        role_map: dict[str, str] = {}
        for role_name in ("tenant_admin", "security_analyst", "read_only_auditor"):
            existing = (await session.execute(select(Role).where(Role.name == role_name))).scalar_one_or_none()
            if not existing:
                existing = Role(id=str(uuid4()), name=role_name, description=role_name)
                session.add(existing)
                await session.flush()
            role_map[role_name] = existing.id

        # Create 3 users with different roles
        for label, role_name in [("admin", "tenant_admin"), ("analyst", "security_analyst"), ("auditor", "read_only_auditor")]:
            user = User(id=str(uuid4()), tenant_id=tenant.id, email=f"{label}-{suffix}@test.local", hashed_password=hash_password(_PASSWORD), full_name=f"User {label}", is_active=True, created_at=datetime.now(UTC))
            session.add(user)
            await session.flush()
            session.add(UserRole(user_id=user.id, role_id=role_map[role_name], granted_at=datetime.now(UTC), granted_by_user_id=user.id))
            result[f"{label}_email"] = user.email
            result[f"{label}_id"] = user.id

        # Agent + alert for testing
        agent = Agent(id=str(uuid4()), tenant_id=tenant.id, hostname="rbac-host", fqdn="rbac.local", os_version="10", agent_version="1.0", hardware_fingerprint=f"fp-rbac-{suffix}", status=AgentStatus.active)
        session.add(agent)
        alert = Alert(id=str(uuid4()), tenant_id=tenant.id, agent_id=agent.id, client_message_id=f"rbac-{suffix}", alert_type="USB", severity=Severity.High, status=AlertStatus.New, detected_at=datetime.now(UTC), summary="RBAC test alert")
        session.add(alert)
        await session.flush()
        result["alert_id"] = alert.id
        result["agent_id"] = agent.id

        await session.commit()
    return result


async def _login(client: AsyncClient, tenant_code: str, email: str) -> str:
    resp = await client.post("/api/v1/auth/login", json={"tenant_code": tenant_code, "email": email, "password": _PASSWORD})
    assert resp.status_code == 200, f"Login failed: {resp.json()}"
    return resp.json()["access_token"]


@pytest.mark.asyncio
async def test_analyst_can_update_alert_status(client: AsyncClient) -> None:
    """security_analyst CAN modify alert status."""
    env = await _setup_rbac_env()
    token = await _login(client, env["tenant_code"], env["analyst_email"])

    resp = await client.post(
        f"/api/v1/dashboard/alerts/{env['alert_id']}/status",
        json={"new_status": "Investigating", "justification": "Looking into it now"},
        headers={"Authorization": f"Bearer {token}"},
    )
    assert resp.status_code == 200


@pytest.mark.asyncio
async def test_auditor_cannot_update_alert_status(client: AsyncClient) -> None:
    """read_only_auditor CANNOT modify alert status — separation of duties."""
    env = await _setup_rbac_env()
    token = await _login(client, env["tenant_code"], env["auditor_email"])

    resp = await client.post(
        f"/api/v1/dashboard/alerts/{env['alert_id']}/status",
        json={"new_status": "Investigating", "justification": "Looking into it now"},
        headers={"Authorization": f"Bearer {token}"},
    )
    assert resp.status_code == 403
    assert "Insufficient permissions" in resp.json()["detail"]


@pytest.mark.asyncio
async def test_analyst_cannot_search_audit_logs(client: AsyncClient) -> None:
    """Audit logs for compliance auditors and admins only — NOT security analysts."""
    env = await _setup_rbac_env()
    token = await _login(client, env["tenant_code"], env["analyst_email"])

    resp = await client.get("/api/v1/dashboard/audit-logs", headers={"Authorization": f"Bearer {token}"})
    assert resp.status_code == 403


@pytest.mark.asyncio
async def test_auditor_can_search_audit_logs(client: AsyncClient) -> None:
    """Auditor has audit log read access — that's their primary purpose."""
    env = await _setup_rbac_env()
    token = await _login(client, env["tenant_code"], env["auditor_email"])

    resp = await client.get("/api/v1/dashboard/audit-logs", headers={"Authorization": f"Bearer {token}"})
    assert resp.status_code == 200


@pytest.mark.asyncio
async def test_only_admin_can_update_roles(client: AsyncClient) -> None:
    """Role management is admin-only. Analyst cannot update roles."""
    env = await _setup_rbac_env()
    token = await _login(client, env["tenant_code"], env["analyst_email"])

    resp = await client.put(
        f"/api/v1/dashboard/users/{env['auditor_id']}/roles",
        json={"role_names": ["security_analyst"]},
        headers={"Authorization": f"Bearer {token}"},
    )
    assert resp.status_code == 403


@pytest.mark.asyncio
async def test_admin_cannot_self_demote(client: AsyncClient) -> None:
    """The only tenant admin cannot remove their own admin role (anti-brick): as the last active
    administrator of the tenant, 409 (GRID-SEC-002)."""
    env = await _setup_rbac_env()
    token = await _login(client, env["tenant_code"], env["admin_email"])

    resp = await client.put(
        f"/api/v1/dashboard/users/{env['admin_id']}/roles",
        json={"role_names": ["security_analyst"]},
        headers={"Authorization": f"Bearer {token}"},
    )
    assert resp.status_code == 409
    assert "last active administrator" in resp.json()["detail"].lower()


@pytest.mark.asyncio
async def test_updating_roles_returns_the_whole_user_the_console_asks_for(client: AsyncClient) -> None:
    """The role change succeeds AND the answer is a complete user.

    The console parses this response strictly. It was missing ``last_login_at``, so a perfectly
    successful 200 failed to parse and the dialog showed "An error occurred" over a change that
    had in fact been saved -- the owner reported it on 2026-10-27 for kelyfa3@gmail.com, whose
    role had been assigned each time they tried.
    """
    env = await _setup_rbac_env()
    token = await _login(client, env["tenant_code"], env["admin_email"])

    resp = await client.put(
        f"/api/v1/dashboard/users/{env['auditor_id']}/roles",
        json={"role_names": ["security_analyst"]},
        headers={"Authorization": f"Bearer {token}"},
    )

    assert resp.status_code == 200
    body = resp.json()
    assert body["roles"] == ["security_analyst"]
    # Every field the console requires, present. A missing one is an error message over a
    # change that worked, which is worse than a failure: the admin retries and sees it again.
    assert set(body) >= {"id", "email", "full_name", "is_active", "last_login_at", "roles"}


@pytest.mark.asyncio
async def test_the_console_can_read_current_roles_before_changing_them(client: AsyncClient) -> None:
    """The dialog pre-ticks what the user has, instead of asking an admin to remember.

    Replacing roles blind is how an admin removes one by accident: the list sent replaces
    everything. Tenant-scoped, admin-only, like every other user endpoint.
    """
    env = await _setup_rbac_env()
    token = await _login(client, env["tenant_code"], env["admin_email"])

    resp = await client.get(
        "/api/v1/dashboard/users-with-roles",
        headers={"Authorization": f"Bearer {token}"},
    )

    assert resp.status_code == 200
    users = {u["email"]: u for u in resp.json()["items"]}
    assert users[env["auditor_email"]]["roles"] == ["read_only_auditor"]
    assert set(users[env["auditor_email"]]) >= {"id", "email", "full_name", "is_active", "last_login_at", "roles"}


@pytest.mark.asyncio
async def test_audit_entries_come_back_in_chain_order_within_one_arrival(client: AsyncClient) -> None:
    """A batch that lands in the same second still reads as a chain.

    The console flags a missing entry by comparing one row with the row above it. Ordering only
    by received_at let the database return a hundred same-second entries in any order, so a
    complete chain was covered in "Sequence gap detected" -- seen on 2026-10-27 the moment real
    entries first arrived.
    """
    from datetime import UTC, datetime
    from uuid import uuid4

    from ransomguard_grid.db.models.alerts import AuditLog as GridAuditLog

    env = await _setup_rbac_env()
    arrived = datetime.now(UTC)
    async with _test_session_factory() as session:
        agent_id = env["agent_id"]
        for seq in (3, 1, 4, 2):  # inserted out of order, one timestamp
            session.add(GridAuditLog(
                id=str(uuid4()), tenant_id=env["tenant_id"], agent_id=agent_id,
                sequence_number=seq, payload_json={"action": f"n{seq}"},
                ed25519_signature="c2ln", signing_key_id="k1", received_at=arrived,
            ))
        await session.commit()

    token = await _login(client, env["tenant_code"], env["admin_email"])
    resp = await client.get(
        f"/api/v1/dashboard/audit-logs?agent_id={agent_id}&limit=10",
        headers={"Authorization": f"Bearer {token}"},
    )

    assert resp.status_code == 200
    seqs = [i["sequence_number"] for i in resp.json()["items"]]
    assert seqs == sorted(seqs, reverse=True), f"not in chain order: {seqs}"
