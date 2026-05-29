"""Agent heartbeat repository with tenant isolation."""

from collections.abc import Sequence

from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from ransomguard_grid.db.models.agent import AgentHeartbeat
from ransomguard_grid.db.repositories.base_repository import BaseRepository


class AgentHeartbeatRepository(BaseRepository[AgentHeartbeat]):
    """Repository for agent heartbeat records."""

    model = AgentHeartbeat

    async def append(self, heartbeat: AgentHeartbeat) -> AgentHeartbeat:
        """Persist a new heartbeat record."""
        return await self.add(heartbeat)

    async def get_recent_for_agent(self, agent_id: str, limit: int = 100) -> Sequence[AgentHeartbeat]:
        """Get recent heartbeats for an agent within this tenant."""
        stmt = (
            select(AgentHeartbeat)
            .where(
                AgentHeartbeat.tenant_id == self.tenant_id,
                AgentHeartbeat.agent_id == agent_id,
            )
            .order_by(AgentHeartbeat.received_at.desc())
            .limit(limit)
        )
        result = await self.session.execute(stmt)
        return result.scalars().all()
