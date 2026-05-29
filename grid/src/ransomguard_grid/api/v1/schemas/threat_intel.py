"""Pydantic schemas for threat intel endpoints."""

from datetime import datetime

from pydantic import BaseModel, ConfigDict, Field


class ManifestResponse(BaseModel):
    """Response for GET /threat-intel/manifest."""

    version: str
    published_at: datetime
    package_size_bytes: int
    package_sha256: str
    package_url: str
    ed25519_signature_base64: str


class AppliedVersionRequest(BaseModel):
    """Request for POST /agents/{id}/threat-intel-version."""

    model_config = ConfigDict(extra="forbid")

    applied_version: str = Field(max_length=50)
    applied_at: datetime
    applied_status: str = Field(max_length=50)
    error_message: str | None = Field(max_length=2000, default=None)


class AppliedVersionResponse(BaseModel):
    """Response for POST /agents/{id}/threat-intel-version."""

    success: bool
