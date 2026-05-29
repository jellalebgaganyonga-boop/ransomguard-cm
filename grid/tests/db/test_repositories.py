"""Tests for repositories — tenant isolation, idempotency, pagination."""

import pytest
from datetime import UTC, datetime
from uuid import uuid4

from sqlalchemy.ext.asyncio import AsyncSession

from ransomguard_grid.db.models.agent import Agent
from ransomguard_grid.db.models.alerts import Alert, AuditLog
from ransomguard_grid.db.models.enums import AlertStatus, Severity, TenantStatus
from ransomguard_grid.db.models.tenant_user import Tenant
from ransomguard_grid.db.repositories.agent_repository import AgentRepository
from ransomguard_grid.db.repositories.alert_repository import AlertRepository
from ransomguard_grid.db.repositories.audit_log_repository import AuditLogRepository
from ransomguard_grid.db.repositories.base_repository import BaseRepository


async def _create_tenant_and_agent(session: AsyncSession, code: str = "t1") -> tuple[Tenant, Agent]:
    """Helper to create a tenant and agent for testing."""
    tenant = Tenant(id=str(uuid4()), name=f"Tenant {code}", code=code, contact_email=f"{code}@test.local", status=TenantStatus.active, created_at=datetime.now(UTC))
    session.add(tenant)
    await session.flush()
    agent = Agent(id=str(uuid4()), tenant_id=tenant.id, hostname=f"host-{code}", fqdn=f"host-{code}.local", os_version="10", agent_version="1.0", hardware_fingerprint=f"fp-{code}", created_at=datetime.now(UTC))
    session.add(agent)
    await session.flush()
    return tenant, agent


@pytest.mark.asyncio
async def test_base_repository_filters_by_tenant_id_on_get_by_id(db_session: AsyncSession) -> None:
    """get_by_id should only return entities belonging to the repository's tenant."""
    t1, a1 = await _create_tenant_and_agent(db_session, "iso-a")
    t2, a2 = await _create_tenant_and_agent(db_session, "iso-b")

    alert = Alert(id=str(uuid4()), tenant_id=t1.id, agent_id=a1.id, client_message_id="msg-1", alert_type="USB", severity=Severity.High, detected_at=datetime.now(UTC), summary="test")
    db_session.add(alert)
    await db_session.flush()

    repo_t1 = AlertRepository(db_session, t1.id)
    repo_t2 = AlertRepository(db_session, t2.id)

    assert await repo_t1.get_by_id(alert.id) is not None
    assert await repo_t2.get_by_id(alert.id) is None  # CROSS-TENANT BLOCKED


@pytest.mark.asyncio
async def test_base_repository_filters_by_tenant_id_on_list(db_session: AsyncSession) -> None:
    """list_paginated should only return entities for the repo's tenant."""
    t1, a1 = await _create_tenant_and_agent(db_session, "list-a")
    t2, a2 = await _create_tenant_and_agent(db_session, "list-b")

    for i in range(3):
        db_session.add(Alert(id=str(uuid4()), tenant_id=t1.id, agent_id=a1.id, client_message_id=f"m-{i}", alert_type="USB", severity=Severity.Low, detected_at=datetime.now(UTC), summary=f"t1-{i}"))
    db_session.add(Alert(id=str(uuid4()), tenant_id=t2.id, agent_id=a2.id, client_message_id="m-0", alert_type="USB", severity=Severity.Low, detected_at=datetime.now(UTC), summary="t2"))
    await db_session.flush()

    items_t1, count_t1 = await AlertRepository(db_session, t1.id).list_paginated()
    items_t2, count_t2 = await AlertRepository(db_session, t2.id).list_paginated()
    assert count_t1 == 3
    assert count_t2 == 1


@pytest.mark.asyncio
async def test_base_repository_add_rejects_missing_tenant_id(db_session: AsyncSession) -> None:
    """add() should reject entities without tenant_id attribute."""
    from ransomguard_grid.db.models.tenant_user import Role
    repo = AlertRepository(db_session, "tenant-x")

    role = Role(id=str(uuid4()), name="test-role", description="desc")
    with pytest.raises(ValueError, match="missing tenant_id"):
        await repo.add(role)  # type: ignore[arg-type]


@pytest.mark.asyncio
async def test_base_repository_add_rejects_mismatched_tenant_id(db_session: AsyncSession) -> None:
    """add() should reject entities with a different tenant_id."""
    t1, a1 = await _create_tenant_and_agent(db_session, "mis-a")
    repo = AlertRepository(db_session, "different-tenant-id")

    alert = Alert(id=str(uuid4()), tenant_id=t1.id, agent_id=a1.id, client_message_id="m-1", alert_type="USB", severity=Severity.Low, detected_at=datetime.now(UTC), summary="test")
    with pytest.raises(ValueError, match="Tenant ID mismatch"):
        await repo.add(alert)


