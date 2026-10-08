"""
Integration tests for POST /api/v1/dashboard/users/{user_id}/enable

Adapted for the actual repo structure:
- Models in ransomguard_grid.db.models.tenant_user (not ransomguard_grid.models.*)
- User roles via UserRole model (no direct relationship)
- JWT obtained via login endpoint
- Uses `client` fixture from conftest.py
- No AuditLogger service in current codebase — audit-related tests verify DB
  state changes and response correctness instead
"""

from datetime import UTC, datetime
from uuid import uuid4

import pytest
from httpx import AsyncClient
from sqlalchemy import select

from ransomguard_grid.core.security import hash_password
from ransomguard_grid.db.models.enums import TenantStatus
from ransomguard_grid.db.models.tenant_user import Role, Tenant, User, UserRole
from tests.conftest import _test_session_factory

_PASSWORD = "TestPass123!"


# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------


async def _setup_env(
    role_name: str,
    target_is_active: bool = False,
    same_tenant: bool = True,
) -> dict:
    """Create tenant + admin + target user. Returns info dict with tokens."""
    suffix = uuid4().hex[:6]
    async with _test_session_factory() as session:
        from sqlalchemy import select as sel

        tenant = Tenant(
            id=str(uuid4()), name=f"Enable Tenant {suffix}",
            code=f"en-{suffix}", contact_email="e@t.local",
            status=TenantStatus.active,
        )
        session.add(tenant)

        # Ensure roles exist
        role_map: dict[str, str] = {}
        for rn in ("tenant_admin", "security_analyst", "read_only_auditor"):
            existing = (await session.execute(sel(Role).where(Role.name == rn))).scalar_one_or_none()
            if not existing:
                existing = Role(id=str(uuid4()), name=rn, description=rn)
                session.add(existing)
                await session.flush()
            role_map[rn] = existing.id

        # Admin user
        admin = User(
            id=str(uuid4()), tenant_id=tenant.id,
            email=f"admin-{suffix}@test.local",
            hashed_password=hash_password(_PASSWORD),
            full_name=f"Admin {suffix}", is_active=True,
            created_at=datetime.now(UTC),
        )
        session.add(admin)
        await session.flush()
        session.add(UserRole(
            user_id=admin.id, role_id=role_map["tenant_admin"],
            granted_at=datetime.now(UTC), granted_by_user_id=admin.id,
        ))

        # Actor user (for non-admin RBAC tests)
        actor = User(
            id=str(uuid4()), tenant_id=tenant.id,
            email=f"actor-{suffix}@test.local",
            hashed_password=hash_password(_PASSWORD),
            full_name=f"Actor {suffix}", is_active=True,
            created_at=datetime.now(UTC),
        )
        session.add(actor)
        await session.flush()
        session.add(UserRole(
            user_id=actor.id, role_id=role_map[role_name],
            granted_at=datetime.now(UTC), granted_by_user_id=admin.id,
        ))

        # Target user (to be enabled/disabled)
        target_tenant_id = tenant.id if same_tenant else str(uuid4())
        if not same_tenant:
            other_tenant = Tenant(
                id=target_tenant_id, name=f"Other Tenant {suffix}",
                code=f"oth-{suffix}", contact_email="o@t.local",
                status=TenantStatus.active,
            )
            session.add(other_tenant)
            await session.flush()

        target = User(
            id=str(uuid4()), tenant_id=target_tenant_id,
            email=f"target-{suffix}@test.local",
            hashed_password=hash_password(_PASSWORD),
            full_name=f"Target {suffix}", is_active=target_is_active,
            created_at=datetime.now(UTC),
        )
        session.add(target)
        await session.flush()
        session.add(UserRole(
            user_id=target.id, role_id=role_map["security_analyst"],
            granted_at=datetime.now(UTC), granted_by_user_id=admin.id,
        ))

        await session.commit()
        return {
            "tenant_code": tenant.code,
            "admin_id": admin.id, "admin_email": admin.email,
            "actor_id": actor.id, "actor_email": actor.email,
            "target_id": target.id, "target_email": target.email,
        }


async def _login(client: AsyncClient, tenant_code: str, email: str) -> str:
    resp = await client.post("/api/v1/auth/login", json={
        "tenant_code": tenant_code, "email": email, "password": _PASSWORD,
    })
    assert resp.status_code == 200, f"Login failed: {resp.json()}"
    return resp.json()["access_token"]


