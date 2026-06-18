"""
Integration tests for GET /api/v1/dashboard/me

Adapted for the actual repo structure:
- Models in ransomguard_grid.db.models.tenant_user (not ransomguard_grid.models.*)
- User roles via UserRole model (no direct relationship)
- JWT obtained via login endpoint (no create_access_token helper)
- Uses `client` fixture from conftest.py (not `async_client`)
- TenantStatus enum used for tenant creation
"""

from datetime import UTC, datetime, timedelta
from uuid import uuid4

import pytest
from httpx import AsyncClient

from ransomguard_grid.core.security import hash_password
from ransomguard_grid.db.models.enums import TenantStatus
from ransomguard_grid.db.models.tenant_user import Role, Tenant, User, UserRole
from tests.conftest import _test_session_factory

_PASSWORD = "TestPass123!"


# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------


async def _seed_user(
    role_name: str,
    is_active: bool = True,
    tenant_status: TenantStatus = TenantStatus.active,
    last_login: datetime | None = None,
) -> dict:
    """Create tenant + user + role in DB. Returns dict with tenant_code, email, user_id, tenant_id."""
    suffix = uuid4().hex[:6]
    async with _test_session_factory() as session:
        from sqlalchemy import select

        tenant = Tenant(
            id=str(uuid4()), name=f"Test Tenant {suffix}",
            code=f"me-{suffix}", contact_email="test@test.local",
            status=tenant_status,
        )
        session.add(tenant)

        existing = (await session.execute(select(Role).where(Role.name == role_name))).scalar_one_or_none()
        if not existing:
            existing = Role(id=str(uuid4()), name=role_name, description=role_name)
            session.add(existing)
            await session.flush()

        user = User(
            id=str(uuid4()), tenant_id=tenant.id,
            email=f"user-{suffix}@test.local",
            hashed_password=hash_password(_PASSWORD),
            full_name=f"Test User {suffix}",
            is_active=is_active,
            created_at=datetime.now(UTC),
            last_login_at=last_login,
        )
        session.add(user)
        await session.flush()
        session.add(UserRole(
            user_id=user.id, role_id=existing.id,
            granted_at=datetime.now(UTC), granted_by_user_id=user.id,
        ))
        await session.commit()
        return {
            "tenant_code": tenant.code, "email": user.email,
            "user_id": user.id, "tenant_id": tenant.id,
            "tenant_name": tenant.name,
        }


async def _login(client: AsyncClient, tenant_code: str, email: str) -> str:
    resp = await client.post("/api/v1/auth/login", json={
        "tenant_code": tenant_code, "email": email, "password": _PASSWORD,
    })
    assert resp.status_code == 200, f"Login failed: {resp.json()}"
    return resp.json()["access_token"]


# ---------------------------------------------------------------------------
# Happy path — one test per role
# ---------------------------------------------------------------------------


@pytest.mark.asyncio
async def test_me_returns_admin_identity_correctly(client: AsyncClient) -> None:
    """tenant_admin user receives full identity with admin role."""
    info = await _seed_user("tenant_admin", last_login=datetime.now(UTC) - timedelta(hours=4))
    token = await _login(client, info["tenant_code"], info["email"])

    resp = await client.get("/api/v1/dashboard/me", headers={"Authorization": f"Bearer {token}"})
    assert resp.status_code == 200
    body = resp.json()
    assert body["user_id"] == info["user_id"]
    assert body["email"] == info["email"]
    assert body["is_active"] is True
    assert body["tenant"]["id"] == info["tenant_id"]
    assert body["tenant"]["name"] == info["tenant_name"]
    assert body["tenant"]["status"] == "active"
    assert "tenant_admin" in body["roles"]
    assert body["preferences"]["language"] == "fr"
    assert body["preferences"]["timezone"] == "Africa/Douala"


@pytest.mark.asyncio
async def test_me_returns_analyst_identity_correctly(client: AsyncClient) -> None:
    """security_analyst user receives full identity with analyst role."""
    info = await _seed_user("security_analyst", last_login=datetime.now(UTC) - timedelta(minutes=30))
    token = await _login(client, info["tenant_code"], info["email"])

    resp = await client.get("/api/v1/dashboard/me", headers={"Authorization": f"Bearer {token}"})
    assert resp.status_code == 200
    body = resp.json()
    assert "security_analyst" in body["roles"]
    assert body["last_login_at"] is not None


@pytest.mark.asyncio
async def test_me_returns_auditor_identity_correctly(client: AsyncClient) -> None:
    """read_only_auditor user receives full identity. last_login_at is None for first login."""
    info = await _seed_user("read_only_auditor", last_login=None)
    token = await _login(client, info["tenant_code"], info["email"])

    resp = await client.get("/api/v1/dashboard/me", headers={"Authorization": f"Bearer {token}"})
    assert resp.status_code == 200
    body = resp.json()
    assert "read_only_auditor" in body["roles"]
    # Note: last_login_at is set by the login call itself (auth.py updates it on each
    # successful login), so it will be non-None even for first-time login via this endpoint.
    assert "last_login_at" in body


# ---------------------------------------------------------------------------
# Authentication failures
# ---------------------------------------------------------------------------


@pytest.mark.asyncio
async def test_me_returns_401_without_token(client: AsyncClient) -> None:
    """Missing Authorization header returns 401."""
    resp = await client.get("/api/v1/dashboard/me")
    assert resp.status_code == 401


