"""A role change reaches a signed-in user on their very next request.

The question the owner asked: if an administrator changes somebody's role while that person is
signed in, when does it take effect? The answer has to be "immediately", and it has to be
demonstrable, because the opposite answer -- "when they next sign in" -- means a demotion is
advisory until the person decides to co-operate.

It is immediate because the token carries no roles. ``require_roles`` reads them from the
database on every request (api/v1/dependencies/jwt_auth.py), so the token the person is already
holding stops opening the door the moment the row changes. Nothing is revoked, nothing is
refreshed, and there is nothing for them to hold on to.

The console is the other half and cannot be the authority: it reads ``/api/v1/dashboard/me`` to decide
which menus to draw, and until this change it kept that answer for five minutes. The menus were
only ever a convenience -- every action behind them is checked here -- but a menu that lies for
five minutes is still a menu that lies, so the console now re-reads it every minute and after any
role change it makes. See dashboard/src/hooks/use-me.ts.
"""

from datetime import UTC, datetime
from uuid import uuid4

import pytest
from httpx import AsyncClient
from sqlalchemy import select

from ransomguard_grid.core.security import hash_password
from ransomguard_grid.db.models.agent import Agent
from ransomguard_grid.db.models.alerts import Alert
from ransomguard_grid.db.models.enums import AgentStatus, AlertStatus, Severity, TenantStatus
from ransomguard_grid.db.models.tenant_user import Role, Tenant, User, UserRole
from tests.conftest import _test_session_factory

_PASSWORD = "TestPass123!"
_ROLES = ("tenant_admin", "security_analyst", "read_only_auditor")


async def _bench_tenant() -> dict[str, str]:
    """A tenant with an administrator, an analyst, and an alert for the analyst to work on."""
    suffix = uuid4().hex[:6]
    async with _test_session_factory() as session:
        tenant = Tenant(
            id=str(uuid4()), name="Hopital de Yaounde", code=f"yde-{suffix}",
            contact_email=f"admin-{suffix}@test.local", status=TenantStatus.active,
        )
        session.add(tenant)

        roles: dict[str, Role] = {}
        for name in _ROLES:
            role = (await session.execute(
                select(Role).where(Role.name == name))).scalar_one_or_none()
            if role is None:
                role = Role(id=str(uuid4()), name=name, description=name)
                session.add(role)
                await session.flush()
            roles[name] = role

        admin = User(
            id=str(uuid4()), tenant_id=tenant.id, email=f"admin-{suffix}@test.local",
            hashed_password=hash_password(_PASSWORD), full_name="Administrator",
            is_active=True, created_at=datetime.now(UTC),
        )
        analyst = User(
            id=str(uuid4()), tenant_id=tenant.id, email=f"analyst-{suffix}@test.local",
            hashed_password=hash_password(_PASSWORD), full_name="Bench Analyst",
            is_active=True, created_at=datetime.now(UTC),
        )
        session.add_all([admin, analyst])
        await session.flush()
        session.add_all([
            UserRole(user_id=admin.id, role_id=roles["tenant_admin"].id,
                     granted_at=datetime.now(UTC), granted_by_user_id=admin.id),
            UserRole(user_id=analyst.id, role_id=roles["security_analyst"].id,
                     granted_at=datetime.now(UTC), granted_by_user_id=admin.id),
        ])

        agent = Agent(
            id=str(uuid4()), tenant_id=tenant.id, hostname="pc-lab", fqdn="pc-lab.local",
            os_version="10", agent_version="1.0", hardware_fingerprint=f"fp-{suffix}",
            status=AgentStatus.active,
        )
        session.add(agent)
        alert = Alert(
            id=str(uuid4()), tenant_id=tenant.id, agent_id=agent.id,
            client_message_id=f"msg-{suffix}", alert_type="USB", severity=Severity.High,
            status=AlertStatus.New, detected_at=datetime.now(UTC), summary="USB finding",
        )
        session.add(alert)

        await session.commit()
        return {
            "tenant_code": tenant.code, "admin_email": admin.email,
            "analyst_email": analyst.email, "analyst_id": analyst.id,
            "alert_id": alert.id,
        }


async def _login(client: AsyncClient, tenant_code: str, email: str) -> str:
    resp = await client.post("/api/v1/auth/login", json={
        "tenant_code": tenant_code, "email": email, "password": _PASSWORD,
    })
    assert resp.status_code == 200, f"login failed: {resp.json()}"
    return resp.json()["access_token"]


def _triage(alert_id: str) -> tuple[str, dict[str, str]]:
    """New -> Investigating: the first thing an analyst does with an alert."""
    return f"/api/v1/dashboard/alerts/{alert_id}/status", {
        "new_status": "Investigating", "justification": "looked at it on the bench",
    }


