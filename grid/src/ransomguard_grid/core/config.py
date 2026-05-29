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


def get_settings() -> Settings:
    """Factory function for settings, enables test override."""
    return Settings()  # type: ignore[call-arg,unused-ignore]
