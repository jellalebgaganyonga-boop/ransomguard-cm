"""Role and UserRole repositories — system-level shared across tenants."""

from collections.abc import Sequence

from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from ransomguard_grid.db.models.tenant_user import Role, UserRole


class RoleRepository:
    """System-level role repository (no tenant filter)."""

    def __init__(self, session: AsyncSession) -> None:
        self.session = session

    async def get_by_name(self, name: str) -> Role | None:
        """Find a role by name."""
        result = await self.session.execute(select(Role).where(Role.name == name))
        return result.scalar_one_or_none()

    async def list_all(self) -> Sequence[Role]:
        """List all system roles."""
        result = await self.session.execute(select(Role).order_by(Role.name))
        return result.scalars().all()


class UserRoleRepository:
    """Repository for user-role associations."""

    def __init__(self, session: AsyncSession) -> None:
        self.session = session

    async def get_user_role_names(self, user_id: str) -> list[str]:
        """Get list of role names for a user."""
        stmt = (
            select(Role.name)
            .join(UserRole, UserRole.role_id == Role.id)
            .where(UserRole.user_id == user_id)
        )
        result = await self.session.execute(stmt)
        return list(result.scalars().all())

    async def grant_role(self, user_id: str, role_id: str, granted_by: str) -> None:
        """Grant a role to a user."""
        from datetime import UTC, datetime
        self.session.add(UserRole(
            user_id=user_id, role_id=role_id,
            granted_at=datetime.now(UTC), granted_by_user_id=granted_by,
        ))
        await self.session.flush()
