"""Enum types for GRID database models. Match agent C# enums where applicable."""

import enum


class TenantStatus(str, enum.Enum):
    """Status of a tenant organization."""

    active = "active"
    suspended = "suspended"
    deleted = "deleted"


class AgentStatus(str, enum.Enum):
    """Lifecycle status of a deployed agent."""

    provisioned = "provisioned"
    active = "active"
    disconnected = "disconnected"
    decommissioned = "decommissioned"


class Severity(str, enum.Enum):
    """Alert severity — matches agent C# ScanSeverity."""

    Low = "Low"
    Medium = "Medium"
    High = "High"
    Critical = "Critical"


class AlertStatus(str, enum.Enum):
    """Workflow status of an alert."""

    New = "New"
    Investigating = "Investigating"
    Resolved = "Resolved"
    FalsePositive = "FalsePositive"
    Suppressed = "Suppressed"


class ThreatIntelStatus(str, enum.Enum):
    """Publication status of a threat intel package."""

    Draft = "Draft"
    Published = "Published"
    Deprecated = "Deprecated"


class CommandStatus(str, enum.Enum):
    """Status of a queued command."""

    Pending = "Pending"
    Dispatched = "Dispatched"
    Acknowledged = "Acknowledged"
    Completed = "Completed"
    Failed = "Failed"


class LogLevel(str, enum.Enum):
    """System log severity level."""

    Debug = "Debug"
    Info = "Info"
    Warning = "Warning"
    Error = "Error"
    Critical = "Critical"
