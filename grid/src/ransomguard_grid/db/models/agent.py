"""Group 2: Agent models (4 models)."""

from datetime import UTC, datetime

from sqlalchemy import BigInteger, DateTime, Enum, ForeignKey, String, Text
from sqlalchemy.dialects.sqlite import JSON
from sqlalchemy.orm import Mapped, mapped_column, relationship

from ransomguard_grid.db.base import Base
from ransomguard_grid.db.models.enums import AgentStatus


class Agent(Base):
    """Deployed RansomGuard agent on an endpoint."""

    __tablename__ = "agents"

    id: Mapped[str] = mapped_column(String(36), primary_key=True)
    tenant_id: Mapped[str] = mapped_column(String(36), ForeignKey("tenants.id", ondelete="RESTRICT"), index=True, nullable=False)
    hostname: Mapped[str] = mapped_column(String(255), nullable=False)
    fqdn: Mapped[str] = mapped_column(String(255), nullable=False)
    os_version: Mapped[str] = mapped_column(String(100), nullable=False)
    agent_version: Mapped[str] = mapped_column(String(50), nullable=False)
    hardware_fingerprint: Mapped[str] = mapped_column(String(64), nullable=False)
    status: Mapped[AgentStatus] = mapped_column(Enum(AgentStatus), nullable=False, default=AgentStatus.provisioned)
    last_heartbeat_at: Mapped[datetime | None] = mapped_column(DateTime(timezone=True), nullable=True)
    enrolled_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), default=lambda: datetime.now(UTC), nullable=False)
    created_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), default=lambda: datetime.now(UTC), nullable=False)

    certificates: Mapped[list["AgentCertificate"]] = relationship(back_populates="agent")

    def __repr__(self) -> str:
        return f"<Agent {self.hostname}>"


class AgentCertificate(Base):
    """X.509 client certificate issued to an agent for mTLS."""

    __tablename__ = "agent_certificates"

    id: Mapped[str] = mapped_column(String(36), primary_key=True)
    agent_id: Mapped[str] = mapped_column(String(36), ForeignKey("agents.id", ondelete="RESTRICT"), nullable=False)
    serial_number: Mapped[str] = mapped_column(String(64), unique=True, nullable=False)
    fingerprint_sha256: Mapped[str] = mapped_column(String(64), nullable=False)
    not_before: Mapped[datetime] = mapped_column(DateTime(timezone=True), nullable=False)
    not_after: Mapped[datetime] = mapped_column(DateTime(timezone=True), nullable=False)
    revoked_at: Mapped[datetime | None] = mapped_column(DateTime(timezone=True), nullable=True)
    revocation_reason: Mapped[str | None] = mapped_column(String(500), nullable=True)
    ed25519_public_key_pem: Mapped[str | None] = mapped_column(Text, nullable=True)

    agent: Mapped["Agent"] = relationship(back_populates="certificates")

    def __repr__(self) -> str:
        return f"<AgentCertificate serial={self.serial_number[:16]}>"


class AgentConfiguration(Base):
    """Configuration pushed to an agent."""

    __tablename__ = "agent_configurations"

    id: Mapped[str] = mapped_column(String(36), primary_key=True)
    tenant_id: Mapped[str] = mapped_column(String(36), ForeignKey("tenants.id", ondelete="RESTRICT"), index=True, nullable=False)
    agent_id: Mapped[str] = mapped_column(String(36), ForeignKey("agents.id", ondelete="RESTRICT"), nullable=False)
    config_yaml: Mapped[str] = mapped_column(Text, nullable=False)
    applied_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), nullable=False)
    applied_by_user_id: Mapped[str] = mapped_column(String(36), ForeignKey("users.id", ondelete="RESTRICT"), nullable=False)
    created_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), default=lambda: datetime.now(UTC), nullable=False)

    def __repr__(self) -> str:
        return f"<AgentConfiguration agent={self.agent_id[:8]}>"


class AgentHeartbeat(Base):
    """Heartbeat record from an agent."""

    __tablename__ = "agent_heartbeats"

    id: Mapped[str] = mapped_column(String(36), primary_key=True)
    tenant_id: Mapped[str] = mapped_column(String(36), ForeignKey("tenants.id", ondelete="RESTRICT"), index=True, nullable=False)
    agent_id: Mapped[str] = mapped_column(String(36), ForeignKey("agents.id", ondelete="RESTRICT"), nullable=False)
    received_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), default=lambda: datetime.now(UTC), nullable=False)
    agent_uptime_seconds: Mapped[int] = mapped_column(BigInteger, nullable=False)
    threat_intel_version: Mapped[str | None] = mapped_column(String(50), nullable=True)
    modules_status_json: Mapped[dict | None] = mapped_column(JSON, nullable=True)  # type: ignore[assignment]

    def __repr__(self) -> str:
        return f"<AgentHeartbeat agent={self.agent_id[:8]}>"
