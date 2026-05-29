"""Pydantic schemas for agent enrollment and heartbeat."""

from datetime import datetime

from pydantic import BaseModel, ConfigDict, Field


class EnrollmentRequest(BaseModel):
    model_config = ConfigDict(extra="forbid")

    otp: str = Field(min_length=10, max_length=50, description="One-time enrollment token")
    hostname: str = Field(min_length=1, max_length=255)
    fqdn: str = Field(max_length=255)
    os_version: str = Field(max_length=100)
    agent_version: str = Field(max_length=50)
    hardware_fingerprint: str = Field(min_length=32, max_length=64, description="SHA-256 hardware ID")


class EnrollmentResponse(BaseModel):
    agent_id: str
    tenant_id: str
    message: str
    ed25519_private_key_pem: str | None = None


class HeartbeatRequest(BaseModel):
    model_config = ConfigDict(extra="forbid")

    agent_uptime_seconds: int = Field(ge=0)
    threat_intel_version: str | None = Field(max_length=50, default=None)
    modules_status: dict[str, str] = Field(default_factory=dict)


class HeartbeatResponse(BaseModel):
    next_heartbeat_in_seconds: int
    pending_commands: list[str] = Field(default_factory=list)
    server_time: datetime
