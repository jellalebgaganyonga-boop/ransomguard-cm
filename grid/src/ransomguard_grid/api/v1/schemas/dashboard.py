"""Pydantic schemas for dashboard endpoints."""

from datetime import datetime
from typing import Any, Literal

from pydantic import BaseModel, ConfigDict, Field, field_validator

from ransomguard_grid.db.models.enums import AlertStatus


class PaginatedResponse(BaseModel):
    """Base paginated response."""

    total: int
    offset: int
    limit: int


class AlertAttribution(BaseModel):
    """Who wrote the canary, as the agent's complement said (ADR-025): state pending until it arrives,
    then resolved (a process), unavailable (a closed reason) or ambiguous."""

    state: str
    reason: str | None = None
    process_id: int | None = None
    process_name: str | None = None
    process_path: str | None = None
    process_created_at: str | None = None


class AlertCommandItem(BaseModel):
    """A command of the console on this alert, with the agent's answer when it came."""

    id: str
    command_type: str
    status: str
    issued_at: datetime
    dispatched_at: datetime | None = None
    completed_at: datetime | None = None
    success: bool | None = None
    result: dict[str, Any] | None = None


class AlertResponseItem(BaseModel):
    """What the agent did about this alert (ADR-026): a response alert linked by response_to."""

    alert_id: str
    alert_type: str
    response: str
    detected_at: datetime
    suspension_id: str | None = None
    process_id: int | None = None
    process_name: str | None = None
    reason: str | None = None


class SuspendedProcessItem(BaseModel):
    """
    A process this agent reports as suspended right now (ADR-026).

    Open means: the agent reported the suspension and has not reported it closed. The agent
    reports a closure when the process is resumed on request, or when it is gone. A resume that
    failed leaves it open and sets `resume_failed`: the process is still frozen and cannot be
    unfrozen until the machine restarts.
    """

    suspension_id: str
    # The canary alert the suspension answers: what the console names to ask for a resume, and
    # whose attribution the GRID reads. The PID never comes from the client.
    alert_id: str
    # Its client_message_id, used to find it; not useful to the console.
    response_to: str | None = None
    process_id: int | None = None
    process_name: str | None = None
    canary_path: str | None = None
    suspended_at: datetime
    resume_failed: bool = False
    # How many times the agent has escalated this suspension for want of a human decision.
    escalations: int = 0


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
    # The attribution, on the alert detail only; None for an alert that carries none (not SENTINEL,
    # or sent before ADR-025).
    attribution: AlertAttribution | None = None
    # The console's commands on this alert and the agent's responses (ADR-026); detail only.
    commands: list[AlertCommandItem] = Field(default_factory=list)
    responses: list[AlertResponseItem] = Field(default_factory=list)


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
    audit_write_failures: int | None
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

    @field_validator("email")
    @classmethod
    def _normalize_email(cls, value: str) -> str:
        # One account per (tenant, email), case-insensitive (GRID-USR-001).
        return value.strip().lower()


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


class UpdateMeRequest(BaseModel):
    """What a user may change on their own profile (Settings > Profile)."""

    model_config = ConfigDict(extra="forbid")

    full_name: str = Field(min_length=1, max_length=200)

    @field_validator("full_name")
    @classmethod
    def _not_blank(cls, value: str) -> str:
        stripped = value.strip()
        if not stripped:
            raise ValueError("full_name must not be blank")
        return stripped


class UpdatePreferencesRequest(BaseModel):
    """Settings > Language: kept on the GRID, applied by the console at sign-in."""

    model_config = ConfigDict(extra="forbid")

    language: Literal["en", "fr"]
    timezone: str | None = Field(default=None, max_length=64)


class ChangePasswordRequest(BaseModel):
    """Settings > Security: the current password proves it is the user, the new one replaces it."""

    model_config = ConfigDict(extra="forbid")

    current_password: str = Field(min_length=1, max_length=128)
    new_password: str = Field(min_length=8, max_length=128)


class InstallerDownloadRequest(BaseModel):
    """The double-click installer for one enrollment code (ADR-027)."""

    model_config = ConfigDict(extra="forbid")

    # The code the console just obtained from /agents/provision; it stays single-use, the agent
    # consumes it at enrollment. Never logged whole.
    otp: str = Field(min_length=10, max_length=50, pattern=r"^[A-Z0-9-]+$")
    # The GRID address the agent will call: the host the console was opened on, as for the
    # scripted command, never a value the server guesses.
    host: str = Field(min_length=1, max_length=253, pattern=r"^[A-Za-z0-9.-]+$")
    port: int = Field(default=8443, ge=1, le=65535)


class ProvisionAgentResponse(BaseModel):
    """Response for agent provisioning — contains the one-time enrollment token."""

    otp: str = Field(description="One-time enrollment token (format: RG-CAM-YYYY-XXXXXX)")
    expires_in_minutes: int = Field(default=30)
    # SHA-256 (upper-case hex, as Get-FileHash prints it) of the install.ps1 this GRID serves at
    # /agent/install.ps1. Null when no installer is published: the console then shows no command.
    installer_sha256: str | None = None