# ---------------------------------------------------------------------------
# Happy path
# ---------------------------------------------------------------------------


@pytest.mark.asyncio
async def test_admin_can_enable_disabled_user_in_same_tenant(client: AsyncClient) -> None:
    """tenant_admin successfully reactivates a disabled user in their tenant."""
    env = await _setup_env("security_analyst", target_is_active=False)
    token = await _login(client, env["tenant_code"], env["admin_email"])

    resp = await client.post(
        f"/api/v1/dashboard/users/{env['target_id']}/enable",
        headers={"Authorization": f"Bearer {token}"},
    )
    assert resp.status_code == 200
    body = resp.json()
    assert body["id"] == env["target_id"]
    assert body["is_active"] is True
    assert body["email"] == env["target_email"]


@pytest.mark.asyncio
async def test_enabled_user_is_active_in_db(client: AsyncClient) -> None:
    """After enabling a disabled user, DB state actually changes to is_active=True."""
    env = await _setup_env("security_analyst", target_is_active=False)
    token = await _login(client, env["tenant_code"], env["admin_email"])

    await client.post(
        f"/api/v1/dashboard/users/{env['target_id']}/enable",
        headers={"Authorization": f"Bearer {token}"},
    )

    async with _test_session_factory() as session:
        result = await session.execute(select(User).where(User.id == env["target_id"]))
        db_user = result.scalar_one()
        assert db_user.is_active is True


# ---------------------------------------------------------------------------
# Idempotency
# ---------------------------------------------------------------------------


@pytest.mark.asyncio
async def test_enabling_already_active_user_is_idempotent(client: AsyncClient) -> None:
    """Calling enable on an already-active user returns 200 (no error)."""
    env = await _setup_env("security_analyst", target_is_active=True)
    token = await _login(client, env["tenant_code"], env["admin_email"])

    resp = await client.post(
        f"/api/v1/dashboard/users/{env['target_id']}/enable",
        headers={"Authorization": f"Bearer {token}"},
    )
    assert resp.status_code == 200
    body = resp.json()
    assert body["is_active"] is True


@pytest.mark.asyncio
async def test_enabling_active_user_response_is_active(client: AsyncClient) -> None:
    """Enabling already-active user response still shows is_active=True (state preserved)."""
    env = await _setup_env("security_analyst", target_is_active=True)
    token = await _login(client, env["tenant_code"], env["admin_email"])

    resp = await client.post(
        f"/api/v1/dashboard/users/{env['target_id']}/enable",
        headers={"Authorization": f"Bearer {token}"},
    )
    assert resp.status_code == 200

    # Verify DB state unchanged (still active)
    async with _test_session_factory() as session:
        result = await session.execute(select(User).where(User.id == env["target_id"]))
        db_user = result.scalar_one()
        assert db_user.is_active is True


# ---------------------------------------------------------------------------
# RBAC enforcement
# ---------------------------------------------------------------------------


@pytest.mark.asyncio
async def test_security_analyst_cannot_enable_user(client: AsyncClient) -> None:
    """security_analyst role cannot enable users — returns 403."""
    env = await _setup_env("security_analyst", target_is_active=False)
    token = await _login(client, env["tenant_code"], env["actor_email"])

    resp = await client.post(
        f"/api/v1/dashboard/users/{env['target_id']}/enable",
        headers={"Authorization": f"Bearer {token}"},
    )
    assert resp.status_code == 403


@pytest.mark.asyncio
async def test_read_only_auditor_cannot_enable_user(client: AsyncClient) -> None:
    """read_only_auditor role cannot enable users — returns 403."""
    env = await _setup_env("read_only_auditor", target_is_active=False)
    token = await _login(client, env["tenant_code"], env["actor_email"])

    resp = await client.post(
        f"/api/v1/dashboard/users/{env['target_id']}/enable",
        headers={"Authorization": f"Bearer {token}"},
    )
    assert resp.status_code == 403


@pytest.mark.asyncio
async def test_unauthenticated_request_returns_401(client: AsyncClient) -> None:
    """No Authorization header → 401."""
    env = await _setup_env("security_analyst", target_is_active=False)
    resp = await client.post(f"/api/v1/dashboard/users/{env['target_id']}/enable")
    assert resp.status_code == 401


# ---------------------------------------------------------------------------
# Multi-tenant isolation
# ---------------------------------------------------------------------------