@pytest.mark.asyncio
async def test_me_returns_401_with_malformed_token(client: AsyncClient) -> None:
    """Malformed JWT returns 401."""
    resp = await client.get(
        "/api/v1/dashboard/me",
        headers={"Authorization": "Bearer not-a-real-jwt"},
    )
    assert resp.status_code == 401


@pytest.mark.asyncio
async def test_me_returns_401_with_token_referencing_deleted_user(client: AsyncClient) -> None:
    """JWT for a user that has been deleted since issuance returns 401."""
    info = await _seed_user("security_analyst")
    token = await _login(client, info["tenant_code"], info["email"])

    # Delete user after JWT issuance
    async with _test_session_factory() as session:
        from sqlalchemy import delete as sql_delete
        from ransomguard_grid.db.models.tenant_user import UserRole as UR
        await session.execute(sql_delete(UR).where(UR.user_id == info["user_id"]))
        await session.execute(
            sql_delete(User).where(User.id == info["user_id"])
        )
        await session.commit()

    resp = await client.get("/api/v1/dashboard/me", headers={"Authorization": f"Bearer {token}"})
    assert resp.status_code == 401


# ---------------------------------------------------------------------------
# State changes between JWT issuance and request
# ---------------------------------------------------------------------------


@pytest.mark.asyncio
async def test_me_returns_4xx_for_disabled_user(client: AsyncClient) -> None:
    """User disabled between JWT issuance and request is rejected.

    get_current_user (jwt_auth.py) checks is_active and returns 401 before
    the /me handler's own 403 check fires. Either way access is denied.
    """
    info = await _seed_user("security_analyst")
    token = await _login(client, info["tenant_code"], info["email"])

    # Disable user after JWT issuance
    async with _test_session_factory() as session:
        from sqlalchemy import update
        await session.execute(
            update(User).where(User.id == info["user_id"]).values(is_active=False)
        )
        await session.commit()

    resp = await client.get("/api/v1/dashboard/me", headers={"Authorization": f"Bearer {token}"})
    assert resp.status_code in (401, 403)


@pytest.mark.asyncio
async def test_me_returns_403_for_suspended_tenant(client: AsyncClient) -> None:
    """Tenant suspended between JWT issuance and request returns 403."""
    info = await _seed_user("security_analyst")
    token = await _login(client, info["tenant_code"], info["email"])

    # Suspend tenant after JWT issuance
    async with _test_session_factory() as session:
        from sqlalchemy import update
        await session.execute(
            update(Tenant).where(Tenant.id == info["tenant_id"]).values(status=TenantStatus.suspended)
        )
        await session.commit()

    resp = await client.get("/api/v1/dashboard/me", headers={"Authorization": f"Bearer {token}"})
    assert resp.status_code == 403


# ---------------------------------------------------------------------------
# Multi-tenant isolation
# ---------------------------------------------------------------------------


@pytest.mark.asyncio
async def test_me_returns_correct_tenant_for_user(client: AsyncClient) -> None:
    """User's tenant in response matches their actual tenant, not another tenant."""
    info_a = await _seed_user("security_analyst")
    info_b = await _seed_user("security_analyst")  # Different tenant
    token = await _login(client, info_a["tenant_code"], info_a["email"])

    resp = await client.get("/api/v1/dashboard/me", headers={"Authorization": f"Bearer {token}"})
    assert resp.status_code == 200
    body = resp.json()
    assert body["tenant"]["id"] == info_a["tenant_id"]
    assert body["tenant"]["id"] != info_b["tenant_id"]


# ---------------------------------------------------------------------------
# Response schema strictness
# ---------------------------------------------------------------------------


@pytest.mark.asyncio
async def test_me_response_has_no_unexpected_fields(client: AsyncClient) -> None:
    """Response must contain exactly the documented fields, no leakage."""
    info = await _seed_user("tenant_admin")
    token = await _login(client, info["tenant_code"], info["email"])

    resp = await client.get("/api/v1/dashboard/me", headers={"Authorization": f"Bearer {token}"})
    assert resp.status_code == 200
    body = resp.json()
    expected_top_level = {"user_id", "email", "full_name", "is_active", "tenant", "roles", "last_login_at", "preferences"}
    assert set(body.keys()) == expected_top_level
    assert set(body["tenant"].keys()) == {"id", "name", "status"}
    assert set(body["preferences"].keys()) == {"language", "timezone"}


@pytest.mark.asyncio
async def test_me_does_not_leak_password_hash(client: AsyncClient) -> None:
    """Critical: password hash MUST NOT appear in any form in response."""
    info = await _seed_user("tenant_admin")
    token = await _login(client, info["tenant_code"], info["email"])

    resp = await client.get("/api/v1/dashboard/me", headers={"Authorization": f"Bearer {token}"})
    body_str = resp.text.lower()
    assert "argon2" not in body_str
    assert "$argon" not in body_str
    assert "hashed_password" not in body_str
    assert "password" not in body_str


@pytest.mark.asyncio
async def test_me_user_id_matches_logged_in_user(client: AsyncClient) -> None:
    """user_id in response matches the authenticated user's actual ID."""
    info = await _seed_user("tenant_admin")
    token = await _login(client, info["tenant_code"], info["email"])

    resp = await client.get("/api/v1/dashboard/me", headers={"Authorization": f"Bearer {token}"})
    assert resp.status_code == 200
    body = resp.json()
    assert body["user_id"] == info["user_id"]
