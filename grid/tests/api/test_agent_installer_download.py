"""ADR-027, step 2: the GRID builds the double-click installer for one enrollment code.

POST /api/v1/dashboard/agents/installer -- a copy of the published stub with the agent package,
the configuration and the 56-byte trailer appended; the code is embedded, not consumed.
"""

import hashlib
import json
import os
from datetime import UTC, datetime, timedelta
from pathlib import Path
from uuid import uuid4

import pytest
from httpx import AsyncClient, Response
from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from ransomguard_grid.db.models.notifications import UserActionLog
from ransomguard_grid.services.agent_installer import PACKAGE_NAME, STUB_NAME, TRAILER, TRAILER_MAGIC
from tests.api.test_heartbeat import _setup
from tests.api.test_heartbeat_posture import _admin_headers

STUB = b"MZ stub of the installer " + bytes(range(256)) * 40
PACKAGE = b"PK\x03\x04" + os.urandom(20_000)
PACKAGE_SHA = hashlib.sha256(PACKAGE).hexdigest()
CA_PEM = "-----BEGIN CERTIFICATE-----\nMIIBszCCAVmgAwIBAgIUTEST\n-----END CERTIFICATE-----\n"
HOST = "grid.hopital.local"


def _publish(tmp_path: Path, monkeypatch: pytest.MonkeyPatch, *, stub: bool = True, ca: bool = True, sha_file: bool = True) -> None:
    if stub:
        (tmp_path / STUB_NAME).write_bytes(STUB)
    (tmp_path / PACKAGE_NAME).write_bytes(PACKAGE)
    if sha_file:
        (tmp_path / f"{PACKAGE_NAME}.sha256").write_text(f"{PACKAGE_SHA}  {PACKAGE_NAME}\n", encoding="ascii")
    if ca:
        (tmp_path / "ca.crt").write_text(CA_PEM, encoding="ascii")
    monkeypatch.setenv("GRID_AGENT_PACKAGES_DIR", str(tmp_path))
    monkeypatch.setenv("GRID_GRID_CA_CERT_PATH", str(tmp_path / "ca.crt"))


async def _provision(client: AsyncClient, headers: dict[str, str]) -> str:
    response = await client.post("/api/v1/dashboard/agents/provision", headers=headers)
    assert response.status_code == 200, response.text
    return str(response.json()["otp"])


async def _download(client: AsyncClient, headers: dict[str, str], otp: str, host: str = HOST) -> Response:
    return await client.post("/api/v1/dashboard/agents/installer", headers=headers, json={"otp": otp, "host": host})


def _parse(body: bytes) -> tuple[bytes, bytes, dict[str, object]]:
    magic, zip_length, config_length, digest = TRAILER.unpack(body[-TRAILER.size:])
    assert magic == TRAILER_MAGIC
    payload = body[-TRAILER.size - zip_length - config_length:-TRAILER.size]
    assert hashlib.sha256(payload).digest() == digest
    zip_part, config_part = payload[:zip_length], payload[zip_length:]
    return body[: len(body) - TRAILER.size - zip_length - config_length], zip_part, json.loads(config_part.decode("utf-8"))


@pytest.mark.asyncio
async def test_the_installer_is_the_stub_plus_the_package_the_configuration_and_the_trailer(
    client: AsyncClient, db_session: AsyncSession, test_cert_serial: str, tmp_path: Path, monkeypatch: pytest.MonkeyPatch,
) -> None:
    _publish(tmp_path, monkeypatch)
    tenant, _ = await _setup(db_session, test_cert_serial)
    tenant_id, tenant_code = tenant.id, tenant.code  # plain values: nothing lazy-loaded after the request
    headers = await _admin_headers(client, db_session, tenant)
    otp = await _provision(client, headers)
    before = datetime.now(UTC)

    response = await _download(client, headers, otp)

    assert response.status_code == 200, response.text
    assert response.headers["content-type"].startswith("application/vnd.microsoft.portable-executable")
    assert response.headers["content-disposition"] == f'attachment; filename="RansomGuard-CM-Installeur-{HOST}.exe"'
    assert int(response.headers["content-length"]) == len(response.content)
    assert response.headers["cache-control"] == "no-store"

    stub, package, config = _parse(response.content)
    assert stub == STUB
    assert package == PACKAGE
    # The agent channel (nginx agent.conf, :443), not the console's 8443, which answers 405.
    assert config["grid_base_url"] == f"https://{HOST}:443"
    assert config["otp"] == otp
    assert config["tenant_code"] == tenant_code
    assert config["agent_package_sha256"] == PACKAGE_SHA
    assert config["grid_ca_pem"] == CA_PEM
    expires = datetime.fromisoformat(str(config["otp_expires_at"]))
    # 5-minute enrollment code (enrollment_otp_validity_minutes default), +/- 1 min slack for clock skew.
    assert before + timedelta(minutes=4) <= expires <= before + timedelta(minutes=6)

    # Logged, without the code itself.
    db_session.expire_all()
    log = (await db_session.execute(select(UserActionLog).where(UserActionLog.action_type == "installer_downloaded"))).scalars().one()
    assert log.tenant_id == tenant_id
    assert log.details_json == {"otp_prefix": otp[:14], "host": HOST, "port": 443, "package_sha256": PACKAGE_SHA}
    assert otp not in json.dumps(log.details_json)


