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
    """Tenant admin cannot remove their own admin role (anti-brick)."""
    env = await _setup_rbac_env()
    token = await _login(client, env["tenant_code"], env["admin_email"])

    resp = await client.put(
        f"/api/v1/dashboard/users/{env['admin_id']}/roles",
        json={"role_names": ["security_analyst"]},
        headers={"Authorization": f"Bearer {token}"},
    )
    assert resp.status_code == 400
    assert "yourself" in resp.json()["detail"].lower()
