"""Group 4: Threat Intel models (3 models)."""

from datetime import UTC, datetime

from sqlalchemy import BigInteger, DateTime, Enum, ForeignKey, LargeBinary, String
from sqlalchemy.orm import Mapped, mapped_column

from ransomguard_grid.db.base import Base
from ransomguard_grid.db.models.enums import ThreatIntelStatus


class ThreatIntelVersion(Base):
    """Published threat intel package version. System-level, no tenant_id."""

    __tablename__ = "threat_intel_versions"

    id: Mapped[str] = mapped_column(String(36), primary_key=True)
    version_string: Mapped[str] = mapped_column(String(50), unique=True, nullable=False)
    published_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), default=lambda: datetime.now(UTC), nullable=False)
    package_size_bytes: Mapped[int] = mapped_column(BigInteger, nullable=False)
    package_sha256: Mapped[str] = mapped_column(String(64), nullable=False)
    ed25519_signature: Mapped[bytes] = mapped_column(LargeBinary, nullable=False)
    status: Mapped[ThreatIntelStatus] = mapped_column(Enum(ThreatIntelStatus), nullable=False, default=ThreatIntelStatus.Draft)

    def __repr__(self) -> str:
        return f"<ThreatIntelVersion {self.version_string}>"


class ThreatIntelPackage(Base):
    """Individual file within a threat intel version package."""

    __tablename__ = "threat_intel_packages"

    id: Mapped[str] = mapped_column(String(36), primary_key=True)
    version_id: Mapped[str] = mapped_column(String(36), ForeignKey("threat_intel_versions.id", ondelete="RESTRICT"), nullable=False)
    file_name: Mapped[str] = mapped_column(String(255), nullable=False)
    file_path: Mapped[str] = mapped_column(String(1000), nullable=False)
    file_size_bytes: Mapped[int] = mapped_column(BigInteger, nullable=False)
    file_sha256: Mapped[str] = mapped_column(String(64), nullable=False)

    def __repr__(self) -> str:
        return f"<ThreatIntelPackage {self.file_name}>"


class AgentThreatIntelVersion(Base):
    """Tracks which threat intel version an agent has applied."""

    __tablename__ = "agent_threat_intel_versions"

    id: Mapped[str] = mapped_column(String(36), primary_key=True)
    tenant_id: Mapped[str] = mapped_column(String(36), ForeignKey("tenants.id", ondelete="RESTRICT"), index=True, nullable=False)
    agent_id: Mapped[str] = mapped_column(String(36), ForeignKey("agents.id", ondelete="RESTRICT"), nullable=False)
    version_id: Mapped[str] = mapped_column(String(36), ForeignKey("threat_intel_versions.id", ondelete="RESTRICT"), nullable=False)
    applied_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), default=lambda: datetime.now(UTC), nullable=False)
    applied_status: Mapped[str] = mapped_column(String(50), nullable=False)
    error_message: Mapped[str | None] = mapped_column(String(2000), nullable=True)

    def __repr__(self) -> str:
        return f"<AgentThreatIntelVersion agent={self.agent_id[:8]}>"
