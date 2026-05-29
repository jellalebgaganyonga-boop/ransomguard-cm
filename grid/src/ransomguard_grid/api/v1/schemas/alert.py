"""Pydantic schemas for alert ingestion."""

from datetime import datetime
from typing import Literal

from pydantic import BaseModel, ConfigDict, Field

from ransomguard_grid.db.models.enums import Severity


class AlertDetailInput(BaseModel):
    model_config = ConfigDict(extra="forbid")

    key: str = Field(max_length=100)
    value: str = Field(max_length=2000)


class AlertArtifactInput(BaseModel):
    model_config = ConfigDict(extra="forbid")

    artifact_type: str = Field(max_length=50)
    artifact_hash_sha256: str = Field(min_length=64, max_length=64)
    artifact_metadata: dict[str, str] = Field(default_factory=dict)


class AlertIngestRequest(BaseModel):
    model_config = ConfigDict(extra="forbid")

    client_message_id: str = Field(min_length=10, max_length=64, description="Agent-generated unique ID")
    alert_type: str = Field(max_length=50)
    mitre_technique_id: str | None = Field(max_length=20, default=None)
    severity: Severity
    detected_at: datetime
    summary: str = Field(max_length=500)
    details: list[AlertDetailInput] = Field(default_factory=list, max_length=20)
    artifacts: list[AlertArtifactInput] = Field(default_factory=list, max_length=10)
    raw_payload: dict[str, object] = Field(default_factory=dict)


class AlertIngestResponse(BaseModel):
    alert_id: str
    status: Literal["ingested", "duplicate"]
    ingested_at: datetime


class BatchAlertIngestRequest(BaseModel):
    alerts: list[AlertIngestRequest] = Field(min_length=1, max_length=100)


class BatchAlertIngestResponse(BaseModel):
    results: list[AlertIngestResponse]
