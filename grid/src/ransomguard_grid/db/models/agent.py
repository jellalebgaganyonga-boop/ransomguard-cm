"""Group 2: Agent models (6 models)."""

from datetime import UTC, datetime

from sqlalchemy import BigInteger, DateTime, Enum, ForeignKey, Integer, String, Text, UniqueConstraint
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


class AgentPosture(Base):
    """The last posture an agent reported: one row per agent, upserted at every v2 heartbeat.

    Volatile and rewritten every minute, so kept off ``agents`` (identity and enrolment). A v1
    heartbeat writes schema_version 1 and nothing else: the verdict reads it as "agent update
    required". Agent timestamps (last_detection_at, last_upload_ok_at) are the agent's clock;
    received_at is the server's.
    """

    __tablename__ = "agent_posture"

    agent_id: Mapped[str] = mapped_column(String(36), ForeignKey("agents.id", ondelete="CASCADE"), primary_key=True)
    tenant_id: Mapped[str] = mapped_column(String(36), ForeignKey("tenants.id", ondelete="RESTRICT"), index=True, nullable=False)
    schema_version: Mapped[int] = mapped_column(Integer, nullable=False)
    agent_uptime_seconds: Mapped[int] = mapped_column(BigInteger, nullable=False)
    #: The workstation's own clock at the moment it sent this heartbeat. Compared with
    #: ``received_at`` it says how far the two have drifted apart -- the bench VMs have no Guest
    #: Additions and free-run, and a drifted workstation makes every alert look hours old.
    #: Null from an agent older than 2026-10-28, which did not send it.
    agent_clock_utc: Mapped[datetime | None] = mapped_column(DateTime(timezone=True), nullable=True)
    threat_intel_version: Mapped[str | None] = mapped_column(String(50), nullable=True)
    last_detection_at: Mapped[datetime | None] = mapped_column(DateTime(timezone=True), nullable=True)
    # Null = the agent could not measure it ("unavailable"), never 0.
    system_free_bytes: Mapped[int | None] = mapped_column(BigInteger, nullable=True)
    db_volume_free_bytes: Mapped[int | None] = mapped_column(BigInteger, nullable=True)
    pending_alerts: Mapped[int | None] = mapped_column(Integer, nullable=True)
    last_upload_ok_at: Mapped[datetime | None] = mapped_column(DateTime(timezone=True), nullable=True)
    # Age of the last successful upload, measured by the agent on its own clock when it sent
    # this heartbeat: immune to agent/server clock skew, preferred over last_upload_ok_at.
    last_upload_ok_age_seconds: Mapped[int | None] = mapped_column(BigInteger, nullable=True)
    last_upload_error_code: Mapped[str | None] = mapped_column(String(50), nullable=True)
    # Alerts the GRID refused (400/422) over the agent's last 24 h; null = not reported.
    rejected_alerts: Mapped[int | None] = mapped_column(Integer, nullable=True)
    # Audit entries the agent could not record since it started (AGT-AUD-002); null = not reported.
    audit_write_failures: Mapped[int | None] = mapped_column(Integer, nullable=True)
    received_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), nullable=False)

    def __repr__(self) -> str:
        return f"<AgentPosture agent={self.agent_id[:8]} v{self.schema_version}>"


class AgentModuleStatus(Base):
    """Current state of one module of one agent: "which endpoints have SENTINEL inactive?" is SQL."""

    __tablename__ = "agent_module_status"
    __table_args__ = (UniqueConstraint("agent_id", "module_code", name="uq_agent_module_status_agent_module"),)

    id: Mapped[str] = mapped_column(String(36), primary_key=True)
    tenant_id: Mapped[str] = mapped_column(String(36), ForeignKey("tenants.id", ondelete="RESTRICT"), index=True, nullable=False)
    agent_id: Mapped[str] = mapped_column(String(36), ForeignKey("agents.id", ondelete="CASCADE"), index=True, nullable=False)
    module_code: Mapped[str] = mapped_column(String(32), nullable=False)
    state: Mapped[str] = mapped_column(String(32), nullable=False)
    reason_code: Mapped[str | None] = mapped_column(String(50), nullable=True)
    # Agent clock, as reported: displayed, never used for a threshold (the verdict uses server
    # times and the agent's own durations only).
    changed_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), nullable=False)
    updated_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), nullable=False)  # server clock

    def __repr__(self) -> str:
        return f"<AgentModuleStatus {self.module_code}={self.state}>"


class AgentPostureChange(Base):
    """A module transition, written only when the state or reason changes. Purged after 90 days.

    Agent restarts are recorded here too, with module_code ``AGENT`` and new_state ``restarted``:
    the verdict counts those of the last hour to detect a restart loop.
    """

    __tablename__ = "agent_posture_changes"

    id: Mapped[str] = mapped_column(String(36), primary_key=True)
    tenant_id: Mapped[str] = mapped_column(String(36), ForeignKey("tenants.id", ondelete="RESTRICT"), index=True, nullable=False)
    agent_id: Mapped[str] = mapped_column(String(36), ForeignKey("agents.id", ondelete="CASCADE"), index=True, nullable=False)
    module_code: Mapped[str] = mapped_column(String(32), nullable=False)
    old_state: Mapped[str | None] = mapped_column(String(32), nullable=True)
    old_reason_code: Mapped[str | None] = mapped_column(String(50), nullable=True)
    new_state: Mapped[str] = mapped_column(String(32), nullable=False)
    new_reason_code: Mapped[str | None] = mapped_column(String(50), nullable=True)
    created_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), index=True, nullable=False)  # server clock

    def __repr__(self) -> str:
        return f"<AgentPostureChange {self.module_code} {self.old_state}->{self.new_state}>"
