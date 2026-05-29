"""Pydantic schemas for audit log ingestion."""

from datetime import datetime

from pydantic import BaseModel, ConfigDict, Field


class AuditLogEntry(BaseModel):
    model_config = ConfigDict(extra="forbid")

    sequence_number: int = Field(ge=0)
    payload: dict[str, object]
    ed25519_signature_base64: str
    signing_key_id: str = Field(max_length=64)
    timestamp: datetime


class AuditLogBatchRequest(BaseModel):
    entries: list[AuditLogEntry] = Field(min_length=1, max_length=500)


class AuditLogBatchResponse(BaseModel):
    accepted_count: int
    rejected_count: int
    rejected_reasons: list[str] = Field(default_factory=list)
