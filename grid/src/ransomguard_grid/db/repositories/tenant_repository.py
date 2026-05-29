"""Tenant repository — system-level, no tenant_id filter."""

from collections.abc import Sequence

from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from ransomguard_grid.db.models.tenant_user import Tenant


class TenantRepository:
    """System-level repository for tenants. Does NOT inherit BaseRepository."""

    def __init__(self, session: AsyncSession) -> None:
        self.session = session

    async def get_by_id(self, tenant_id: str) -> Tenant | None:
        """Get a tenant by ID."""
        result = await self.session.execute(select(Tenant).where(Tenant.id == tenant_id))
        return result.scalar_one_or_none()

    async def get_by_code(self, code: str) -> Tenant | None:
        """Get a tenant by unique code."""
        result = await self.session.execute(select(Tenant).where(Tenant.code == code))
        return result.scalar_one_or_none()

    async def list_all(self) -> Sequence[Tenant]:
        """List all tenants."""
        result = await self.session.execute(select(Tenant).order_by(Tenant.name))
        return result.scalars().all()

    async def add(self, tenant: Tenant) -> Tenant:
        """Add a new tenant."""
        self.session.add(tenant)
        await self.session.flush()
        return tenant
