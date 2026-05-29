"""Alert repository with idempotency lookup via client_message_id."""

from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from ransomguard_grid.db.models.alerts import Alert
from ransomguard_grid.db.repositories.base_repository import BaseRepository


class AlertRepository(BaseRepository[Alert]):
    """Repository for Alert entities with tenant isolation and idempotency."""

    model = Alert

    async def find_by_client_message_id(self, agent_id: str, client_message_id: str) -> Alert | None:
        """Find an existing alert by idempotency key (tenant_id, agent_id, client_message_id)."""
        stmt = select(Alert).where(
            Alert.tenant_id == self.tenant_id,
            Alert.agent_id == agent_id,
            Alert.client_message_id == client_message_id,
        )
        result = await self.session.execute(stmt)
        return result.scalar_one_or_none()
