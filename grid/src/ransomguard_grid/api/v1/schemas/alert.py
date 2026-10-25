"""Pydantic schemas for alert ingestion."""

from datetime import datetime
from typing import Literal

from pydantic import BaseModel, ConfigDict, Field, model_validator

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


AttributionState = Literal["resolved", "unavailable", "ambiguous"]
AttributionReason = Literal["no_write_event", "system_pid", "pid_reused", "process_exited", "etw_unavailable"]
ATTRIBUTION_FINAL_STATES: frozenset[str] = frozenset({"resolved", "unavailable", "ambiguous"})


class GenealogyProcess(BaseModel):
    """One process in the chain behind a write."""

    model_config = ConfigDict(extra="forbid")

    pid: int = Field(ge=0, le=2**32 - 1)
    name: str = Field(max_length=260)
    path: str | None = Field(default=None, max_length=1024)
    started_at: datetime | None = None
    command_line: str | None = Field(default=None, max_length=2048)


class GenealogyPattern(BaseModel):
    """A MITRE ATT&CK technique the chain matched."""

    model_config = ConfigDict(extra="forbid")

    technique_id: str = Field(max_length=16)
    technique_name: str = Field(max_length=120)
    description: str = Field(max_length=500)


class GenealogyComplementRequest(BaseModel):
    """The chain of processes behind an alert (GENEALOGY), sent after the alert itself.

    The chain is built while the writer is still running, which is necessarily after the alert has
    gone: ransomware writes and exits, so waiting for the tree before sending the alert would
    delay every alert for the one case where it cannot be built anyway. It arrives as a complement,
    the same way the attribution does.

    `writer` is the process that wrote the file; `ancestors` are its parent, grandparent and so on,
    nearest first. The console reads them in that order and draws the chain the other way round.
    """

    model_config = ConfigDict(extra="forbid")

    writer: GenealogyProcess
    ancestors: list[GenealogyProcess] = Field(default_factory=list, max_length=10)
    patterns: list[GenealogyPattern] = Field(default_factory=list, max_length=10)


class GenealogyComplementResponse(BaseModel):
    """What the GRID did with a genealogy complement."""

    model_config = ConfigDict(extra="forbid")

    alert_id: str
    status: Literal["applied", "unchanged"]


class AttributionComplementRequest(BaseModel):
    """The attribution complement of a SENTINEL alert (ADR-025, section 3), bounded field by field.

    resolved names one process by (pid, process_created_at); unavailable names nobody and says why
    with a closed reason; ambiguous names nobody. Nothing else is accepted.
    """

    model_config = ConfigDict(extra="forbid")

    state: AttributionState
    pid: int | None = Field(default=None, ge=1, le=2**32 - 1)
    process_created_at: datetime | None = None
    process_name: str | None = Field(default=None, max_length=260)
    image_path: str | None = Field(default=None, max_length=1024)
    reason: AttributionReason | None = None

    @model_validator(mode="after")
    def _consistent(self) -> "AttributionComplementRequest":
        named = any(v is not None for v in (self.pid, self.process_created_at, self.process_name, self.image_path))
        if self.state == "resolved":
            if self.pid is None or self.process_created_at is None or self.reason is not None:
                raise ValueError("resolved requires pid and process_created_at, and no reason")
        elif self.state == "unavailable":
            if self.reason is None or named:
                raise ValueError("unavailable requires a reason and names no process")
        elif self.reason is not None or named:
            raise ValueError("ambiguous names no process and has no reason")
        return self


class AttributionComplementResponse(BaseModel):
    alert_id: str
    status: Literal["applied", "unchanged"]


class BatchAlertIngestRequest(BaseModel):
    alerts: list[AlertIngestRequest] = Field(min_length=1, max_length=100)


class BatchAlertIngestResponse(BaseModel):
    results: list[AlertIngestResponse]
