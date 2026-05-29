"""Group 5: Operations models (3 models)."""

from datetime import UTC, datetime

from sqlalchemy import Boolean, DateTime, Enum, ForeignKey, String, Text
from sqlalchemy.dialects.sqlite import JSON
from sqlalchemy.orm import Mapped, mapped_column

from ransomguard_grid.db.base import Base
from ransomguard_grid.db.models.enums import CommandStatus, LogLevel


class CommandQueue(Base):
    """Command queued for delivery to an agent."""

    __tablename__ = "command_queue"

    id: Mapped[str] = mapped_column(String(36), primary_key=True)
    tenant_id: Mapped[str] = mapped_column(String(36), ForeignKey("tenants.id", ondelete="RESTRICT"), index=True, nullable=False)
    agent_id: Mapped[str] = mapped_column(String(36), ForeignKey("agents.id", ondelete="RESTRICT"), nullable=False)
    command_type: Mapped[str] = mapped_column(String(50), nullable=False)
    parameters_json: Mapped[dict | None] = mapped_column(JSON, nullable=True)  # type: ignore[assignment]
    status: Mapped[CommandStatus] = mapped_column(Enum(CommandStatus), nullable=False, default=CommandStatus.Pending)
    issued_by_user_id: Mapped[str] = mapped_column(String(36), ForeignKey("users.id", ondelete="RESTRICT"), nullable=False)
    issued_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), default=lambda: datetime.now(UTC), nullable=False)
    dispatched_at: Mapped[datetime | None] = mapped_column(DateTime(timezone=True), nullable=True)
    completed_at: Mapped[datetime | None] = mapped_column(DateTime(timezone=True), nullable=True)

    def __repr__(self) -> str:
        return f"<CommandQueue {self.command_type} status={self.status}>"


class CommandResponse(Base):
    """Response received from an agent for a queued command."""

    __tablename__ = "command_responses"

    id: Mapped[str] = mapped_column(String(36), primary_key=True)
    tenant_id: Mapped[str] = mapped_column(String(36), ForeignKey("tenants.id", ondelete="RESTRICT"), index=True, nullable=False)
    command_id: Mapped[str] = mapped_column(String(36), ForeignKey("command_queue.id", ondelete="RESTRICT"), nullable=False)
    response_payload_json: Mapped[dict | None] = mapped_column(JSON, nullable=True)  # type: ignore[assignment]
    received_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), default=lambda: datetime.now(UTC), nullable=False)
    success: Mapped[bool] = mapped_column(Boolean, nullable=False)

    def __repr__(self) -> str:
        return f"<CommandResponse cmd={self.command_id[:8]} success={self.success}>"


class SystemLog(Base):
    """System-level log entry for server operations."""

    __tablename__ = "system_logs"

    id: Mapped[str] = mapped_column(String(36), primary_key=True)
    tenant_id: Mapped[str] = mapped_column(String(36), ForeignKey("tenants.id", ondelete="RESTRICT"), index=True, nullable=False)
    level: Mapped[LogLevel] = mapped_column(Enum(LogLevel), nullable=False)
    component: Mapped[str] = mapped_column(String(100), nullable=False)
    message: Mapped[str] = mapped_column(Text, nullable=False)
    context_json: Mapped[dict | None] = mapped_column(JSON, nullable=True)  # type: ignore[assignment]
    created_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), default=lambda: datetime.now(UTC), nullable=False)

    def __repr__(self) -> str:
        return f"<SystemLog {self.level} {self.component}>"
