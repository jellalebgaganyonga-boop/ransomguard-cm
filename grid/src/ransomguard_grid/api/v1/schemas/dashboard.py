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
    # Joined from agents on agent_id AND tenant_id; None when the agent is not in the
    # caller's tenant (or no longer exists) -- never another hospital's machine name.
    agent_hostname: str | None = None
    # Stable module code derived from alert_type (core/alert_modules.py); UNKNOWN, never empty.
    module: str = "UNKNOWN"


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
    # Computed by the server at read time (services/agent_posture.py); the console translates
    # and colours, it never recomputes. Defaults are only for model_validate before the verdict.
    protection_state: str = "unknown"
    reason_codes: list[str] = Field(default_factory=list)
    signal_age_seconds: int | None = None


class ModuleStatusItem(BaseModel):
    code: str
    state: str
    reason_code: str | None
    changed_at: datetime


class AgentPostureItem(BaseModel):
    """Last posture reported. Null numbers mean "unavailable", never 0."""

    schema_version: int
    received_at: datetime
    agent_uptime_seconds: int
    threat_intel_version: str | None
    last_detection_at: datetime | None
    system_free_bytes: int | None
    db_volume_free_bytes: int | None
    pending_alerts: int | None
    last_upload_ok_at: datetime | None  # agent clock, as reported
    # Computed by the server at read time (agent's own measure when sent, never negative):
    # what the console shows as "last transmission N min ago".
    last_upload_age_seconds: int | None
    last_upload_error_code: str | None
    rejected_alerts: int | None
    modules: list[ModuleStatusItem]


class AgentDetailItem(AgentItem):
    posture: AgentPostureItem | None = None


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
    # SHA-256 (upper-case hex, as Get-FileHash prints it) of the install.ps1 this GRID serves at
    # /agent/install.ps1. Null when no installer is published: the console then shows no command.
    installer_sha256: str | None = None
