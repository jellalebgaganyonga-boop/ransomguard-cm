"""GRID-SEC-006: the console's install command checks the SHA-256 of the installer the GRID serves."""

import hashlib
from pathlib import Path

import pytest
from httpx import AsyncClient
from sqlalchemy.ext.asyncio import AsyncSession

from ransomguard_grid.services.agent_installer import installer_sha256
from tests.api.test_heartbeat import _setup
from tests.api.test_heartbeat_posture import _admin_headers

INSTALLER = b"#Requires -RunAsAdministrator\r\nWrite-Host 'RansomGuard-CM installer'\r\n"


def test_fingerprint_is_the_sha256_of_the_served_file_in_get_filehash_format(tmp_path: Path) -> None:
    (tmp_path / "install.ps1").write_bytes(INSTALLER)

    assert installer_sha256(str(tmp_path)) == hashlib.sha256(INSTALLER).hexdigest().upper()


def test_no_installer_published_gives_no_fingerprint(tmp_path: Path) -> None:
    assert installer_sha256(str(tmp_path)) is None


@pytest.mark.asyncio
async def test_provisioning_returns_the_fingerprint_of_the_served_installer(
    client: AsyncClient, db_session: AsyncSession, test_cert_serial: str, tmp_path: Path, monkeypatch: pytest.MonkeyPatch,
) -> None:
    (tmp_path / "install.ps1").write_bytes(INSTALLER)
    monkeypatch.setenv("GRID_AGENT_PACKAGES_DIR", str(tmp_path))
    tenant, _ = await _setup(db_session, test_cert_serial)
    headers = await _admin_headers(client, db_session, tenant)

    response = await client.post("/api/v1/dashboard/agents/provision", headers=headers)

    assert response.status_code == 200
    body = response.json()
    assert body["installer_sha256"] == hashlib.sha256(INSTALLER).hexdigest().upper()
    assert body["otp"].startswith("RG-CAM-")


@pytest.mark.asyncio
async def test_provisioning_without_a_published_installer_returns_null(
    client: AsyncClient, db_session: AsyncSession, test_cert_serial: str, tmp_path: Path, monkeypatch: pytest.MonkeyPatch,
) -> None:
    monkeypatch.setenv("GRID_AGENT_PACKAGES_DIR", str(tmp_path / "missing"))
    tenant, _ = await _setup(db_session, test_cert_serial)
    headers = await _admin_headers(client, db_session, tenant)

    response = await client.post("/api/v1/dashboard/agents/provision", headers=headers)

    assert response.status_code == 200
    assert response.json()["installer_sha256"] is None
