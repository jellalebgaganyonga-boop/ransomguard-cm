"""Application configuration via Pydantic Settings with environment variable support."""

from pathlib import Path
from typing import Literal

from pydantic import Field, ValidationError, field_validator
from pydantic_settings import BaseSettings, SettingsConfigDict

# JWT keys that have been published in this repository (old default, .env.example placeholder,
# old test key). A GRID started with one of them issues tokens anyone can forge (GRID-SEC-007).
_PUBLISHED_JWT_SECRETS = frozenset({
    "dev-secret-key-minimum-32-characters-long!",
    "replace_with_strong_secret_minimum_32_chars",
    "test-secret-key-minimum-32-characters-long!",
})
_GENERATE_JWT_SECRET = 'python -c "import secrets; print(secrets.token_urlsafe(48))"'


class ConfigurationError(RuntimeError):
    """The GRID refuses to start: the message names the GRID_ variables to fix."""


class Settings(BaseSettings):
    """GRID server configuration. All values can be overridden via GRID_ prefixed env vars."""

    model_config = SettingsConfigDict(
        env_file=".env",
        env_prefix="GRID_",
        case_sensitive=False,
        extra="forbid",
    )

    # General
    environment: str = Field(default="development", pattern="^(development|staging|production)$")
    debug: bool = False
    log_level: str = Field(default="INFO", pattern="^(DEBUG|INFO|WARNING|ERROR|CRITICAL)$")

    # Database (default SQLite for dev/testing, MySQL for production)
    database_url: str = Field(default="sqlite+aiosqlite:///./grid.db")
    database_pool_size: int = Field(default=20, ge=5, le=100)
    database_max_overflow: int = Field(default=10, ge=0, le=50)

    # Redis (optional, for rate limiting and cache)
    redis_url: str = Field(default="redis://localhost:6379/0")

    # JWT (dashboard). No default: a default is a key everyone knows (GRID-SEC-007).
    jwt_secret_key: str = Field(min_length=32)
    # HS256 only: the algorithm is fixed in code (core/jwt_service.py). Kept so an
    # existing GRID_JWT_ALGORITHM setting fails at startup unless it says HS256.
    jwt_algorithm: Literal["HS256"] = "HS256"
    jwt_access_token_expire_minutes: int = Field(default=60, ge=5, le=480)

    # Ed25519 (threat intel signing)
    threat_intel_signing_key_path: Path = Field(default=Path("pki/threat-intel-ed25519.key"))
    threat_intel_packages_dir: Path = Field(default=Path("threat-intel-packages"))

    # Threat intel
    threat_intel_update_interval_hours: int = Field(default=24, ge=1, le=168)
    alienvault_otx_api_key: str | None = None

    # Multi-tenant
    default_tenant_id: str = "default"

    # Rate limiting
    rate_limit_per_minute: int = Field(default=120, ge=1)

    # Agent enrollment
    enrollment_otp_validity_minutes: int = Field(default=30, ge=5, le=1440)

    # Email notifications (Mailjet SMTP relay)
    smtp_host: str = Field(default="in-v3.mailjet.com")
    smtp_port: int = Field(default=587, ge=1, le=65535)
    smtp_username: str | None = None  # Mailjet API key
    smtp_password: str | None = None  # Mailjet API secret
    smtp_from_email: str = Field(default="noreply@ransomguard.local")
    smtp_from_name: str = Field(default="RansomGuard-CM")
    smtp_enabled: bool = False  # Set True when credentials are configured

    # Agent auto-disconnect timeout (minutes since last heartbeat)
    agent_disconnect_timeout_minutes: int = Field(default=5, ge=1, le=60)

    # Module transitions (agent_posture_changes) are kept this long, then purged once a day by
    # the disconnect worker: enough to answer "EXFIL WATCH was inactive from 2 pm to 4 pm".
    agent_posture_history_retention_days: int = Field(default=90, ge=1, le=3650)

    # Repeats of the same alert type for the same recipient are collapsed inside
    # this window. A ransomware run fires dozens of identical alerts in seconds;
    # one email each would bury the recipient and exhaust the relay quota.
    # 0 disables collapsing.
    notification_throttle_minutes: int = Field(default=10, ge=0, le=1440)

    # How agents prove who they are on /agents/* endpoints.
    #   "mtls"     -- nginx verifies a client certificate and forwards it in
    #                 X-Client-Cert. The target posture (Sprint 10).
    #   "agent_id" -- the agent is identified by the id in its URL. Weak: anyone
    #                 who learns the UUID can impersonate the endpoint, so it is
    #                 only defensible on a closed network (the tailnet).
    #
    # This used to be inferred from `environment != "development"`, which meant
    # naming a deployment "production" silently switched on an mTLS requirement
    # whose plumbing is not built yet -- and every agent heartbeat started
    # returning 401. A security control has to be chosen explicitly, not fall
    # out of an environment label.
    agent_auth_mode: str = Field(default="mtls", pattern="^(mtls|agent_id)$")

    # The directory nginx serves at /agent/ (install.ps1, the agent package), mounted read-only
    # into grid-api too: the console shows the SHA-256 of the very installer that is served, and
    # the install command refuses to run a script whose hash differs (GRID-SEC-006).
    agent_packages_dir: str = Field(default="/srv/agent-packages")

    @field_validator("jwt_secret_key")
    @classmethod
    def _not_a_published_key(cls, value: str) -> str:
        if value in _PUBLISHED_JWT_SECRETS:
            raise ValueError("this key is published in the repository, generate your own")
        return value


def get_settings() -> Settings:
    """Factory function for settings, enables test override.

    Raises ConfigurationError, naming each GRID_ variable at fault, instead of a bare pydantic
    error that names fields: whoever deploys sets variables, not fields.
    """
    try:
        return Settings()  # type: ignore[call-arg,unused-ignore]
    except ValidationError as error:
        fields = [str(item["loc"][0]) if item["loc"] else "?" for item in error.errors()]
        problems = [f"GRID_{field.upper()}: {item['msg']}" for field, item in zip(fields, error.errors(), strict=True)]
        message = "GRID configuration refused -- " + "; ".join(problems)
        if "jwt_secret_key" in fields:
            message += f". Generate a JWT key with: {_GENERATE_JWT_SECRET}"
        raise ConfigurationError(message) from error
