"""Group 3: Alert models (6 models)."""

from datetime import UTC, datetime

from sqlalchemy import DateTime, Enum, Float, ForeignKey, Index, LargeBinary, String, Text, UniqueConstraint
from sqlalchemy.dialects.sqlite import JSON
from sqlalchemy.orm import Mapped, mapped_column

from ransomguard_grid.db.base import Base
from ransomguard_grid.db.models.enums import AlertStatus, Severity


class Alert(Base):
    """Security alert ingested from an agent. Idempotent via client_message_id."""

    __tablename__ = "alerts"
    __table_args__ = (
        UniqueConstraint("tenant_id", "agent_id", "client_message_id", name="uq_alert_idempotency"),
        Index("ix_alert_tenant_severity_status_detected", "tenant_id", "severity", "status", "detected_at"),
    )

    id: Mapped[str] = mapped_column(String(36), primary_key=True)
    tenant_id: Mapped[str] = mapped_column(String(36), ForeignKey("tenants.id", ondelete="RESTRICT"), index=True, nullable=False)
    agent_id: Mapped[str] = mapped_column(String(36), ForeignKey("agents.id", ondelete="RESTRICT"), nullable=False)
    client_message_id: Mapped[str] = mapped_column(String(64), nullable=False)
    alert_type: Mapped[str] = mapped_column(String(50), nullable=False)
    mitre_technique_id: Mapped[str | None] = mapped_column(String(20), nullable=True)
    severity: Mapped[Severity] = mapped_column(Enum(Severity), nullable=False)
    status: Mapped[AlertStatus] = mapped_column(Enum(AlertStatus), nullable=False, default=AlertStatus.New)
    detected_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), nullable=False)
    ingested_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), default=lambda: datetime.now(UTC), nullable=False)
    summary: Mapped[str] = mapped_column(String(500), nullable=False)
    raw_payload_json: Mapped[dict | None] = mapped_column(JSON, nullable=True)  # type: ignore[assignment]

    def __repr__(self) -> str:
        return f"<Alert {self.alert_type} severity={self.severity}>"


class AlertDetail(Base):
    """Flexible key-value detail for an alert."""

    __tablename__ = "alert_details"

    id: Mapped[str] = mapped_column(String(36), primary_key=True)
    tenant_id: Mapped[str] = mapped_column(String(36), ForeignKey("tenants.id", ondelete="RESTRICT"), index=True, nullable=False)
    alert_id: Mapped[str] = mapped_column(String(36), ForeignKey("alerts.id", ondelete="RESTRICT"), nullable=False)
    key: Mapped[str] = mapped_column(String(100), nullable=False)
    value: Mapped[str] = mapped_column(String(2000), nullable=False)

    def __repr__(self) -> str:
        return f"<AlertDetail {self.key}>"


class AlertArtifact(Base):
    """Hash reference to an alert artifact. HASH ONLY, never store artifact content per privacy by design."""

    __tablename__ = "alert_artifacts"

    id: Mapped[str] = mapped_column(String(36), primary_key=True)
    tenant_id: Mapped[str] = mapped_column(String(36), ForeignKey("tenants.id", ondelete="RESTRICT"), index=True, nullable=False)
    alert_id: Mapped[str] = mapped_column(String(36), ForeignKey("alerts.id", ondelete="RESTRICT"), nullable=False)
    artifact_type: Mapped[str] = mapped_column(String(50), nullable=False)
    artifact_hash_sha256: Mapped[str] = mapped_column(String(64), nullable=False)
    artifact_metadata_json: Mapped[dict | None] = mapped_column(JSON, nullable=True)  # type: ignore[assignment]

    def __repr__(self) -> str:
        return f"<AlertArtifact {self.artifact_type}>"


class AuditLog(Base):
    """Ed25519-signed audit log entry from an agent. Sequence-checked for chain integrity."""

    __tablename__ = "audit_logs_grid"
    __table_args__ = (
        UniqueConstraint("tenant_id", "agent_id", "sequence_number", name="uq_audit_log_sequence"),
        Index("ix_audit_tenant_agent_sequence", "tenant_id", "agent_id", "sequence_number"),
    )

    id: Mapped[str] = mapped_column(String(36), primary_key=True)
    tenant_id: Mapped[str] = mapped_column(String(36), ForeignKey("tenants.id", ondelete="RESTRICT"), index=True, nullable=False)
    agent_id: Mapped[str] = mapped_column(String(36), ForeignKey("agents.id", ondelete="RESTRICT"), nullable=False)
    sequence_number: Mapped[int] = mapped_column(nullable=False)
    payload_json: Mapped[dict | None] = mapped_column(JSON, nullable=True)  # type: ignore[assignment]
    ed25519_signature: Mapped[bytes] = mapped_column(LargeBinary, nullable=False)
    signing_key_id: Mapped[str] = mapped_column(String(64), nullable=False)
    received_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), default=lambda: datetime.now(UTC), nullable=False)

    def __repr__(self) -> str:
        return f"<AuditLog agent={self.agent_id[:8]} seq={self.sequence_number}>"


class AlertCorrelation(Base):
    """Correlation between two alerts (e.g., double-extortion link)."""

    __tablename__ = "alert_correlations"

    id: Mapped[str] = mapped_column(String(36), primary_key=True)
    tenant_id: Mapped[str] = mapped_column(String(36), ForeignKey("tenants.id", ondelete="RESTRICT"), index=True, nullable=False)
    primary_alert_id: Mapped[str] = mapped_column(String(36), ForeignKey("alerts.id", ondelete="RESTRICT"), nullable=False)
    related_alert_id: Mapped[str] = mapped_column(String(36), ForeignKey("alerts.id", ondelete="RESTRICT"), nullable=False)
    correlation_type: Mapped[str] = mapped_column(String(50), nullable=False)
    confidence: Mapped[float] = mapped_column(Float, nullable=False)
    created_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), default=lambda: datetime.now(UTC), nullable=False)

    def __repr__(self) -> str:
        return f"<AlertCorrelation {self.correlation_type}>"


class AlertStatusChange(Base):
    """Audit trail of alert status changes by analysts."""

    __tablename__ = "alert_status_changes"

    id: Mapped[str] = mapped_column(String(36), primary_key=True)
    tenant_id: Mapped[str] = mapped_column(String(36), ForeignKey("tenants.id", ondelete="RESTRICT"), index=True, nullable=False)
    alert_id: Mapped[str] = mapped_column(String(36), ForeignKey("alerts.id", ondelete="RESTRICT"), nullable=False)
    old_status: Mapped[AlertStatus] = mapped_column(Enum(AlertStatus), nullable=False)
    new_status: Mapped[AlertStatus] = mapped_column(Enum(AlertStatus), nullable=False)
    changed_by_user_id: Mapped[str] = mapped_column(String(36), ForeignKey("users.id", ondelete="RESTRICT"), nullable=False)
    justification: Mapped[str] = mapped_column(Text, nullable=False)
    changed_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), default=lambda: datetime.now(UTC), nullable=False)

    def __repr__(self) -> str:
        return f"<AlertStatusChange {self.old_status}->{self.new_status}>"
