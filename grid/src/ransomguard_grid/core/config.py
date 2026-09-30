"""Application configuration via Pydantic Settings with environment variable support."""

from pathlib import Path

from pydantic import Field
from pydantic_settings import BaseSettings, SettingsConfigDict


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

    # JWT (dashboard)
    jwt_secret_key: str = Field(default="dev-secret-key-minimum-32-characters-long!", min_length=32)
    jwt_algorithm: str = "HS256"
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


def get_settings() -> Settings:
    """Factory function for settings, enables test override."""
    return Settings()  # type: ignore[call-arg,unused-ignore]
