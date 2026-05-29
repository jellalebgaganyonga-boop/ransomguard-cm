"""Base repository with mandatory tenant_id enforcement on every query (CWE-285)."""

from collections.abc import Sequence
from typing import Generic, TypeVar

from sqlalchemy import func, select
from sqlalchemy.ext.asyncio import AsyncSession

from ransomguard_grid.db.base import Base

ModelType = TypeVar("ModelType", bound=Base)


class BaseRepository(Generic[ModelType]):
    """Generic repository enforcing tenant_id on every query.

    SECURITY: tenant_id filter is applied at the base class level.
    Cross-tenant access is impossible if subclasses use these methods.
    """

    model: type[ModelType]

    def __init__(self, session: AsyncSession, tenant_id: str) -> None:
        if not tenant_id:
            raise ValueError("tenant_id is required")
        self.session = session
        self.tenant_id = tenant_id

    async def get_by_id(self, entity_id: str) -> ModelType | None:
        """Get entity by ID, filtered by tenant_id."""
        stmt = select(self.model).where(
            self.model.id == entity_id,  # type: ignore[attr-defined]
            self.model.tenant_id == self.tenant_id,  # type: ignore[attr-defined]
        )
        result = await self.session.execute(stmt)
        return result.scalar_one_or_none()

    async def list_paginated(
        self,
        offset: int = 0,
        limit: int = 100,
        order_by: str = "id",
        order_dir: str = "desc",
    ) -> tuple[Sequence[ModelType], int]:
        """List entities with pagination, filtered by tenant_id."""
        count_stmt = (
            select(func.count())
            .select_from(self.model)
            .where(self.model.tenant_id == self.tenant_id)  # type: ignore[attr-defined]
        )
        count_result = await self.session.execute(count_stmt)
        total = count_result.scalar_one()

        order_col = getattr(self.model, order_by, None)
        if order_col is None:
            raise ValueError(f"Invalid order_by column: {order_by}")
        order_clause = order_col.desc() if order_dir == "desc" else order_col.asc()

        list_stmt = (
            select(self.model)
            .where(self.model.tenant_id == self.tenant_id)  # type: ignore[attr-defined]
            .order_by(order_clause)
            .offset(offset)
            .limit(min(limit, 200))
        )
        list_result = await self.session.execute(list_stmt)
        items = list_result.scalars().all()
        return items, total

    async def add(self, entity: ModelType) -> ModelType:
        """Add entity, validating tenant_id matches repository tenant."""
        if not hasattr(entity, "tenant_id"):
            raise ValueError(f"Entity {type(entity).__name__} missing tenant_id")
        if entity.tenant_id != self.tenant_id:  # type: ignore[attr-defined]
            raise ValueError(
                f"Tenant ID mismatch: entity={entity.tenant_id}, repo={self.tenant_id}"  # type: ignore[attr-defined]
            )
        self.session.add(entity)
        await self.session.flush()
        return entity
