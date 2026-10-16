"""GRID-SEC-007: no usable default secret -- without its JWT key the GRID refuses to start."""

import os
import secrets
import subprocess
import sys
from pathlib import Path

import pytest

from ransomguard_grid.core.config import _PUBLISHED_JWT_SECRETS, ConfigurationError, get_settings

GRID = Path(__file__).resolve().parents[1]


def _start(tmp_path: Path, **overrides: str) -> subprocess.CompletedProcess[str]:
    """Import the application the way uvicorn does, in a directory without any .env file."""
    env = {k: v for k, v in os.environ.items() if not k.startswith("GRID_")}
    env.update({"GRID_DATABASE_URL": "sqlite+aiosqlite://", "PYTHONPATH": str(GRID / "src"), **overrides})
    return subprocess.run([sys.executable, "-c", "import ransomguard_grid.main"], cwd=tmp_path, env=env,
                          capture_output=True, text=True, timeout=120)


def test_without_the_variable_the_grid_refuses_to_start_and_names_it(tmp_path: Path) -> None:
    result = _start(tmp_path)

    assert result.returncode != 0
    assert "ConfigurationError" in result.stderr
    assert "GRID_JWT_SECRET_KEY: Field required" in result.stderr
    assert "secrets.token_urlsafe" in result.stderr  # says how to fix it


def test_with_its_own_key_the_same_start_succeeds(tmp_path: Path) -> None:
    result = _start(tmp_path, GRID_JWT_SECRET_KEY=secrets.token_urlsafe(48))

    assert result.returncode == 0, result.stderr


@pytest.mark.parametrize("published", sorted(_PUBLISHED_JWT_SECRETS))
def test_a_key_published_in_the_repository_is_refused(monkeypatch: pytest.MonkeyPatch, published: str) -> None:
    monkeypatch.setenv("GRID_JWT_SECRET_KEY", published)

    with pytest.raises(ConfigurationError, match="GRID_JWT_SECRET_KEY: Value error, this key is published"):
        get_settings()


def test_an_empty_key_is_refused(monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.setenv("GRID_JWT_SECRET_KEY", "")

    with pytest.raises(ConfigurationError, match="GRID_JWT_SECRET_KEY: String should have at least 32 characters"):
        get_settings()


def test_the_env_example_carries_no_usable_key() -> None:
    lines = (GRID / ".env.example").read_text(encoding="utf-8").splitlines()
    [value] = [line.split("=", 1)[1] for line in lines if line.startswith("GRID_JWT_SECRET_KEY=")]

    assert value == ""
