"""GRID-SEC-004, GRID-SEC-005, GRID-ENR-001 on TWO REAL WORKERS.

Production runs ``uvicorn --workers 2``. The defects came from state kept in one process's
memory, so the only honest proof is two separate processes: two uvicorn servers, one shared
Redis, one shared database. Two app instances in the same Python process would share module
globals and could not have caught the original bug.

Requires a real Redis: GRID_TEST_REDIS_URL (the CI grid job provides a Redis service). Without
it the module is skipped locally, and fails in CI so the proof can never silently disappear.
"""

import asyncio
import os
import socket
import subprocess
import sys
import time
from collections.abc import AsyncGenerator, Iterator
from dataclasses import dataclass
from pathlib import Path
from uuid import uuid4

import httpx
import pytest
from redis import Redis
from sqlalchemy.ext.asyncio import create_async_engine

import ransomguard_grid.db.models  # noqa: F401  (registers every table)
from ransomguard_grid.core.security import hash_password
from ransomguard_grid.db.base import Base
from ransomguard_grid.db.models.enums import TenantStatus
from ransomguard_grid.db.models.tenant_user import Role, Tenant, User, UserRole

REDIS_URL = os.environ.get("GRID_TEST_REDIS_URL")
if not REDIS_URL:
    if os.environ.get("CI"):
        pytest.fail("GRID_TEST_REDIS_URL is required in CI: the multi-worker session proof needs a real Redis")
    pytest.skip("GRID_TEST_REDIS_URL not set: no real Redis for the multi-worker tests", allow_module_level=True)

PASSWORD = "MultiWorker-Pass-123!"
XHR = {"X-Requested-With": "XMLHttpRequest"}
SECRET = "multi-worker-test-secret-at-least-32-characters"


@dataclass
class Deployment:
    a: str  # base URL of worker A
    b: str  # base URL of worker B
    tenant_code: str
    email: str
    workdir: Path


def _free_port() -> int:
    with socket.socket() as s:
        s.bind(("127.0.0.1", 0))
        return int(s.getsockname()[1])


async def _seed(db_url: str, tenant_code: str, email: str) -> None:
    engine = create_async_engine(db_url)
    async with engine.begin() as conn:
        await conn.run_sync(Base.metadata.create_all)
    from sqlalchemy.ext.asyncio import async_sessionmaker

    async with async_sessionmaker(engine)() as session:
        tenant = Tenant(id=str(uuid4()), name="Hospital MW", code=tenant_code, contact_email="mw@test.local",
                        status=TenantStatus.active)
        role = Role(id=str(uuid4()), name="tenant_admin", description="Admin")
        user = User(id=str(uuid4()), tenant_id=tenant.id, email=email, hashed_password=hash_password(PASSWORD),
                    full_name="Admin MW", is_active=True)
        session.add_all([tenant, role, user])
        await session.flush()
        session.add(UserRole(user_id=user.id, role_id=role.id, granted_by_user_id=user.id))
        await session.commit()
    await engine.dispose()


def _start_worker(port: int, env: dict[str, str], log: Path) -> subprocess.Popen[bytes]:
    return subprocess.Popen(
        [sys.executable, "-m", "uvicorn", "ransomguard_grid.main:app", "--host", "127.0.0.1", "--port", str(port)],
        env=env, stdout=log.open("wb"), stderr=subprocess.STDOUT,
    )


def _wait_healthy(base: str, proc: subprocess.Popen[bytes], log: Path) -> None:
    deadline = time.monotonic() + 60
    while time.monotonic() < deadline:
        if proc.poll() is not None:
            raise RuntimeError(f"worker exited: {log.read_text(errors='replace')[-2000:]}")
        try:
            if httpx.get(f"{base}/api/v1/health", timeout=1).status_code == 200:
                return
        except httpx.HTTPError:
            pass
        time.sleep(0.3)
    raise RuntimeError(f"worker not healthy: {log.read_text(errors='replace')[-2000:]}")


def _env(workdir: Path, db_url: str, redis_url: str) -> dict[str, str]:
    env = {k: v for k, v in os.environ.items() if not k.startswith("GRID_")}
    env.update({
        "GRID_DATABASE_URL": db_url,
        "GRID_REDIS_URL": redis_url,
        "GRID_JWT_SECRET_KEY": SECRET,
        "GRID_ENVIRONMENT": "development",
        "GRID_AGENT_AUTH_MODE": "agent_id",
        "GRID_THREAT_INTEL_SIGNING_KEY_PATH": str(workdir / "ti.key"),
        "GRID_THREAT_INTEL_PACKAGES_DIR": str(workdir / "ti"),
        "GRID_AGENT_PACKAGES_DIR": str(workdir / "packages"),
    })
    return env


@pytest.fixture(scope="module")
def deployment(tmp_path_factory: pytest.TempPathFactory) -> Iterator[Deployment]:
    assert REDIS_URL
    Redis.from_url(REDIS_URL).flushdb()
    workdir = tmp_path_factory.mktemp("multiworker")
    db_url = f"sqlite+aiosqlite:///{(workdir / 'grid.db').as_posix()}"
    tenant_code, email = f"mw-{uuid4().hex[:6]}", f"admin-{uuid4().hex[:6]}@mw.local"
    asyncio.run(_seed(db_url, tenant_code, email))

    env = _env(workdir, db_url, REDIS_URL)
    ports = (_free_port(), _free_port())
    procs = [_start_worker(p, env, workdir / f"worker-{p}.log") for p in ports]
    try:
        bases = [f"http://127.0.0.1:{p}" for p in ports]
        for base, proc, port in zip(bases, procs, ports, strict=True):
            _wait_healthy(base, proc, workdir / f"worker-{port}.log")
        yield Deployment(a=bases[0], b=bases[1], tenant_code=tenant_code, email=email, workdir=workdir)
    finally:
        for proc in procs:
            proc.terminate()
            proc.wait(timeout=20)


