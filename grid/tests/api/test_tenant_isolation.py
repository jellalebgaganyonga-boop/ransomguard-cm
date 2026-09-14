"""CRITICAL: Multi-tenant isolation tests — cross-tenant leak prevention (CWE-285)."""

from datetime import UTC, datetime
from uuid import uuid4

import pytest
from httpx import AsyncClient
from jose import jwt  # type: ignore[import-untyped]

from ransomguard_grid.core.security import hash_password
from ransomguard_grid.db.models.agent import Agent
from ransomguard_grid.db.models.alerts import Alert
from ransomguard_grid.db.models.enums import AgentStatus, AlertStatus, Severity, TenantStatus
from ransomguard_grid.db.models.tenant_user import Role, Tenant, User, UserRole
from tests.conftest import _test_session_factory

_PASSWORD = "TestPass123!"


async def _create_two_tenants() -> dict[str, str]:
    """Create two tenants with admin user, agent, and alert each."""
    suffix = uuid4().hex[:6]
    result: dict[str, str] = {}

    async with _test_session_factory() as session:
        for label in ("a", "b"):
            tenant = Tenant(id=str(uuid4()), name=f"Hospital {label.upper()}", code=f"hosp-{label}-{suffix}", contact_email=f"{label}@test.local", status=TenantStatus.active)
            session.add(tenant)

            # Ensure role exists (idempotent)
            from sqlalchemy import select

            from ransomguard_grid.db.models.tenant_user import Role as RoleModel
            existing_role = (await session.execute(select(RoleModel).where(RoleModel.name == "tenant_admin"))).scalar_one_or_none()
            if not existing_role:
                existing_role = Role(id=str(uuid4()), name="tenant_admin", description="Admin")
                session.add(existing_role)
                await session.flush()

            user = User(id=str(uuid4()), tenant_id=tenant.id, email=f"admin-{label}@test.local", hashed_password=hash_password(_PASSWORD), full_name=f"Admin {label.upper()}", is_active=True, created_at=datetime.now(UTC))
            session.add(user)

            agent = Agent(id=str(uuid4()), tenant_id=tenant.id, hostname=f"pc-{label}", fqdn=f"pc-{label}.local", os_version="10", agent_version="1.0", hardware_fingerprint=f"fp-{label}-{suffix}", status=AgentStatus.active)
            session.add(agent)

            alert = Alert(id=str(uuid4()), tenant_id=tenant.id, agent_id=agent.id, client_message_id=f"msg-{label}-{suffix}", alert_type="USB", severity=Severity.High, status=AlertStatus.New, detected_at=datetime.now(UTC), summary=f"Alert from tenant {label}")
            session.add(alert)

            await session.flush()
            session.add(UserRole(user_id=user.id, role_id=existing_role.id, granted_at=datetime.now(UTC), granted_by_user_id=user.id))

            result[f"tenant_{label}_code"] = tenant.code
            result[f"tenant_{label}_id"] = tenant.id
            result[f"user_{label}_email"] = user.email
            result[f"agent_{label}_id"] = agent.id
            result[f"alert_{label}_id"] = alert.id

        await session.commit()
    return result


async def _login(client: AsyncClient, tenant_code: str, email: str) -> str:
    """Login and return access token."""
    resp = await client.post("/api/v1/auth/login", json={
        "tenant_code": tenant_code, "email": email, "password": _PASSWORD,
    })
    assert resp.status_code == 200, f"Login failed: {resp.json()}"
    return resp.json()["access_token"]


@pytest.mark.asyncio
async def test_user_a_cannot_list_tenant_b_alerts(client: AsyncClient) -> None:
    """User A lists alerts, sees only tenant A alerts."""
    setup = await _create_two_tenants()
    token_a = await _login(client, setup["tenant_a_code"], setup["user_a_email"])

    resp = await client.get("/api/v1/dashboard/alerts", headers={"Authorization": f"Bearer {token_a}"})
    assert resp.status_code == 200
    ids = [a["id"] for a in resp.json()["items"]]
    assert setup["alert_a_id"] in ids
    assert setup["alert_b_id"] not in ids


@pytest.mark.asyncio
async def test_user_a_gets_404_for_tenant_b_alert(client: AsyncClient) -> None:
    """Direct ID access to other tenant's alert returns 404, not 403."""
    setup = await _create_two_tenants()
    token_a = await _login(client, setup["tenant_a_code"], setup["user_a_email"])

    resp = await client.get(f"/api/v1/dashboard/alerts/{setup['alert_b_id']}", headers={"Authorization": f"Bearer {token_a}"})
    assert resp.status_code == 404