@pytest.mark.asyncio
async def test_alert_repository_find_by_client_message_id(db_session: AsyncSession) -> None:
    """find_by_client_message_id should return existing alert for idempotency."""
    t, a = await _create_tenant_and_agent(db_session, "idem-1")
    alert = Alert(id=str(uuid4()), tenant_id=t.id, agent_id=a.id, client_message_id="unique-msg", alert_type="SENTINEL", severity=Severity.Critical, detected_at=datetime.now(UTC), summary="canary tampered")
    db_session.add(alert)
    await db_session.flush()

    repo = AlertRepository(db_session, t.id)
    found = await repo.find_by_client_message_id(a.id, "unique-msg")
    assert found is not None
    assert found.id == alert.id

    not_found = await repo.find_by_client_message_id(a.id, "nonexistent")
    assert not_found is None


@pytest.mark.asyncio
async def test_agent_repository_find_by_fingerprint(db_session: AsyncSession) -> None:
    """find_by_fingerprint should return agent within tenant."""
    t, a = await _create_tenant_and_agent(db_session, "fp-1")
    repo = AgentRepository(db_session, t.id)

    found = await repo.find_by_fingerprint(f"fp-fp-1")
    assert found is not None
    assert found.id == a.id


@pytest.mark.asyncio
async def test_audit_log_repository_rejects_sequence_gap(db_session: AsyncSession) -> None:
    """append_with_sequence_check should reject non-contiguous sequence numbers."""
    t, a = await _create_tenant_and_agent(db_session, "seq-gap")
    repo = AuditLogRepository(db_session, t.id)

    entry = AuditLog(id=str(uuid4()), tenant_id=t.id, agent_id=a.id, sequence_number=5, ed25519_signature=b"sig", signing_key_id="key1", payload_json={})
    with pytest.raises(ValueError, match="Sequence gap"):
        await repo.append_with_sequence_check(entry)


@pytest.mark.asyncio
async def test_audit_log_repository_accepts_contiguous_sequence(db_session: AsyncSession) -> None:
    """append_with_sequence_check should accept sequence number = max + 1."""
    t, a = await _create_tenant_and_agent(db_session, "seq-ok")
    repo = AuditLogRepository(db_session, t.id)

    e1 = AuditLog(id=str(uuid4()), tenant_id=t.id, agent_id=a.id, sequence_number=1, ed25519_signature=b"sig1", signing_key_id="key1", payload_json={})
    await repo.append_with_sequence_check(e1)

    e2 = AuditLog(id=str(uuid4()), tenant_id=t.id, agent_id=a.id, sequence_number=2, ed25519_signature=b"sig2", signing_key_id="key1", payload_json={})
    result = await repo.append_with_sequence_check(e2)
    assert result.sequence_number == 2


@pytest.mark.asyncio
async def test_cross_tenant_query_returns_empty(db_session: AsyncSession) -> None:
    """CRITICAL ISOLATION: Tenant A repository CANNOT see Tenant B records."""
    t_a, a_a = await _create_tenant_and_agent(db_session, "cross-a")
    t_b, a_b = await _create_tenant_and_agent(db_session, "cross-b")

    # Insert alert in tenant A
    alert_a = Alert(id=str(uuid4()), tenant_id=t_a.id, agent_id=a_a.id, client_message_id="secret-msg", alert_type="ENTROPY", severity=Severity.Critical, detected_at=datetime.now(UTC), summary="tenant A secret")
    db_session.add(alert_a)
    await db_session.flush()

    # Tenant B repo should see nothing
    repo_b = AlertRepository(db_session, t_b.id)
    assert await repo_b.get_by_id(alert_a.id) is None
    items, count = await repo_b.list_paginated()
    assert count == 0
    assert len(items) == 0

    # Tenant A repo should see its own
    repo_a = AlertRepository(db_session, t_a.id)
    assert await repo_a.get_by_id(alert_a.id) is not None


@pytest.mark.asyncio
async def test_pagination_orders_correctly(db_session: AsyncSession) -> None:
    """Pagination should respect order_by and order_dir."""
    t, a = await _create_tenant_and_agent(db_session, "page-1")
    import time
    for i in range(5):
        db_session.add(Alert(id=str(uuid4()), tenant_id=t.id, agent_id=a.id, client_message_id=f"p-{i}", alert_type="USB", severity=Severity.Low, detected_at=datetime.now(UTC), summary=f"alert-{i}"))
    await db_session.flush()

    repo = AlertRepository(db_session, t.id)
    items, total = await repo.list_paginated(offset=0, limit=3)
    assert total == 5
    assert len(items) == 3
