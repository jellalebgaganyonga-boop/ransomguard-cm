"""Shared test fixtures for GRID server tests."""

import os
from collections.abc import AsyncGenerator

import pytest
from httpx import ASGITransport, AsyncClient
from sqlalchemy.ext.asyncio import AsyncSession, async_sessionmaker, create_async_engine

from ransomguard_grid.db.base import Base

# Set test environment before importing app
os.environ["GRID_DATABASE_URL"] = "sqlite+aiosqlite://"
os.environ["GRID_JWT_SECRET_KEY"] = "test-secret-key-minimum-32-characters-long!"
os.environ["GRID_ENVIRONMENT"] = "development"
os.environ["GRID_DEBUG"] = "true"


@pytest.fixture
async def db_engine():
    """Create an in-memory SQLite engine for testing."""
    engine = create_async_engine("sqlite+aiosqlite://", echo=False)
    async with engine.begin() as conn:
        await conn.run_sync(Base.metadata.create_all)
    yield engine
    await engine.dispose()


@pytest.fixture
async def db_session(db_engine) -> AsyncGenerator[AsyncSession, None]:
    """Provide a transactional database session for testing."""
    session_factory = async_sessionmaker(bind=db_engine, expire_on_commit=False)
    async with session_factory() as session:
        yield session


@pytest.fixture
async def client() -> AsyncGenerator[AsyncClient, None]:
    """Provide an async HTTP client for testing FastAPI endpoints."""
    from ransomguard_grid.main import app

    transport = ASGITransport(app=app)
    async with AsyncClient(transport=transport, base_url="http://test") as ac:
        yield ac
