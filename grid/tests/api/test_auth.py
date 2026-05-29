"""Tests for JWT authentication endpoints."""

from datetime import UTC, datetime
from uuid import uuid4

import pytest
from httpx import AsyncClient

from ransomguard_grid.core.security import hash_password
from ransomguard_grid.db.models.enums import TenantStatus
from ransomguard_grid.db.models.tenant_user import Role, Tenant, User, UserRole
from tests.conftest import _test_session_factory


async def _seed_user(tenant_code: str = "auth-t", email: str = "user@test.local", password: str = "TestPass123!") -> tuple[str, str, str]:
    """Create tenant + user + role, return (tenant_code, email, user_id)."""
    async with _test_session_factory() as session:
        tenant = Tenant(id=str(uuid4()), name="Auth Tenant", code=tenant_code, contact_email="a@a.local", status=TenantStatus.active)
        session.add(tenant)
        role = Role(id=str(uuid4()), name="tenant_admin", description="Admin")
        session.add(role)
        user = User(id=str(uuid4()), tenant_id=tenant.id, email=email, hashed_password=hash_password(password), full_name="Test User", is_active=True, created_at=datetime.now(UTC))
        session.add(user)
        await session.flush()
        session.add(UserRole(user_id=user.id, role_id=role.id, granted_at=datetime.now(UTC), granted_by_user_id=user.id))
        await session.commit()
        return tenant_code, email, user.id


@pytest.mark.asyncio
async def test_login_returns_tokens(client: AsyncClient) -> None:
    """Valid credentials return access + refresh tokens."""
    code, email, _ = await _seed_user(f"login-{uuid4().hex[:6]}")

    resp = await client.post("/api/v1/auth/login", json={
        "tenant_code": code, "email": email, "password": "TestPass123!",
    })
    assert resp.status_code == 200
    data = resp.json()
    assert "access_token" in data
    assert "refresh_token" in data
    assert data["token_type"] == "bearer"
    assert "tenant_admin" in data["roles"]


@pytest.mark.asyncio
async def test_login_invalid_credentials_returns_401(client: AsyncClient) -> None:
    """Wrong password returns 401."""
    code, email, _ = await _seed_user(f"bad-{uuid4().hex[:6]}")

    resp = await client.post("/api/v1/auth/login", json={
        "tenant_code": code, "email": email, "password": "WrongPassword",
    })
    assert resp.status_code == 401


@pytest.mark.asyncio
async def test_login_nonexistent_tenant_returns_401(client: AsyncClient) -> None:
    """Unknown tenant code returns 401 (not 404, prevent enumeration)."""
    resp = await client.post("/api/v1/auth/login", json={
        "tenant_code": "nonexistent", "email": "x@x.local", "password": "x",
    })
    assert resp.status_code == 401


@pytest.mark.asyncio
async def test_logout_blacklists_token(client: AsyncClient) -> None:
    """After logout, token should be rejected."""
    code, email, _ = await _seed_user(f"logout-{uuid4().hex[:6]}")

    login_resp = await client.post("/api/v1/auth/login", json={
        "tenant_code": code, "email": email, "password": "TestPass123!",
    })
    token = login_resp.json()["access_token"]

    # Logout
    await client.post("/api/v1/auth/logout", headers={"Authorization": f"Bearer {token}"})

    # Subsequent request should fail
    resp = await client.get("/api/v1/dashboard/alerts", headers={"Authorization": f"Bearer {token}"})
    assert resp.status_code == 401


@pytest.mark.asyncio
async def test_refresh_returns_new_access_token(client: AsyncClient) -> None:
    """Refresh token produces new access token."""
    code, email, _ = await _seed_user(f"refresh-{uuid4().hex[:6]}")

    login_resp = await client.post("/api/v1/auth/login", json={
        "tenant_code": code, "email": email, "password": "TestPass123!",
    })
    refresh_token = login_resp.json()["refresh_token"]

    resp = await client.post("/api/v1/auth/refresh", json={"refresh_token": refresh_token})
    assert resp.status_code == 200
    assert "access_token" in resp.json()