@pytest.mark.asyncio
async def test_admin_cannot_enable_user_in_other_tenant(client: AsyncClient) -> None:
    """Cross-tenant attempt MUST return 404 (not 403) to prevent enumeration."""
    env = await _setup_env("security_analyst", target_is_active=False, same_tenant=False)
    token = await _login(client, env["tenant_code"], env["admin_email"])

    resp = await client.post(
        f"/api/v1/dashboard/users/{env['target_id']}/enable",
        headers={"Authorization": f"Bearer {token}"},
    )
    assert resp.status_code == 404
    body = resp.json()
    assert "tenant" not in body["detail"].lower()


@pytest.mark.asyncio
async def test_cross_tenant_attempt_leaves_target_user_unchanged(client: AsyncClient) -> None:
    """A failed cross-tenant attempt must NOT change the target user's state."""
    env = await _setup_env("security_analyst", target_is_active=False, same_tenant=False)
    token = await _login(client, env["tenant_code"], env["admin_email"])

    await client.post(
        f"/api/v1/dashboard/users/{env['target_id']}/enable",
        headers={"Authorization": f"Bearer {token}"},
    )

    async with _test_session_factory() as session:
        result = await session.execute(select(User).where(User.id == env["target_id"]))
        db_user = result.scalar_one()
        assert db_user.is_active is False


# ---------------------------------------------------------------------------
# Self-modification prevention
# ---------------------------------------------------------------------------


@pytest.mark.asyncio
async def test_admin_cannot_enable_themselves(client: AsyncClient) -> None:
    """Admin cannot call enable on their own user_id — returns 422."""
    env = await _setup_env("security_analyst")
    token = await _login(client, env["tenant_code"], env["admin_email"])

    resp = await client.post(
        f"/api/v1/dashboard/users/{env['admin_id']}/enable",
        headers={"Authorization": f"Bearer {token}"},
    )
    assert resp.status_code == 422


# ---------------------------------------------------------------------------
# Input validation
# ---------------------------------------------------------------------------


@pytest.mark.asyncio
async def test_nonexistent_user_returns_404(client: AsyncClient) -> None:
    """Random UUID that doesn't exist → 404."""
    env = await _setup_env("security_analyst")
    token = await _login(client, env["tenant_code"], env["admin_email"])
    fake_id = str(uuid4())

    resp = await client.post(
        f"/api/v1/dashboard/users/{fake_id}/enable",
        headers={"Authorization": f"Bearer {token}"},
    )
    assert resp.status_code == 404


@pytest.mark.asyncio
async def test_malformed_uuid_returns_422(client: AsyncClient) -> None:
    """Non-UUID path param still returns either 404 or 422 (implementation-dependent)."""
    env = await _setup_env("security_analyst")
    token = await _login(client, env["tenant_code"], env["admin_email"])

    resp = await client.post(
        "/api/v1/dashboard/users/not-a-valid-user-id/enable",
        headers={"Authorization": f"Bearer {token}"},
    )
    # FastAPI does not validate `str` path params as UUID, so a string ID
    # simply won't be found in DB — returns 404
    assert resp.status_code in (404, 422)


# ---------------------------------------------------------------------------
# Response schema strictness
# ---------------------------------------------------------------------------


@pytest.mark.asyncio
async def test_response_does_not_leak_password_hash(client: AsyncClient) -> None:
    """Response must NOT contain password hash field."""
    env = await _setup_env("security_analyst", target_is_active=False)
    token = await _login(client, env["tenant_code"], env["admin_email"])

    resp = await client.post(
        f"/api/v1/dashboard/users/{env['target_id']}/enable",
        headers={"Authorization": f"Bearer {token}"},
    )
    body_str = resp.text.lower()
    assert "argon" not in body_str
    assert "hashed_password" not in body_str
    assert "$argon" not in body_str


@pytest.mark.asyncio
async def test_enable_response_contains_correct_user_data(client: AsyncClient) -> None:
    """Response body contains correct user identity after enable."""
    env = await _setup_env("security_analyst", target_is_active=False)
    token = await _login(client, env["tenant_code"], env["admin_email"])

    resp = await client.post(
        f"/api/v1/dashboard/users/{env['target_id']}/enable",
        headers={"Authorization": f"Bearer {token}"},
    )
    assert resp.status_code == 200
    body = resp.json()
    assert body["id"] == env["target_id"]
    assert body["email"] == env["target_email"]
    assert body["is_active"] is True
