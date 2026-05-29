"""User repository with tenant isolation and email lookup."""

from datetime import UTC, datetime

from sqlalchemy import select

from ransomguard_grid.db.models.tenant_user import User
from ransomguard_grid.db.repositories.base_repository import BaseRepository


class UserRepository(BaseRepository[User]):
    """Repository for User entities with tenant isolation."""

    model = User

    async def find_by_email(self, email: str) -> User | None:
        """Find a user by email within this tenant."""
        stmt = select(User).where(
            User.tenant_id == self.tenant_id,
            User.email == email,
        )
        result = await self.session.execute(stmt)
        return result.scalar_one_or_none()

    async def update_last_login(self, user_id: str) -> None:
        """Update the last_login_at timestamp for a user."""
        stmt = select(User).where(User.id == user_id, User.tenant_id == self.tenant_id)
        result = await self.session.execute(stmt)
        user = result.scalar_one_or_none()
        if user:
            user.last_login_at = datetime.now(UTC)
            await self.session.flush()
