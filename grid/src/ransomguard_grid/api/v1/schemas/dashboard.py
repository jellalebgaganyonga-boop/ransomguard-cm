"""Pydantic schemas for dashboard endpoints."""

from datetime import datetime
from typing import Any

from pydantic import BaseModel, ConfigDict, Field

from ransomguard_grid.db.models.enums import AlertStatus, Severity


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
