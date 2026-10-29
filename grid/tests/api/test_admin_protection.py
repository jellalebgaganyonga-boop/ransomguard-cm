"""GRID-SEC-002: the backend refuses self-disable, and never leaves a tenant without an active admin."""

from datetime import UTC, datetime
from uuid import uuid4

import pytest
from httpx import AsyncClient
from sqlalchemy import select

from ransomguard_grid.api.v1.routes.dashboard import _is_last_active_admin
from ransomguard_grid.core.security import hash_password
from ransomguard_grid.db.models.tenant_user import Role, User, UserRole
from tests.api.test_tenant_isolation import _PASSWORD, _create_two_tenants, _login
from tests.conftest import _test_session_factory


async def _add_admin(tenant_id: str, active: bool = True) -> tuple[str, str]:
    """A second tenant_admin in the tenant; returns (user id, email)."""
    async with _test_session_factory() as session:
        role = (await session.execute(select(Role).where(Role.name == "tenant_admin"))).scalar_one()
        user = User(id=str(uuid4()), tenant_id=tenant_id, email=f"admin2-{uuid4().hex[:6]}@test.local",
                    hashed_password=hash_password(_PASSWORD), full_name="Admin 2", is_active=active,
                    created_at=datetime.now(UTC))
        session.add(user)
        await session.flush()
        session.add(UserRole(user_id=user.id, role_id=role.id, granted_at=datetime.now(UTC), granted_by_user_id=user.id))
        await session.commit()
        return user.id, user.email


async def _ensure_role(name: str) -> None:
    async with _test_session_factory() as session:
        if (await session.execute(select(Role).where(Role.name == name))).scalar_one_or_none() is None:
            session.add(Role(id=str(uuid4()), name=name, description=name))
            await session.commit()


async def _admin_id(email: str) -> str:
    async with _test_session_factory() as session:
        return (await session.execute(select(User.id).where(User.email == email))).scalar_one()


@pytest.mark.asyncio
async def test_an_admin_cannot_disable_their_own_account(client: AsyncClient) -> None:
    setup = await _create_two_tenants()
    headers = {"Authorization": f"Bearer {await _login(client, setup['tenant_a_code'], setup['user_a_email'])}"}
    own_id = await _admin_id(setup["user_a_email"])

    response = await client.post(f"/api/v1/dashboard/users/{own_id}/disable", headers=headers)

    assert response.status_code == 403


@pytest.mark.asyncio
async def test_the_last_active_admin_cannot_be_demoted(client: AsyncClient) -> None:
    setup = await _create_two_tenants()
    headers = {"Authorization": f"Bearer {await _login(client, setup['tenant_a_code'], setup['user_a_email'])}"}
    own_id = await _admin_id(setup["user_a_email"])

    response = await client.put(f"/api/v1/dashboard/users/{own_id}/roles", headers=headers,
                                json={"role_names": ["security_analyst"]})

    assert response.status_code == 409


@pytest.mark.asyncio
async def test_with_two_active_admins_one_can_demote_then_disable_the_other(client: AsyncClient) -> None:
    setup = await _create_two_tenants()
    await _ensure_role("security_analyst")
    other_id, _ = await _add_admin(setup["tenant_a_id"])
    headers = {"Authorization": f"Bearer {await _login(client, setup['tenant_a_code'], setup['user_a_email'])}"}

    demoted = await client.put(f"/api/v1/dashboard/users/{other_id}/roles", headers=headers,
                               json={"role_names": ["security_analyst"]})
    assert demoted.status_code == 200

    disabled = await client.post(f"/api/v1/dashboard/users/{other_id}/disable", headers=headers)
    assert disabled.status_code == 200


@pytest.mark.asyncio
async def test_self_demotion_with_another_admin_present_is_still_refused(client: AsyncClient) -> None:
    setup = await _create_two_tenants()
    await _ensure_role("security_analyst")
    await _add_admin(setup["tenant_a_id"])
    headers = {"Authorization": f"Bearer {await _login(client, setup['tenant_a_code'], setup['user_a_email'])}"}
    own_id = await _admin_id(setup["user_a_email"])

    response = await client.put(f"/api/v1/dashboard/users/{own_id}/roles", headers=headers,
                                json={"role_names": ["security_analyst"]})

    assert response.status_code == 400  # not the last admin, but still not on oneself


@pytest.mark.asyncio
async def test_last_active_admin_detection_ignores_inactive_admins() -> None:
    setup = await _create_two_tenants()
    own_id = await _admin_id(setup["user_a_email"])
    async with _test_session_factory() as session:
        assert await _is_last_active_admin(session, setup["tenant_a_id"], own_id) is True

    await _add_admin(setup["tenant_a_id"], active=False)
    async with _test_session_factory() as session:
        assert await _is_last_active_admin(session, setup["tenant_a_id"], own_id) is True

    await _add_admin(setup["tenant_a_id"], active=True)
    async with _test_session_factory() as session:
        assert await _is_last_active_admin(session, setup["tenant_a_id"], own_id) is False


@pytest.mark.asyncio
async def test_a_user_who_is_not_an_admin_is_never_the_last_admin() -> None:
    setup = await _create_two_tenants()
    async with _test_session_factory() as session:
        assert await _is_last_active_admin(session, setup["tenant_a_id"], str(uuid4())) is False
