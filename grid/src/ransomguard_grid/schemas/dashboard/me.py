"""
Pydantic schemas for the GET /api/v1/dashboard/me endpoint.

Adapted for the actual repo where User.id and Tenant.id are str (UUID as string),
EmailStr is not available (no pydantic[email] installed), and TenantStatus uses
"deleted" (not "archived").

Reference: STRIDE WT1.6 (cache poisoning) — tenant_id from this response
           is used to namespace all client-side query caches.
"""

from datetime import datetime
from typing import Literal

from pydantic import BaseModel, ConfigDict, Field


# ---------------------------------------------------------------------------
# Nested schemas
# ---------------------------------------------------------------------------


class TenantContext(BaseModel):
    """Minimal tenant context returned in /me response."""

    model_config = ConfigDict(extra="forbid", from_attributes=True)

    id: str = Field(
        ...,
        description="Tenant UUID (as string). Frontend uses this to namespace cache keys.",
    )
    name: str = Field(
        ...,
        min_length=1,
        max_length=255,
        description="Human-readable tenant name.",
    )
    status: Literal["active", "suspended", "deleted"] = Field(
        ...,
        description="Current tenant lifecycle status.",
    )


class UserPreferences(BaseModel):
    """User preferences relevant to the frontend rendering."""

    model_config = ConfigDict(extra="forbid")

    language: Literal["fr", "en"] = Field(
        default="fr",
        description="Preferred UI language. Defaults to French per product spec.",
    )
    timezone: str = Field(
        default="Africa/Douala",
        description="IANA timezone name for date/time rendering.",
    )


# ---------------------------------------------------------------------------
# Response schema
# ---------------------------------------------------------------------------


class MeResponse(BaseModel):
    """Response schema for GET /api/v1/dashboard/me."""

    model_config = ConfigDict(extra="forbid", from_attributes=True)

    user_id: str = Field(
        ...,
        description="Authenticated user's UUID (as string).",
    )
    email: str = Field(
        ...,
        description="User's email address.",
    )
    full_name: str = Field(
        ...,
        min_length=1,
        max_length=255,
        description="User's full display name.",
    )
    is_active: bool = Field(
        ...,
        description="Account active status.",
    )
    tenant: TenantContext = Field(
        ...,
        description="Tenant context for this user's session.",
    )
    roles: list[str] = Field(
        ...,
        description="List of role names assigned to the user.",
    )
    last_login_at: datetime | None = Field(
        default=None,
        description="Timestamp of the last successful login. None for first-time login.",
    )
    preferences: UserPreferences = Field(
        default_factory=UserPreferences,
        description="User preferences (language, timezone).",
    )
