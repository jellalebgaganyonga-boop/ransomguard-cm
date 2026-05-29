"""Shared test fixtures for GRID server tests."""

import os
from collections.abc import AsyncGenerator
from datetime import UTC, datetime, timedelta
from uuid import uuid4

import pytest
from httpx import ASGITransport, AsyncClient
from sqlalchemy.ext.asyncio import AsyncSession, async_sessionmaker, create_async_engine

from ransomguard_grid.db.base import Base

# Set test environment before importing app
os.environ["GRID_DATABASE_URL"] = "sqlite+aiosqlite://"
os.environ["GRID_JWT_SECRET_KEY"] = "test-secret-key-minimum-32-characters-long!"
os.environ["GRID_ENVIRONMENT"] = "development"
os.environ["GRID_DEBUG"] = "true"

# Shared test engine — single in-memory DB for both fixtures and app
_test_engine = create_async_engine("sqlite+aiosqlite://", echo=False)
_test_session_factory = async_sessionmaker(bind=_test_engine, expire_on_commit=False)


@pytest.fixture(autouse=True)
async def _setup_db():
    """Create all tables before each test, drop after."""
    async with _test_engine.begin() as conn:
        await conn.run_sync(Base.metadata.create_all)
    yield
    async with _test_engine.begin() as conn:
        await conn.run_sync(Base.metadata.drop_all)


@pytest.fixture
async def db_engine():
    """Provide the shared test engine."""
    return _test_engine


@pytest.fixture
async def db_session() -> AsyncGenerator[AsyncSession, None]:
    """Provide a database session on the shared test engine."""
    async with _test_session_factory() as session:
        yield session


async def _override_get_db() -> AsyncGenerator[AsyncSession, None]:
    """Override for FastAPI's get_db dependency to use test engine."""
    async with _test_session_factory() as session:
        try:
            yield session
            await session.commit()
        except Exception:
            await session.rollback()
            raise


@pytest.fixture
async def client() -> AsyncGenerator[AsyncClient, None]:
    """Provide an async HTTP client with get_db overridden to test engine."""
    from ransomguard_grid.db.session import get_db
    from ransomguard_grid.main import app

    app.dependency_overrides[get_db] = _override_get_db
    transport = ASGITransport(app=app)
    async with AsyncClient(transport=transport, base_url="http://test") as ac:
        yield ac
    app.dependency_overrides.pop(get_db, None)


def _make_self_signed_cert() -> tuple[str, str]:
    """Generate a self-signed X.509 cert + PEM for testing mTLS."""
    from cryptography import x509
    from cryptography.hazmat.primitives import hashes, serialization
    from cryptography.hazmat.primitives.asymmetric import rsa
    from cryptography.x509.oid import NameOID

    key = rsa.generate_private_key(public_exponent=65537, key_size=2048)
    subject = issuer = x509.Name([x509.NameAttribute(NameOID.COMMON_NAME, "test-agent")])
    cert = (
        x509.CertificateBuilder()
        .subject_name(subject)
        .issuer_name(issuer)
        .public_key(key.public_key())
        .serial_number(x509.random_serial_number())
        .not_valid_before(datetime.now(UTC) - timedelta(days=1))
        .not_valid_after(datetime.now(UTC) + timedelta(days=365))
        .sign(key, hashes.SHA256())
    )
    serial_hex = format(cert.serial_number, "x").upper()
    cert_pem = cert.public_bytes(serialization.Encoding.PEM).decode()
    return serial_hex, cert_pem


_TEST_SERIAL, _TEST_CERT_PEM = _make_self_signed_cert()


@pytest.fixture
def test_cert_serial() -> str:
    """Serial number hex of the test certificate."""
    return _TEST_SERIAL


@pytest.fixture
def test_cert_pem() -> str:
    """PEM string of the test certificate."""
    return _TEST_CERT_PEM
