"""Pydantic schemas for authentication endpoints."""

from pydantic import BaseModel, ConfigDict, Field


class LoginRequest(BaseModel):
    model_config = ConfigDict(extra="forbid")

    tenant_code: str = Field(max_length=50)
    email: str = Field(max_length=255)
    password: str = Field(min_length=1, max_length=128)


class LoginResponse(BaseModel):
    access_token: str
    refresh_token: str = ""  # Kept for backward compat, but value is empty when using HttpOnly cookie
    token_type: str = "bearer"
    expires_in: int
    user_id: str
    tenant_id: str
    roles: list[str]


class RefreshRequest(BaseModel):
    """Body-based refresh (legacy). Cookie-based refresh reads from HttpOnly cookie."""
    model_config = ConfigDict(extra="forbid")

    refresh_token: str = ""  # Optional — if empty, server reads from cookie
