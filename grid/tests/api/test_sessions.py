"""Sessions in Redis (GRID-SEC-004, GRID-SEC-005, GRID-ENR-001), in-process with fakeredis.

The properties two workers must share are proven on two real uvicorn processes in
tests/integration/test_sessions_multiworker.py. These tests cover each rule on one instance.
"""

import asyncio
from collections.abc import AsyncGenerator

import pytest
from httpx import AsyncClient
from redis.asyncio import Redis
from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from ransomguard_grid.core.session_store import SessionStore, get_session_store
from ransomguard_grid.db.models.notifications import UserActionLog
from ransomguard_grid.db.models.operations import SystemLog
from ransomguard_grid.main import app
from ransomguard_grid.services.enrollment_otp_service import EnrollmentOtpService
from tests.api.test_tenant_isolation import _PASSWORD, _create_two_tenants

XHR = {"X-Requested-With": "XMLHttpRequest"}


async def _login(client: AsyncClient) -> tuple[str, dict[str, str]]:
    """Log tenant A's admin in; returns (access token, the refresh cookie as a header)."""
    setup = await _create_two_tenants()
    response = await client.post("/api/v1/auth/login", json={
        "tenant_code": setup["tenant_a_code"], "email": setup["user_a_email"], "password": _PASSWORD,
    })
    assert response.status_code == 200
    return response.json()["access_token"], {"rg_refresh_token": response.cookies["rg_refresh_token"]}


def _bearer(token: str) -> dict[str, str]:
    return {"Authorization": f"Bearer {token}"}


async def _me(client: AsyncClient, token: str) -> int:
    return (await client.get("/api/v1/dashboard/me", headers=_bearer(token))).status_code


async def _refresh(client: AsyncClient, cookie: dict[str, str]) -> tuple[int, dict[str, str] | None, str | None]:
    client.cookies.clear()
    response = await client.post("/api/v1/auth/refresh", headers=XHR, cookies=cookie)
    if response.status_code != 200:
        return response.status_code, None, None
    return 200, {"rg_refresh_token": response.cookies["rg_refresh_token"]}, response.json()["access_token"]


# ── Logout ──

@pytest.mark.asyncio
async def test_logout_revokes_the_access_token_and_every_refresh_token_of_the_session(client: AsyncClient) -> None:
    token, cookie = await _login(client)
    assert await _me(client, token) == 200

    assert (await client.post("/api/v1/auth/logout", headers=_bearer(token))).status_code == 200

    assert await _me(client, token) == 401
    status, _, _ = await _refresh(client, cookie)  # a copy of the cookie, kept by a thief
    assert status == 401


# ── Refresh rotation and replay ──

@pytest.mark.asyncio
async def test_each_refresh_token_is_used_once_and_a_replay_revokes_its_whole_family(
    client: AsyncClient, db_session: AsyncSession,
) -> None:
    _, first_cookie = await _login(client)

    status, second_cookie, new_token = await _refresh(client, first_cookie)
    assert status == 200 and second_cookie and new_token
    assert await _me(client, new_token) == 200

    # The first cookie comes back: it was already used -- a theft.
    status, _, _ = await _refresh(client, first_cookie)
    assert status == 401

    # The whole family is revoked: the legitimate session's new tokens too.
    assert await _me(client, new_token) == 401
    status, _, _ = await _refresh(client, second_cookie)
    assert status == 401

    # And the event is logged.
    actions = (await db_session.execute(
        select(UserActionLog).where(UserActionLog.action_type == "refresh_token_reuse")
    )).scalars().all()
    warnings = (await db_session.execute(select(SystemLog).where(SystemLog.component == "auth"))).scalars().all()
    assert len(actions) == 1
    assert len(warnings) == 1


@pytest.mark.asyncio
async def test_refresh_reads_the_cookie_only_the_body_path_is_closed(client: AsyncClient) -> None:
    _, cookie = await _login(client)
    client.cookies.clear()

    response = await client.post(
        "/api/v1/auth/refresh", headers=XHR, json={"refresh_token": cookie["rg_refresh_token"]},
    )

    assert response.status_code == 401


@pytest.mark.asyncio
async def test_refresh_without_x_requested_with_is_refused(client: AsyncClient) -> None:
    _, cookie = await _login(client)
    client.cookies.clear()

    response = await client.post("/api/v1/auth/refresh", cookies=cookie)

    assert response.status_code == 403


# ── Log out every session (US1.2.2) ──

@pytest.mark.asyncio
async def test_logout_all_invalidates_every_session_of_the_user(client: AsyncClient) -> None:
    setup = await _create_two_tenants()
    credentials = {"tenant_code": setup["tenant_a_code"], "email": setup["user_a_email"], "password": _PASSWORD}
    first = await client.post("/api/v1/auth/login", json=credentials)
    second = await client.post("/api/v1/auth/login", json=credentials)
    first_token, second_token = first.json()["access_token"], second.json()["access_token"]
    second_cookie = {"rg_refresh_token": second.cookies["rg_refresh_token"]}

    assert (await client.post("/api/v1/auth/logout-all", headers=_bearer(first_token))).status_code == 200

    assert await _me(client, first_token) == 401
    assert await _me(client, second_token) == 401
    status, _, _ = await _refresh(client, second_cookie)
    assert status == 401

    # A new login afterwards works.
    third = await client.post("/api/v1/auth/login", json=credentials)
    assert await _me(client, third.json()["access_token"]) == 200


# ── Redis unreachable: 503, never 200 ──

@pytest.fixture
async def unreachable_store() -> AsyncGenerator[None, None]:
    redis = Redis(host="127.0.0.1", port=1, socket_connect_timeout=0.5, socket_timeout=0.5)
    app.dependency_overrides[get_session_store] = lambda: SessionStore(redis)
    yield
    await redis.aclose()


@pytest.mark.asyncio
async def test_login_answers_503_when_the_session_store_is_unreachable(client: AsyncClient, unreachable_store: None) -> None:
    setup = await _create_two_tenants()
    response = await client.post("/api/v1/auth/login", json={
        "tenant_code": setup["tenant_a_code"], "email": setup["user_a_email"], "password": _PASSWORD,
    })

    assert response.status_code == 503


@pytest.mark.asyncio
async def test_a_valid_token_is_refused_503_never_accepted_when_the_store_is_unreachable(
    client: AsyncClient, session_store: SessionStore,
) -> None:
    token, _ = await _login(client)
    assert await _me(client, token) == 200

    redis = Redis(host="127.0.0.1", port=1, socket_connect_timeout=0.5, socket_timeout=0.5)
    app.dependency_overrides[get_session_store] = lambda: SessionStore(redis)
    try:
        assert await _me(client, token) == 503
    finally:
        app.dependency_overrides[get_session_store] = lambda: session_store
        await redis.aclose()


# ── Enrollment OTPs ──

@pytest.mark.asyncio
async def test_an_otp_is_consumed_once_even_under_concurrent_use(session_store: SessionStore) -> None:
    service = EnrollmentOtpService(session_store)
    otp = await service.generate("tenant-x", validity_minutes=5)

    results = await asyncio.gather(*(service.validate_and_consume(otp) for _ in range(10)))

    assert results.count("tenant-x") == 1
    assert results.count(None) == 9