@pytest.mark.asyncio
async def test_a_demotion_takes_effect_on_the_next_request(client: AsyncClient) -> None:
    """analyst -> auditor, with the analyst's existing token: the next request is refused."""
    bench = await _bench_tenant()
    analyst_token = await _login(client, bench["tenant_code"], bench["analyst_email"])
    admin_token = await _login(client, bench["tenant_code"], bench["admin_email"])
    analyst_auth = {"Authorization": f"Bearer {analyst_token}"}
    admin_auth = {"Authorization": f"Bearer {admin_token}"}

    url, payload = _triage(bench["alert_id"])
    allowed = await client.post(url, json=payload, headers=analyst_auth)
    assert allowed.status_code == 200, "an analyst triages alerts"

    demote = await client.put(
        f"/api/v1/dashboard/users/{bench['analyst_id']}/roles",
        json={"role_names": ["read_only_auditor"]}, headers=admin_auth,
    )
    assert demote.status_code == 200
    assert demote.json()["roles"] == ["read_only_auditor"]

    # The same token, not a new one: nothing was signed out and nothing was refreshed.
    refused = await client.post(url, json=payload, headers=analyst_auth)
    assert refused.status_code == 403, "the demotion must not wait for the next sign-in"

    # Still signed in, and still able to read: demoted, not locked out.
    still_reading = await client.get("/api/v1/dashboard/alerts", headers=analyst_auth)
    assert still_reading.status_code == 200
    me = await client.get("/api/v1/dashboard/me", headers=analyst_auth)
    assert me.status_code == 200
    assert me.json()["roles"] == ["read_only_auditor"], (
        "the console reads its menus from here, so it must say the new role"
    )


@pytest.mark.asyncio
async def test_a_promotion_takes_effect_on_the_next_request(client: AsyncClient) -> None:
    """The other direction, which is the one a demo needs: auditor -> analyst, no sign-out."""
    bench = await _bench_tenant()
    analyst_token = await _login(client, bench["tenant_code"], bench["analyst_email"])
    admin_token = await _login(client, bench["tenant_code"], bench["admin_email"])
    analyst_auth = {"Authorization": f"Bearer {analyst_token}"}
    admin_auth = {"Authorization": f"Bearer {admin_token}"}

    await client.put(
        f"/api/v1/dashboard/users/{bench['analyst_id']}/roles",
        json={"role_names": ["read_only_auditor"]}, headers=admin_auth,
    )
    url, payload = _triage(bench["alert_id"])
    assert (await client.post(url, json=payload, headers=analyst_auth)).status_code == 403

    await client.put(
        f"/api/v1/dashboard/users/{bench['analyst_id']}/roles",
        json={"role_names": ["security_analyst"]}, headers=admin_auth,
    )
    assert (await client.post(url, json=payload, headers=analyst_auth)).status_code == 200


@pytest.mark.asyncio
async def test_the_last_administrator_cannot_be_demoted(client: AsyncClient) -> None:
    """A hospital with no administrator can no longer be administered -- by anyone, ever."""
    bench = await _bench_tenant()
    admin_token = await _login(client, bench["tenant_code"], bench["admin_email"])
    admin_auth = {"Authorization": f"Bearer {admin_token}"}

    me = (await client.get("/api/v1/dashboard/me", headers=admin_auth)).json()
    refused = await client.put(
        f"/api/v1/dashboard/users/{me['user_id']}/roles",
        json={"role_names": ["security_analyst"]}, headers=admin_auth,
    )
    assert refused.status_code in (400, 409)
    assert "admin" in refused.json()["detail"].lower()

    # And the refusal changed nothing: still an administrator.
    after = (await client.get("/api/v1/dashboard/me", headers=admin_auth)).json()
    assert "tenant_admin" in after["roles"]


@pytest.mark.asyncio
async def test_a_role_change_leaves_a_trace(client: AsyncClient) -> None:
    """Who changed whose role, from what to what. A privilege change is audited or it is nothing."""
    bench = await _bench_tenant()
    admin_token = await _login(client, bench["tenant_code"], bench["admin_email"])
    admin_auth = {"Authorization": f"Bearer {admin_token}"}

    await client.put(
        f"/api/v1/dashboard/users/{bench['analyst_id']}/roles",
        json={"role_names": ["read_only_auditor"]}, headers=admin_auth,
    )

    audit = (await client.get(
        "/api/v1/dashboard/user-actions?action_type=user_roles_change",
        headers=admin_auth)).json()
    entry = next(e for e in audit["items"] if e["target_id"] == bench["analyst_id"])
    assert entry["details_json"]["before"] == ["security_analyst"]
    assert entry["details_json"]["after"] == ["read_only_auditor"]
    assert entry["actor_email"] == bench["admin_email"]
