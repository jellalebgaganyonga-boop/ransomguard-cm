"""Tests for SQLAlchemy models — imports, constraints, enums."""

from datetime import UTC, datetime
from uuid import uuid4

import pytest
from sqlalchemy.exc import IntegrityError
from sqlalchemy.ext.asyncio import AsyncSession

from ransomguard_grid.db.models import (
    Agent,
    AgentCertificate,
    AgentConfiguration,
    AgentHeartbeat,
    AgentThreatIntelVersion,
    Alert,
    AlertArtifact,
    AlertCorrelation,
    AlertDetail,
    AlertStatusChange,
    AuditLog,
    CommandQueue,
    CommandResponse,
    Role,
    Session,
    SystemLog,
    Tenant,
    ThreatIntelPackage,
    ThreatIntelVersion,
    User,
    UserRole,
)
from ransomguard_grid.db.models.enums import Severity, TenantStatus


def test_all_21_models_import_successfully() -> None:
    """All 21 model classes should be importable."""
    models = [
        Tenant, User, Role, UserRole, Session,
        Agent, AgentCertificate, AgentConfiguration, AgentHeartbeat,
        Alert, AlertDetail, AlertArtifact, AuditLog, AlertCorrelation, AlertStatusChange,
        ThreatIntelVersion, ThreatIntelPackage, AgentThreatIntelVersion,
        CommandQueue, CommandResponse, SystemLog,
    ]
    assert len(models) == 21
    for m in models:
        assert hasattr(m, "__tablename__")


@pytest.mark.asyncio
async def test_tenant_code_uniqueness_enforced(db_session: AsyncSession) -> None:
    """Inserting duplicate tenant code should raise IntegrityError."""
    t1 = Tenant(id=str(uuid4()), name="Hospital A", code="hosp-a", contact_email="a@test.local", status=TenantStatus.active, created_at=datetime.now(UTC))
    t2 = Tenant(id=str(uuid4()), name="Hospital B", code="hosp-a", contact_email="b@test.local", status=TenantStatus.active, created_at=datetime.now(UTC))
    db_session.add(t1)
    await db_session.flush()
    db_session.add(t2)
    with pytest.raises(IntegrityError):
        await db_session.flush()


@pytest.mark.asyncio
async def test_alert_idempotency_constraint(db_session: AsyncSession) -> None:
    """Duplicate (tenant_id, agent_id, client_message_id) should raise IntegrityError."""
    tenant = Tenant(id=str(uuid4()), name="T", code="t1", contact_email="t@t.local", status=TenantStatus.active, created_at=datetime.now(UTC))
    db_session.add(tenant)
    await db_session.flush()
    agent = Agent(id=str(uuid4()), tenant_id=tenant.id, hostname="h", fqdn="h.local", os_version="10", agent_version="1.0", hardware_fingerprint="fp1", created_at=datetime.now(UTC))
    db_session.add(agent)
    await db_session.flush()

    a1 = Alert(id=str(uuid4()), tenant_id=tenant.id, agent_id=agent.id, client_message_id="msg-001", alert_type="USB", severity=Severity.High, detected_at=datetime.now(UTC), summary="test")
    a2 = Alert(id=str(uuid4()), tenant_id=tenant.id, agent_id=agent.id, client_message_id="msg-001", alert_type="USB", severity=Severity.High, detected_at=datetime.now(UTC), summary="test2")
    db_session.add(a1)
    await db_session.flush()
    db_session.add(a2)
    with pytest.raises(IntegrityError):
        await db_session.flush()


@pytest.mark.asyncio
async def test_foreign_key_restrict_prevents_orphan_delete(db_session: AsyncSession) -> None:
    """Deleting a tenant with users should raise IntegrityError (ON DELETE RESTRICT)."""
    tenant = Tenant(id=str(uuid4()), name="T", code="t-del", contact_email="t@t.local", status=TenantStatus.active, created_at=datetime.now(UTC))
    db_session.add(tenant)
    await db_session.flush()
    user = User(id=str(uuid4()), tenant_id=tenant.id, email="u@t.local", hashed_password="x", full_name="Test", created_at=datetime.now(UTC))
    db_session.add(user)
    await db_session.flush()

    await db_session.delete(tenant)
    with pytest.raises(IntegrityError):
        await db_session.flush()


def test_enum_severity_values() -> None:
    """Severity enum should match agent C# values exactly."""
    assert Severity.Low.value == "Low"
    assert Severity.Medium.value == "Medium"
    assert Severity.High.value == "High"
    assert Severity.Critical.value == "Critical"
    assert len(Severity) == 4
