"""Tests for database session management."""

import pytest
from sqlalchemy import text
from sqlalchemy.ext.asyncio import AsyncSession


@pytest.mark.asyncio
async def test_db_session_creates_and_closes(db_session: AsyncSession) -> None:
    """DB session should execute a query and close without error."""
    result = await db_session.execute(text("SELECT 1"))
    assert result.scalar() == 1


@pytest.mark.asyncio
async def test_transaction_rollback_on_exception(db_engine) -> None:
    """Session should rollback on exception."""
    from sqlalchemy.ext.asyncio import async_sessionmaker

    session_factory = async_sessionmaker(bind=db_engine, expire_on_commit=False)

    with pytest.raises(ValueError):
        async with session_factory() as session:
            await session.execute(text("SELECT 1"))
            raise ValueError("Intentional error")

    # Session should be usable after rollback
    async with session_factory() as session:
        result = await session.execute(text("SELECT 1"))
        assert result.scalar() == 1


@pytest.mark.asyncio
async def test_base_metadata_has_naming_convention() -> None:
    """Base metadata should have the MySQL naming convention configured."""
    from ransomguard_grid.db.base import Base

    nc = Base.metadata.naming_convention
    assert "ix" in nc
    assert "uq" in nc
    assert "fk" in nc
    assert "pk" in nc
