"""Pydantic schemas for dashboard endpoints."""

from datetime import datetime
from typing import Any

from pydantic import BaseModel, ConfigDict, Field

from ransomguard_grid.db.models.enums import AlertStatus


class PaginatedResponse(BaseModel):
    """Base paginated response."""

    total: int
    offset: int
    limit: int


class AlertItem(BaseModel):
    model_config = ConfigDict(from_attributes=True)

    id: str
    agent_id: str
    alert_type: str
    mitre_technique_id: str | None
    severity: str
    status: str
    detected_at: datetime
    ingested_at: datetime
    summary: str


class PaginatedAlertResponse(PaginatedResponse):
    items: list[AlertItem]


class AgentItem(BaseModel):
    model_config = ConfigDict(from_attributes=True)

    id: str
    hostname: str
    fqdn: str
    os_version: str
    agent_version: str
    status: str
    last_heartbeat_at: datetime | None


class PaginatedAgentResponse(PaginatedResponse):
    items: list[AgentItem]


class UpdateAlertStatusRequest(BaseModel):
    model_config = ConfigDict(extra="forbid")

    new_status: AlertStatus
    justification: str = Field(min_length=5, max_length=2000)


class MetricsSummary(BaseModel):
    total_agents: int
    active_agents: int
    alerts_24h: int
    critical_alerts_24h: int


class IssueCommandRequest(BaseModel):
    model_config = ConfigDict(extra="forbid")

    agent_id: str
    command_type: str = Field(max_length=50)
    parameters: dict[str, Any] = Field(default_factory=dict)


class CommandItem(BaseModel):
    model_config = ConfigDict(from_attributes=True)

    id: str
    agent_id: str
    command_type: str
    status: str
    issued_at: datetime


class CreateUserRequest(BaseModel):
    model_config = ConfigDict(extra="forbid")

    email: str = Field(max_length=255)
    full_name: str = Field(max_length=200)
    password: str = Field(min_length=8, max_length=128)
    roles: list[str] = Field(default_factory=list)


class UserItem(BaseModel):
    model_config = ConfigDict(from_attributes=True)

    id: str
    email: str
    full_name: str
    is_active: bool
    last_login_at: datetime | None


class PaginatedUserResponse(PaginatedResponse):
    items: list[UserItem]


class UpdateUserRolesRequest(BaseModel):
    model_config = ConfigDict(extra="forbid")

    role_names: list[str] = Field(max_length=10)


class UserWithRolesItem(BaseModel):
    id: str
    email: str
    full_name: str
    is_active: bool
    roles: list[str]


class AuditLogItem(BaseModel):
    model_config = ConfigDict(from_attributes=True)

    id: str
    agent_id: str
    sequence_number: int
    signing_key_id: str
    received_at: datetime


class PaginatedAuditLogResponse(PaginatedResponse):
    items: list[AuditLogItem]


# --- Notification Preferences ---


class NotificationPreferenceItem(BaseModel):
    model_config = ConfigDict(from_attributes=True)

    id: str
    email_critical: bool
    email_high: bool
    email_medium: bool
    email_low: bool


class UpdateNotificationPreferenceRequest(BaseModel):
    model_config = ConfigDict(extra="forbid")

    email_critical: bool = True
    email_high: bool = True
    email_medium: bool = False
    email_low: bool = False


# --- User Action Logs ---


class UserActionLogItem(BaseModel):
    model_config = ConfigDict(from_attributes=True)

    id: str
    actor_user_id: str
    actor_email: str | None = None
    action_type: str
    target_type: str | None
    target_id: str | None
    details_json: dict[str, Any] | None
    ip_address: str | None
    created_at: datetime


class PaginatedUserActionLogResponse(PaginatedResponse):
    items: list[UserActionLogItem]


# --- User item with roles (for GET /users fix) ---


class UserItemWithRoles(BaseModel):
    model_config = ConfigDict(from_attributes=True)

    id: str
    email: str
    full_name: str
    is_active: bool
    last_login_at: datetime | None
    roles: list[str] = Field(default_factory=list)


class PaginatedUserWithRolesResponse(PaginatedResponse):
    items: list[UserItemWithRoles]


class ProvisionAgentResponse(BaseModel):
    """Response for agent provisioning — contains the one-time enrollment token."""

    otp: str = Field(description="One-time enrollment token (format: RG-CAM-YYYY-XXXXXX)")
    expires_in_minutes: int = Field(default=30)
