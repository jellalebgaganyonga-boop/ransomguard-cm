"""Pydantic schemas for agent enrollment and heartbeat."""

import base64
import binascii
from datetime import datetime
from typing import Annotated, Literal

from pydantic import BaseModel, ConfigDict, Field, StringConstraints, field_validator, model_validator

from ransomguard_grid.services.agent_posture import MAX_UPLOAD_AGE_SECONDS


class EnrollmentRequest(BaseModel):
    model_config = ConfigDict(extra="forbid")

    otp: str = Field(min_length=10, max_length=50, description="One-time enrollment token")
    hostname: str = Field(min_length=1, max_length=255)
    fqdn: str = Field(max_length=255)
    os_version: str = Field(max_length=100)
    agent_version: str = Field(max_length=50)
    hardware_fingerprint: str = Field(min_length=32, max_length=64, description="SHA-256 hardware ID")
    # The agent's OWN audit-signing public key (raw 32-byte Ed25519, base64). Its private key never
    # leaves the endpoint: the GRID verifies the agent's audit chain with this key, and holds no
    # key able to sign in the agent's name (GRID-SEC-003).
    ed25519_public_key_b64: str = Field(min_length=44, max_length=44)

    @field_validator("ed25519_public_key_b64")
    @classmethod
    def _raw_ed25519_public_key(cls, value: str) -> str:
        try:
            raw = base64.b64decode(value, validate=True)
        except (binascii.Error, ValueError) as exc:
            raise ValueError("not base64") from exc
        if len(raw) != 32:
            raise ValueError("an Ed25519 public key is 32 bytes")
        return value


class EnrollmentResponse(BaseModel):
    """No key material: the GRID never generates nor sends a signing key (GRID-SEC-003)."""

    model_config = ConfigDict(extra="forbid")

    agent_id: str
    tenant_id: str
    message: str


_ShortKey = Annotated[str, StringConstraints(max_length=64)]


class HeartbeatDisk(BaseModel):
    """Free bytes; null = the agent could not measure it, never 0."""

    model_config = ConfigDict(extra="forbid")

    system_free_bytes: int | None = Field(default=None, ge=0)
    db_volume_free_bytes: int | None = Field(default=None, ge=0)


class HeartbeatTransmission(BaseModel):
    """Alert transmission health. Null = not reported / unavailable, never 0."""

    model_config = ConfigDict(extra="forbid")

    pending_alerts: int | None = Field(default=None, ge=0)
    last_upload_ok_at: datetime | None = None
    # Age of the last successful upload on the agent's own clock, at send time. When present the
    # GRID uses it and ignores last_upload_ok_at, which is only as good as the clock skew.
    last_upload_ok_age_seconds: int | None = Field(default=None, ge=0, le=MAX_UPLOAD_AGE_SECONDS)
    # A stable code (NETWORK_UNREACHABLE, HTTP_422...), never an exception text.
    last_upload_error_code: str | None = Field(default=None, max_length=50, pattern=r"^[A-Za-z0-9_]+$")
    # Alerts the GRID refused (400/422), over the agent's last 24 h.
    rejected_alerts: int | None = Field(default=None, ge=0)


class HeartbeatModule(BaseModel):
    model_config = ConfigDict(extra="forbid")

    # Checked against the closed list by the server: an unknown code is logged, never stored.
    code: str = Field(min_length=1, max_length=64)
    state: Literal["active", "inactive", "disabled_by_config", "degraded"]
    reason_code: str | None = Field(default=None, max_length=50, pattern=r"^[a-z0-9_]+$")
    changed_at: datetime


class HeartbeatRequest(BaseModel):
    """Agent heartbeat. schema_version absent = v1, the legacy 3-field heartbeat, still accepted:
    the agents of a hospital are not all updated the same day.
    """

    model_config = ConfigDict(extra="forbid")

    schema_version: Literal[1, 2] = 1
    agent_uptime_seconds: int = Field(ge=0)
    threat_intel_version: str | None = Field(max_length=50, default=None)

    # v1 only: free-form, bounded, ignored (no posture, no log).
    modules_status: dict[_ShortKey, _ShortKey] | None = Field(default=None, max_length=32)

    # v2
    agent_version: str | None = Field(default=None, max_length=50)
    os_version: str | None = Field(default=None, max_length=100)
    last_detection_at: datetime | None = None
    disk: HeartbeatDisk | None = None
    transmission: HeartbeatTransmission | None = None
    modules: list[HeartbeatModule] = Field(default_factory=list, max_length=16)

    @model_validator(mode="after")
    def _one_entry_per_module(self) -> "HeartbeatRequest":
        codes = [m.code for m in self.modules]
        if len(codes) != len(set(codes)):
            raise ValueError("modules: each code may appear only once")
        return self


class HeartbeatResponse(BaseModel):
    next_heartbeat_in_seconds: int
    pending_commands: list[str] = Field(default_factory=list)
    server_time: datetime