@pytest.fixture
async def http() -> AsyncGenerator[httpx.AsyncClient, None]:
    async with httpx.AsyncClient(timeout=15) as client:
        yield client


async def _login(http: httpx.AsyncClient, base: str, d: Deployment) -> tuple[str, str]:
    """(access token, refresh cookie value). The cookie is Secure: carried by hand over http."""
    response = await http.post(f"{base}/api/v1/auth/login",
                               json={"tenant_code": d.tenant_code, "email": d.email, "password": PASSWORD})
    assert response.status_code == 200, response.text
    return response.json()["access_token"], response.cookies["rg_refresh_token"]


async def _me(http: httpx.AsyncClient, base: str, token: str) -> int:
    return (await http.get(f"{base}/api/v1/dashboard/me", headers={"Authorization": f"Bearer {token}"})).status_code


async def _refresh(http: httpx.AsyncClient, base: str, cookie: str) -> httpx.Response:
    return await http.post(f"{base}/api/v1/auth/refresh", headers={**XHR, "Cookie": f"rg_refresh_token={cookie}"})


@pytest.mark.asyncio
async def test_a_token_revoked_on_a_is_refused_by_b(deployment: Deployment, http: httpx.AsyncClient) -> None:
    token, _ = await _login(http, deployment.a, deployment)
    assert await _me(http, deployment.b, token) == 200

    logout = await http.post(f"{deployment.a}/api/v1/auth/logout", headers={"Authorization": f"Bearer {token}"})
    assert logout.status_code == 200

    assert await _me(http, deployment.b, token) == 401


@pytest.mark.asyncio
async def test_a_replayed_refresh_token_revokes_its_family_across_workers(
    deployment: Deployment, http: httpx.AsyncClient,
) -> None:
    _, first_cookie = await _login(http, deployment.a, deployment)

    rotated = await _refresh(http, deployment.b, first_cookie)
    assert rotated.status_code == 200
    new_token = rotated.json()["access_token"]
    assert await _me(http, deployment.a, new_token) == 200

    replay = await _refresh(http, deployment.a, first_cookie)  # the stolen copy, on the other worker
    assert replay.status_code == 401

    assert await _me(http, deployment.b, new_token) == 401


@pytest.mark.asyncio
async def test_logout_all_on_a_invalidates_a_second_session_used_on_b(
    deployment: Deployment, http: httpx.AsyncClient,
) -> None:
    first, _ = await _login(http, deployment.a, deployment)
    second, second_cookie = await _login(http, deployment.b, deployment)
    assert await _me(http, deployment.b, second) == 200

    done = await http.post(f"{deployment.a}/api/v1/auth/logout-all", headers={"Authorization": f"Bearer {first}"})
    assert done.status_code == 200

    assert await _me(http, deployment.b, second) == 401
    assert (await _refresh(http, deployment.b, second_cookie)).status_code == 401


@pytest.mark.asyncio
async def test_an_otp_created_on_a_is_accepted_by_b_once(deployment: Deployment, http: httpx.AsyncClient) -> None:
    token, _ = await _login(http, deployment.a, deployment)
    provision = await http.post(f"{deployment.a}/api/v1/dashboard/agents/provision",
                                headers={"Authorization": f"Bearer {token}"})
    assert provision.status_code == 200
    otp = provision.json()["otp"]

    def enroll(fingerprint: str) -> dict[str, str]:
        return {"otp": otp, "hostname": "mw-pc", "fqdn": "mw-pc.local", "os_version": "10",
                "agent_version": "0.4.0", "hardware_fingerprint": fingerprint}

    first = await http.post(f"{deployment.b}/api/v1/agents/enroll", json=enroll("e" * 64))
    assert first.status_code == 200, first.text

    again = await http.post(f"{deployment.a}/api/v1/agents/enroll", json=enroll("f" * 64))
    assert again.status_code == 401


def test_a_worker_whose_redis_is_unreachable_answers_503_never_200(
    deployment: Deployment,
) -> None:
    workdir = deployment.workdir
    db_url = f"sqlite+aiosqlite:///{(workdir / 'grid.db').as_posix()}"
    port = _free_port()
    log = workdir / "worker-no-redis.log"
    proc = _start_worker(port, _env(workdir, db_url, "redis://127.0.0.1:1/0"), log)
    try:
        base = f"http://127.0.0.1:{port}"
        _wait_healthy(base, proc, log)  # the server itself is up

        login = httpx.post(f"{base}/api/v1/auth/login", timeout=15,
                           json={"tenant_code": deployment.tenant_code, "email": deployment.email, "password": PASSWORD})
        assert login.status_code == 503

        # A token issued by a healthy worker is not accepted either.
        token = httpx.post(f"{deployment.a}/api/v1/auth/login", timeout=15,
                           json={"tenant_code": deployment.tenant_code, "email": deployment.email,
                                 "password": PASSWORD}).json()["access_token"]
        me = httpx.get(f"{base}/api/v1/dashboard/me", headers={"Authorization": f"Bearer {token}"}, timeout=15)
        assert me.status_code == 503
    finally:
        proc.terminate()
        proc.wait(timeout=20)
