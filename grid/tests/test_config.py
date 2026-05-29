"""Tests for application configuration."""

import os

import pytest
from pydantic import ValidationError


def test_settings_load_from_env(monkeypatch: pytest.MonkeyPatch) -> None:
    """Settings should load values from GRID_ prefixed environment variables."""
    monkeypatch.setenv("GRID_LOG_LEVEL", "DEBUG")
    monkeypatch.setenv("GRID_JWT_SECRET_KEY", "a-very-long-secret-key-with-32-or-more-chars!")
    monkeypatch.setenv("GRID_DATABASE_URL", "sqlite+aiosqlite://")

    from ransomguard_grid.core.config import Settings

    s = Settings()  # type: ignore[call-arg]
    assert s.log_level == "DEBUG"


def test_settings_validation_rejects_invalid_log_level(monkeypatch: pytest.MonkeyPatch) -> None:
    """Settings should reject invalid log_level values."""
    monkeypatch.setenv("GRID_LOG_LEVEL", "INVALID_LEVEL")
    monkeypatch.setenv("GRID_JWT_SECRET_KEY", "a-very-long-secret-key-with-32-or-more-chars!")

    from ransomguard_grid.core.config import Settings

    with pytest.raises(ValidationError):
        Settings()  # type: ignore[call-arg]


def test_jwt_secret_minimum_length(monkeypatch: pytest.MonkeyPatch) -> None:
    """JWT secret must be at least 32 characters."""
    monkeypatch.setenv("GRID_JWT_SECRET_KEY", "short")
    monkeypatch.delenv("GRID_DATABASE_URL", raising=False)
    monkeypatch.delenv("GRID_DEBUG", raising=False)

    from ransomguard_grid.core.config import Settings

    with pytest.raises(ValidationError):
        Settings()  # type: ignore[call-arg]


def test_default_values_applied(monkeypatch: pytest.MonkeyPatch) -> None:
    """Default values should be applied when env vars are not set."""
    monkeypatch.setenv("GRID_JWT_SECRET_KEY", "a-very-long-secret-key-with-32-or-more-chars!")
    monkeypatch.delenv("GRID_DEBUG", raising=False)
    monkeypatch.delenv("GRID_DATABASE_URL", raising=False)

    from ransomguard_grid.core.config import Settings

    s = Settings()  # type: ignore[call-arg]
    assert s.environment == "development"
    assert s.debug is False
    assert s.rate_limit_per_minute == 120
    assert s.enrollment_otp_validity_minutes == 30
    assert s.default_tenant_id == "default"
