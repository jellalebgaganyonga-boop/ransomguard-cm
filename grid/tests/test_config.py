"""Tests for application configuration."""


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
    monkeypatch.delenv("GRID_REDIS_URL", raising=False)
    monkeypatch.delenv("GRID_LOG_LEVEL", raising=False)

    from ransomguard_grid.core.config import Settings

    s = Settings(_env_file=None)  # type: ignore[call-arg]
    assert s.environment == "development"
    assert s.debug is False
    assert s.rate_limit_per_minute == 120
    assert s.enrollment_otp_validity_minutes == 30
    assert s.default_tenant_id == "default"


# ── Agent authentication mode vs environment (plan de finition, J3 § 3.3, reduced by the owner) ──
#
# "agent_id" identifies an agent by the UUID in its URL: whoever learns it speaks for the
# endpoint. It is tolerable on a development GRID and nowhere else; staging and production take
# mTLS only (the chain is not delivered yet, ADR to come). The GRID refuses to start rather than
# run a production-labelled server on a guessable identity.

_SECRET = "a-very-long-secret-key-with-32-or-more-chars!"


def _env(monkeypatch: pytest.MonkeyPatch, environment: str, mode: str) -> None:
    monkeypatch.setenv("GRID_JWT_SECRET_KEY", _SECRET)
    monkeypatch.setenv("GRID_ENVIRONMENT", environment)
    monkeypatch.setenv("GRID_AGENT_AUTH_MODE", mode)


@pytest.mark.parametrize("environment", ["staging", "production"])
def test_agent_id_mode_is_refused_outside_development(monkeypatch: pytest.MonkeyPatch, environment: str) -> None:
    _env(monkeypatch, environment, "agent_id")

    from ransomguard_grid.core.config import Settings

    with pytest.raises(ValidationError) as refused:
        Settings(_env_file=None)  # type: ignore[call-arg]
    assert "development" in str(refused.value)


def test_agent_id_mode_is_accepted_in_development(monkeypatch: pytest.MonkeyPatch) -> None:
    _env(monkeypatch, "development", "agent_id")

    from ransomguard_grid.core.config import Settings

    assert Settings(_env_file=None).agent_auth_mode == "agent_id"  # type: ignore[call-arg]


@pytest.mark.parametrize("environment", ["development", "staging", "production"])
def test_mtls_mode_is_accepted_everywhere(monkeypatch: pytest.MonkeyPatch, environment: str) -> None:
    _env(monkeypatch, environment, "mtls")

    from ransomguard_grid.core.config import Settings

    assert Settings(_env_file=None).agent_auth_mode == "mtls"  # type: ignore[call-arg]


def test_the_refusal_names_both_variables_for_whoever_deploys(monkeypatch: pytest.MonkeyPatch) -> None:
    _env(monkeypatch, "production", "agent_id")

    from ransomguard_grid.core.config import ConfigurationError, get_settings

    with pytest.raises(ConfigurationError) as refused:
        get_settings()
    message = str(refused.value)
    assert "GRID_AGENT_AUTH_MODE" in message and "GRID_ENVIRONMENT" in message
    assert "GRID_?" not in message  # a model-level error has no field: it must still read well