@pytest.mark.asyncio
async def test_user_a_cannot_list_tenant_b_agents(client: AsyncClient) -> None:
    """User A lists agents, sees only tenant A agents."""
    setup = await _create_two_tenants()
    token_a = await _login(client, setup["tenant_a_code"], setup["user_a_email"])

    resp = await client.get("/api/v1/dashboard/agents", headers={"Authorization": f"Bearer {token_a}"})
    assert resp.status_code == 200
    ids = [a["id"] for a in resp.json()["items"]]
    assert setup["agent_a_id"] in ids
    assert setup["agent_b_id"] not in ids


@pytest.mark.asyncio
async def test_user_a_gets_404_for_tenant_b_agent(client: AsyncClient) -> None:
    """Direct ID access to other tenant's agent returns 404."""
    setup = await _create_two_tenants()
    token_a = await _login(client, setup["tenant_a_code"], setup["user_a_email"])

    resp = await client.get(f"/api/v1/dashboard/agents/{setup['agent_b_id']}", headers={"Authorization": f"Bearer {token_a}"})
    assert resp.status_code == 404


@pytest.mark.asyncio
async def test_jwt_tampering_rejected(client: AsyncClient) -> None:
    """Modifying JWT payload invalidates signature."""
    setup = await _create_two_tenants()
    token_a = await _login(client, setup["tenant_a_code"], setup["user_a_email"])

    payload = jwt.decode(token_a, "dummy", options={"verify_signature": False}, algorithms=["HS256"])
    payload["tenant_id"] = setup["tenant_b_id"]
    tampered = jwt.encode(payload, "wrong_secret", algorithm="HS256")

    resp = await client.get("/api/v1/dashboard/alerts", headers={"Authorization": f"Bearer {tampered}"})
    assert resp.status_code == 401


@pytest.mark.asyncio
async def test_admin_role_does_not_bypass_tenant(client: AsyncClient) -> None:
    """tenant_admin in A cannot read tenant B data."""
    setup = await _create_two_tenants()
    token_a = await _login(client, setup["tenant_a_code"], setup["user_a_email"])

    resp = await client.get(f"/api/v1/dashboard/agents/{setup['agent_b_id']}", headers={"Authorization": f"Bearer {token_a}"})
    assert resp.status_code == 404


@pytest.mark.asyncio
async def test_query_param_cannot_escape_tenant(client: AsyncClient) -> None:
    """Malicious tenant_id query param is ignored — server uses JWT."""
    setup = await _create_two_tenants()
    token_a = await _login(client, setup["tenant_a_code"], setup["user_a_email"])

    resp = await client.get(
        f"/api/v1/dashboard/alerts?tenant_id={setup['tenant_b_id']}",
        headers={"Authorization": f"Bearer {token_a}"},
    )
    assert resp.status_code == 200
    for alert in resp.json()["items"]:
        assert alert["id"] != setup["alert_b_id"]


@pytest.mark.asyncio
async def test_cross_tenant_command_blocked(client: AsyncClient) -> None:
    """Admin A cannot issue command to agent B (404)."""
    setup = await _create_two_tenants()
    token_a = await _login(client, setup["tenant_a_code"], setup["user_a_email"])

    resp = await client.post(
        "/api/v1/dashboard/commands",
        json={"agent_id": setup["agent_b_id"], "command_type": "restart"},
        headers={"Authorization": f"Bearer {token_a}"},
    )
    assert resp.status_code == 404


@pytest.mark.asyncio
async def test_cross_tenant_user_list_isolated(client: AsyncClient) -> None:
    """Admin A listing users sees only tenant A users."""
    setup = await _create_two_tenants()
    token_a = await _login(client, setup["tenant_a_code"], setup["user_a_email"])
    token_b = await _login(client, setup["tenant_b_code"], setup["user_b_email"])

    resp_a = await client.get("/api/v1/dashboard/users", headers={"Authorization": f"Bearer {token_a}"})
    resp_b = await client.get("/api/v1/dashboard/users", headers={"Authorization": f"Bearer {token_b}"})

    emails_a = {u["email"] for u in resp_a.json()["items"]}
    emails_b = {u["email"] for u in resp_b.json()["items"]}

    assert setup["user_a_email"] in emails_a
    assert setup["user_b_email"] not in emails_a
    assert setup["user_b_email"] in emails_b
    assert setup["user_a_email"] not in emails_b


@pytest.mark.asyncio
async def test_cross_tenant_metrics_isolated(client: AsyncClient) -> None:
    """Metrics summary only counts tenant's own data."""
    setup = await _create_two_tenants()
    token_a = await _login(client, setup["tenant_a_code"], setup["user_a_email"])

    resp = await client.get("/api/v1/dashboard/metrics/summary", headers={"Authorization": f"Bearer {token_a}"})
    assert resp.status_code == 200
    data = resp.json()
    assert data["total_agents"] == 1  # Only tenant A's agent
    assert data["alerts_24h"] == 1  # Only tenant A's alert
