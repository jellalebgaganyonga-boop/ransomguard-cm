"""Agent repository with fingerprint lookup."""

from sqlalchemy import select

from ransomguard_grid.db.models.agent import Agent
from ransomguard_grid.db.models.enums import AgentStatus
from ransomguard_grid.db.repositories.base_repository import BaseRepository


class AgentRepository(BaseRepository[Agent]):
    """Repository for Agent entities with tenant isolation."""

    model = Agent

    async def find_by_fingerprint(self, fingerprint: str) -> Agent | None:
        """Find the agent currently holding this hardware fingerprint, in this tenant.

        A decommissioned agent does not hold its fingerprint any more. Enrolment refuses a
        fingerprint this returns (409), so counting decommissioned agents here meant a
        workstation could be enrolled exactly once in its life: reinstall the agent, rebuild
        the machine, or decommission it on purpose, and it could never come back -- the console
        offered a decommission button and no way to undo what it caused. Observed on the bench
        on 2026-10-25: PC-LABO was reinstalled and the GRID answered 409 to every attempt.

        Decommissioning is now the supported way to let a workstation enrol again, which is what
        the word means.
        """
        stmt = select(Agent).where(
            Agent.tenant_id == self.tenant_id,
            Agent.hardware_fingerprint == fingerprint,
            Agent.status != AgentStatus.decommissioned,
        )
        result = await self.session.execute(stmt)
        return result.scalar_one_or_none()