@pytest.mark.asyncio
async def test_the_code_is_embedded_not_consumed_a_second_download_still_works(
    client: AsyncClient, db_session: AsyncSession, test_cert_serial: str, tmp_path: Path, monkeypatch: pytest.MonkeyPatch,
) -> None:
    _publish(tmp_path, monkeypatch)
    tenant, _ = await _setup(db_session, test_cert_serial)
    headers = await _admin_headers(client, db_session, tenant)
    otp = await _provision(client, headers)

    assert (await _download(client, headers, otp)).status_code == 200
    assert (await _download(client, headers, otp, host="grid-bis.hopital.local")).status_code == 200


@pytest.mark.asyncio
async def test_an_unknown_code_or_another_tenants_code_is_refused_the_same_way(
    client: AsyncClient, db_session: AsyncSession, test_cert_serial: str, tmp_path: Path, monkeypatch: pytest.MonkeyPatch,
) -> None:
    _publish(tmp_path, monkeypatch)
    tenant_a, _ = await _setup(db_session, test_cert_serial)
    tenant_b, _ = await _setup(db_session, f"serial-b-{uuid4().hex[:8]}")
    headers_a = await _admin_headers(client, db_session, tenant_a)
    headers_b = await _admin_headers(client, db_session, tenant_b)
    otp_b = await _provision(client, headers_b)

    unknown = await _download(client, headers_a, "RG-CAM-2026-ZZZZZZ")
    foreign = await _download(client, headers_a, otp_b)

    assert unknown.status_code == 409
    assert foreign.status_code == 409
    assert unknown.json() == foreign.json()


@pytest.mark.asyncio
async def test_nothing_published_gives_404_stub_package_or_ca_missing(
    client: AsyncClient, db_session: AsyncSession, test_cert_serial: str, tmp_path: Path, monkeypatch: pytest.MonkeyPatch,
) -> None:
    tenant, _ = await _setup(db_session, test_cert_serial)
    headers = await _admin_headers(client, db_session, tenant)

    (tmp_path / "no-stub").mkdir(exist_ok=True)
    _publish(tmp_path / "no-stub", monkeypatch, stub=False)
    otp = await _provision(client, headers)
    assert (await _download(client, headers, otp)).status_code == 404

    (tmp_path / "no-ca").mkdir(exist_ok=True)
    _publish(tmp_path / "no-ca", monkeypatch, ca=False)
    assert (await _download(client, headers, otp)).status_code == 404


@pytest.mark.asyncio
async def test_the_package_sha256_is_computed_when_the_cd_wrote_no_sha_file(
    client: AsyncClient, db_session: AsyncSession, test_cert_serial: str, tmp_path: Path, monkeypatch: pytest.MonkeyPatch,
) -> None:
    _publish(tmp_path, monkeypatch, sha_file=False)
    tenant, _ = await _setup(db_session, test_cert_serial)
    headers = await _admin_headers(client, db_session, tenant)
    otp = await _provision(client, headers)

    _, _, config = _parse((await _download(client, headers, otp)).content)

    assert config["agent_package_sha256"] == PACKAGE_SHA


@pytest.mark.asyncio
async def test_only_an_authenticated_admin_with_a_valid_host_gets_an_installer(
    client: AsyncClient, db_session: AsyncSession, test_cert_serial: str, tmp_path: Path, monkeypatch: pytest.MonkeyPatch,
) -> None:
    _publish(tmp_path, monkeypatch)
    tenant, _ = await _setup(db_session, test_cert_serial)
    headers = await _admin_headers(client, db_session, tenant)
    otp = await _provision(client, headers)

    assert (await client.post("/api/v1/dashboard/agents/installer", json={"otp": otp, "host": HOST})).status_code == 401
    assert (await _download(client, headers, otp, host="grid hopital")).status_code == 422
    assert (await client.post("/api/v1/dashboard/agents/installer", headers=headers,
                              json={"otp": otp, "host": HOST, "extra": 1})).status_code == 422
