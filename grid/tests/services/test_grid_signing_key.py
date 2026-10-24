"""GRID-KEY-001: the threat-intel signing key is never generated silently outside development."""

from pathlib import Path
from typing import Any

import pytest
from cryptography.hazmat.primitives.asymmetric.ed25519 import Ed25519PrivateKey, Ed25519PublicKey
from cryptography.hazmat.primitives.serialization import (
    Encoding,
    NoEncryption,
    PrivateFormat,
    PublicFormat,
    load_pem_public_key,
)
from fastapi import FastAPI

from ransomguard_grid.core.config import ConfigurationError
from ransomguard_grid.main import lifespan
from ransomguard_grid.services import grid_signing_key_service
from ransomguard_grid.services.grid_signing_key_service import GridSigningKeyService


class _RecordingLogger:
    """Stands in for the module's structlog logger (cached on first use, so not capturable)."""

    def __init__(self) -> None:
        self.records: list[tuple[str, str, dict[str, Any]]] = []

    def info(self, event: str, **kw: Any) -> None:
        self.records.append(("info", event, kw))

    def warning(self, event: str, **kw: Any) -> None:
        self.records.append(("warning", event, kw))


@pytest.fixture
def log(monkeypatch: pytest.MonkeyPatch) -> _RecordingLogger:
    recorder = _RecordingLogger()
    monkeypatch.setattr(grid_signing_key_service, "logger", recorder)
    return recorder


def _existing_key(path: Path) -> Ed25519PrivateKey:
    key = Ed25519PrivateKey.generate()
    path.write_bytes(key.private_bytes(Encoding.PEM, PrivateFormat.PKCS8, NoEncryption()))
    return key


def _raw(public_key: Ed25519PublicKey) -> bytes:
    return public_key.public_bytes(Encoding.Raw, PublicFormat.Raw)


@pytest.mark.asyncio
async def test_production_without_a_key_refuses_to_start_and_names_the_path(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch,
) -> None:
    missing = tmp_path / "pki" / "threat-intel-ed25519.key"
    monkeypatch.setenv("GRID_ENVIRONMENT", "production")
    monkeypatch.setenv("GRID_AGENT_AUTH_MODE", "mtls")  # production + agent_id is refused before the key is looked at
    monkeypatch.setenv("GRID_THREAT_INTEL_SIGNING_KEY_PATH", str(missing))

    with pytest.raises(ConfigurationError) as refused:
        async with lifespan(FastAPI()):
            pass  # pragma: no cover -- startup must not get here

    assert str(missing) in str(refused.value)
    assert "init-signing-key.sh" in str(refused.value)
    assert not missing.exists() and not missing.parent.exists()  # nothing generated


def test_production_with_its_key_starts_and_signs_with_that_key(tmp_path: Path, log: _RecordingLogger) -> None:
    path = tmp_path / "threat-intel-ed25519.key"
    key = _existing_key(path)
    before = path.read_bytes()

    service = GridSigningKeyService(path)
    service.initialize(allow_generate=False)

    key.public_key().verify(service.sign(b"package"), b"package")
    assert path.read_bytes() == before  # loaded, not regenerated
    assert [level for level, _, _ in log.records] == ["info"]


def test_development_without_a_key_generates_one_and_warns(tmp_path: Path, log: _RecordingLogger) -> None:
    path = tmp_path / "dev-pki" / "threat-intel-ed25519.key"

    service = GridSigningKeyService(path)
    service.initialize(allow_generate=True)

    assert path.exists()
    public = load_pem_public_key(Path(f"{path}.pub").read_bytes())
    assert isinstance(public, Ed25519PublicKey)
    public.verify(service.sign(b"package"), b"package")
    [(level, event, fields)] = log.records
    assert level == "warning"
    assert "development only" in event
    assert fields["path"] == str(path)


def test_a_key_generated_once_is_the_one_loaded_afterwards(tmp_path: Path, log: _RecordingLogger) -> None:
    path = tmp_path / "threat-intel-ed25519.key"
    first = GridSigningKeyService(path)
    first.initialize(allow_generate=True)

    again = GridSigningKeyService(path)
    again.initialize(allow_generate=False)

    assert again.get_public_key_pem() == first.get_public_key_pem()
