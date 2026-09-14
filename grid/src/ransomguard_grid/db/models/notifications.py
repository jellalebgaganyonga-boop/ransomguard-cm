"""Group 6: Notification and User Action Log models."""

from datetime import UTC, datetime

from sqlalchemy import Boolean, DateTime, ForeignKey, String
from sqlalchemy.dialects.sqlite import JSON
from sqlalchemy.orm import Mapped, mapped_column

from ransomguard_grid.db.base import Base


class NotificationPreference(Base):
    """Per-user notification preferences (email on/off per severity)."""

    __tablename__ = "notification_preferences"

    id: Mapped[str] = mapped_column(String(36), primary_key=True)
    user_id: Mapped[str] = mapped_column(String(36), ForeignKey("users.id", ondelete="RESTRICT"), nullable=False, index=True)
    tenant_id: Mapped[str] = mapped_column(String(36), ForeignKey("tenants.id", ondelete="RESTRICT"), nullable=False, index=True)
    email_critical: Mapped[bool] = mapped_column(Boolean, default=True, nullable=False)
    email_high: Mapped[bool] = mapped_column(Boolean, default=True, nullable=False)
    email_medium: Mapped[bool] = mapped_column(Boolean, default=False, nullable=False)
    email_low: Mapped[bool] = mapped_column(Boolean, default=False, nullable=False)
    created_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), default=lambda: datetime.now(UTC), nullable=False)
    updated_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), default=lambda: datetime.now(UTC), nullable=False)

    def __repr__(self) -> str:
        return f"<NotificationPreference user={self.user_id[:8]}>"

    def should_notify(self, severity: str) -> bool:
        """Check if this severity should trigger an email."""
        mapping = {
            "Critical": self.email_critical,
            "High": self.email_high,
            "Medium": self.email_medium,
            "Low": self.email_low,
        }
        return mapping.get(severity, False)


class UserActionLog(Base):
    """Audit trail for user actions on the dashboard (who did what, when).

    Unlike AuditLog (Ed25519 agent chain), this tracks human actions:
    login, logout, alert status change, user create/disable, command issued, etc.
    """

    __tablename__ = "user_action_logs"

    id: Mapped[str] = mapped_column(String(36), primary_key=True)
    tenant_id: Mapped[str] = mapped_column(String(36), ForeignKey("tenants.id", ondelete="RESTRICT"), nullable=False, index=True)
    actor_user_id: Mapped[str] = mapped_column(
        String(36), ForeignKey("users.id", ondelete="RESTRICT"), nullable=False, index=True
    )
    action_type: Mapped[str] = mapped_column(String(50), nullable=False, index=True)
    target_type: Mapped[str | None] = mapped_column(String(50), nullable=True)
    target_id: Mapped[str | None] = mapped_column(String(36), nullable=True)
    details_json: Mapped[dict | None] = mapped_column(JSON, nullable=True)  # type: ignore[assignment]
    ip_address: Mapped[str | None] = mapped_column(String(45), nullable=True)
    created_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), default=lambda: datetime.now(UTC), nullable=False)

    def __repr__(self) -> str:
        return f"<UserActionLog {self.action_type} by={self.actor_user_id[:8]}>"
