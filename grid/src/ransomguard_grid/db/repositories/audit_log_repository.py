"""Audit log repository with sequence number enforcement for chain integrity."""

from sqlalchemy import func, select
from sqlalchemy.ext.asyncio import AsyncSession

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
