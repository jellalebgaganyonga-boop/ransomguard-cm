"""Shared test fixtures for GRID server tests."""

import os
from collections.abc import AsyncGenerator
from dataclasses import dataclass
from datetime import UTC, datetime, timedelta
from uuid import uuid4

import pytest
from cryptography.hazmat.primitives.asymmetric.ed25519 import Ed25519PrivateKey
from cryptography.hazmat.primitives.serialization import Encoding, PublicFormat
from httpx import ASGITransport, AsyncClient
from sqlalchemy.ext.asyncio import AsyncSession, async_sessionmaker, create_async_engine

from ransomguard_grid.db.base import Base

# Set test environment before importing app
os.environ["GRID_DATABASE_URL"] = "sqlite+aiosqlite://"
os.environ["GRID_JWT_SECRET_KEY"] = "test-secret-key-minimum-32-characters-long!"
os.environ["GRID_ENVIRONMENT"] = "development"
os.environ["GRID_DEBUG"] = "true"

# Use a file-based SQLite for test engine so multiple connections share the same DB
import tempfile

_test_db_path = os.path.join(tempfile.gettempdir(), "ransomguard_grid_test.db")
_test_engine = create_async_engine(f"sqlite+aiosqlite:///{_test_db_path}", echo=False)
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


# Module-level test cert + Ed25519 key (reused across all tests)
_TEST_SERIAL, _TEST_CERT_PEM = _make_self_signed_cert()
_TEST_ED25519_PRIVATE = Ed25519PrivateKey.generate()
_TEST_ED25519_PUBLIC_PEM = _TEST_ED25519_PRIVATE.public_key().public_bytes(
    Encoding.PEM, PublicFormat.SubjectPublicKeyInfo
).decode()


@pytest.fixture
def test_cert_serial() -> str:
    """Serial number hex of the test certificate."""
    return _TEST_SERIAL


@pytest.fixture
def test_cert_pem() -> str:
    """PEM string of the test certificate."""
    return _TEST_CERT_PEM


@pytest.fixture
def test_ed25519_private_key() -> Ed25519PrivateKey:
    """Ed25519 private key matching the test agent's stored public key."""
    return _TEST_ED25519_PRIVATE


@pytest.fixture
def test_ed25519_public_pem() -> str:
    """Ed25519 public key PEM matching the test agent."""
    return _TEST_ED25519_PUBLIC_PEM


@dataclass
class AgentFixture:
    """Bundle of test agent data for convenience."""

    tenant_id: str
    agent_id: str
    cert_serial: str
    cert_pem: str
    ed25519_private_key: Ed25519PrivateKey


@pytest.fixture
async def test_agent() -> AgentFixture:
    """Create a fully configured test agent with cert + Ed25519 key in DB.

    Uses its own session from the shared engine to avoid fixture ordering issues.
    """
    from ransomguard_grid.db.models.agent import Agent, AgentCertificate
    from ransomguard_grid.db.models.enums import AgentStatus, TenantStatus
    from ransomguard_grid.db.models.tenant_user import Tenant

    tenant_id = str(uuid4())
    agent_id = str(uuid4())
    now = datetime.now(UTC)

    # Ensure tables exist (can't rely on autouse ordering)
    async with _test_engine.begin() as conn:
        await conn.run_sync(Base.metadata.create_all)

    async with _test_session_factory() as session:
        tenant = Tenant(
            id=tenant_id, name="Test Tenant", code=f"test-{uuid4().hex[:6]}",
            contact_email="test@test.local", status=TenantStatus.active,
        )
        session.add(tenant)

        agent = Agent(
            id=agent_id, tenant_id=tenant_id, hostname="test-host", fqdn="test-host.local",
            os_version="10", agent_version="1.0", hardware_fingerprint=f"fp-{uuid4().hex[:8]}",
            status=AgentStatus.active,
        )
        session.add(agent)

        cert = AgentCertificate(
            id=str(uuid4()), agent_id=agent_id, serial_number=_TEST_SERIAL,
            fingerprint_sha256="f" * 64, not_before=now - timedelta(days=1),
            not_after=now + timedelta(days=365),
            ed25519_public_key_pem=_TEST_ED25519_PUBLIC_PEM,
        )
        session.add(cert)
        await session.commit()

    return AgentFixture(
        tenant_id=tenant_id,
        agent_id=agent_id,
        cert_serial=_TEST_SERIAL,
        cert_pem=_TEST_CERT_PEM,
        ed25519_private_key=_TEST_ED25519_PRIVATE,
    )
