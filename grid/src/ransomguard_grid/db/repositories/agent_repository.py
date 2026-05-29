"""Agent repository with fingerprint lookup."""

from sqlalchemy import select

from ransomguard_grid.db.models.agent import Agent
from ransomguard_grid.db.repositories.base_repository import BaseRepository


class AgentRepository(BaseRepository[Agent]):
    """Repository for Agent entities with tenant isolation."""

    model = Agent

    async def find_by_fingerprint(self, fingerprint: str) -> Agent | None:
        """Find an agent by hardware fingerprint within the tenant."""
        stmt = select(Agent).where(
            Agent.tenant_id == self.tenant_id,
            Agent.hardware_fingerprint == fingerprint,
        )
        result = await self.session.execute(stmt)
        return result.scalar_one_or_none()
