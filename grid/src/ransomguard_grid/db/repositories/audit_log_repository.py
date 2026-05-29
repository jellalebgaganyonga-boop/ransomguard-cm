"""Audit log repository with sequence number enforcement for chain integrity."""

from collections.abc import Sequence
from datetime import datetime

from sqlalchemy import func, select

from ransomguard_grid.db.models.alerts import AuditLog
from ransomguard_grid.db.repositories.base_repository import BaseRepository


class AuditLogRepository(BaseRepository[AuditLog]):
    """Repository for audit log entries with contiguous sequence enforcement."""

    model = AuditLog

    async def get_max_sequence(self, agent_id: str) -> int:
        """Get the highest sequence number for an agent within this tenant."""
        stmt = select(func.max(AuditLog.sequence_number)).where(
            AuditLog.tenant_id == self.tenant_id,
            AuditLog.agent_id == agent_id,
        )
        result = await self.session.execute(stmt)
        return result.scalar_one() or 0

    async def append_with_sequence_check(self, entry: AuditLog) -> AuditLog:
        """Append an audit log entry, verifying contiguous sequence number.

        Raises ValueError if sequence_number is not exactly max + 1.
        """
        max_seq = await self.get_max_sequence(entry.agent_id)
        expected = max_seq + 1
        if entry.sequence_number != expected:
            raise ValueError(
                f"Sequence gap: expected {expected}, got {entry.sequence_number} "
                f"(agent={entry.agent_id}, tenant={self.tenant_id})"
            )
        return await self.add(entry)

    async def search(
        self,
        agent_id: str | None = None,
        received_after: datetime | None = None,
        received_before: datetime | None = None,
        sequence_after: int | None = None,
        offset: int = 0,
        limit: int = 100,
    ) -> tuple[Sequence[AuditLog], int]:
        """Search audit logs with filters. ALWAYS filtered by tenant_id."""
        base = select(AuditLog).where(AuditLog.tenant_id == self.tenant_id)
        count_base = select(func.count()).select_from(AuditLog).where(AuditLog.tenant_id == self.tenant_id)

        if agent_id:
            base = base.where(AuditLog.agent_id == agent_id)
            count_base = count_base.where(AuditLog.agent_id == agent_id)
        if received_after:
            base = base.where(AuditLog.received_at >= received_after)
            count_base = count_base.where(AuditLog.received_at >= received_after)
        if received_before:
            base = base.where(AuditLog.received_at <= received_before)
            count_base = count_base.where(AuditLog.received_at <= received_before)
        if sequence_after is not None:
            base = base.where(AuditLog.sequence_number > sequence_after)
            count_base = count_base.where(AuditLog.sequence_number > sequence_after)

        total = (await self.session.execute(count_base)).scalar_one()
        items = (await self.session.execute(
            base.order_by(AuditLog.received_at.desc()).offset(offset).limit(min(limit, 200))
        )).scalars().all()

        return items, total
