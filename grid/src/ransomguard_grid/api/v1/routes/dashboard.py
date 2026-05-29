"""Dashboard REST API endpoints — JWT authenticated, RBAC enforced, tenant-isolated."""

from datetime import UTC, datetime, timedelta
from uuid import uuid4

from fastapi import APIRouter, Depends, HTTPException, Query
from sqlalchemy import func, select
from sqlalchemy.ext.asyncio import AsyncSession

from ransomguard_grid.api.v1.dependencies.jwt_auth import get_current_user, require_roles
from ransomguard_grid.api.v1.schemas.dashboard import (
    AgentItem,
    AlertItem,
    CommandItem,
    CreateUserRequest,
    IssueCommandRequest,
    MetricsSummary,
    PaginatedAgentResponse,
    PaginatedAlertResponse,
    PaginatedUserResponse,
    UpdateAlertStatusRequest,
    UserItem,
)
from ransomguard_grid.core.security import hash_password
from ransomguard_grid.db.models.agent import Agent
from ransomguard_grid.db.models.alerts import Alert, AlertStatusChange
from ransomguard_grid.db.models.enums import AlertStatus, CommandStatus, Severity
from ransomguard_grid.db.models.operations import CommandQueue
from ransomguard_grid.db.models.tenant_user import User
from ransomguard_grid.db.repositories.alert_repository import AlertRepository
from ransomguard_grid.db.repositories.role_repository import UserRoleRepository
from ransomguard_grid.db.session import get_db

router = APIRouter(prefix="/dashboard", tags=["dashboard"])

# --- ALERTS ---


@router.get("/alerts", response_model=PaginatedAlertResponse)
async def list_alerts(
    severity: Severity | None = None,
    status: AlertStatus | None = None,
    agent_id: str | None = None,
    mitre_technique: str | None = None,
    detected_after: datetime | None = None,
    detected_before: datetime | None = None,
    offset: int = Query(0, ge=0),
    limit: int = Query(50, ge=1, le=200),
    user: User = Depends(require_roles("tenant_admin", "security_analyst", "read_only_auditor")),
    db: AsyncSession = Depends(get_db),
) -> PaginatedAlertResponse:
    """List alerts with filters. Tenant from JWT."""
    stmt = select(Alert).where(Alert.tenant_id == user.tenant_id)
    count_stmt = select(func.count()).select_from(Alert).where(Alert.tenant_id == user.tenant_id)

    if severity:
        stmt = stmt.where(Alert.severity == severity)
        count_stmt = count_stmt.where(Alert.severity == severity)
    if status:
        stmt = stmt.where(Alert.status == status)
        count_stmt = count_stmt.where(Alert.status == status)
    if agent_id:
        stmt = stmt.where(Alert.agent_id == agent_id)
        count_stmt = count_stmt.where(Alert.agent_id == agent_id)
    if mitre_technique:
        stmt = stmt.where(Alert.mitre_technique_id == mitre_technique)
        count_stmt = count_stmt.where(Alert.mitre_technique_id == mitre_technique)
    if detected_after:
        stmt = stmt.where(Alert.detected_at >= detected_after)
        count_stmt = count_stmt.where(Alert.detected_at >= detected_after)
    if detected_before:
        stmt = stmt.where(Alert.detected_at <= detected_before)
        count_stmt = count_stmt.where(Alert.detected_at <= detected_before)

    total = (await db.execute(count_stmt)).scalar_one()
    items_result = await db.execute(stmt.order_by(Alert.detected_at.desc()).offset(offset).limit(limit))
    items = [AlertItem.model_validate(a) for a in items_result.scalars().all()]

    return PaginatedAlertResponse(items=items, total=total, offset=offset, limit=limit)


@router.get("/alerts/{alert_id}", response_model=AlertItem)
async def get_alert(
    alert_id: str,
    user: User = Depends(require_roles("tenant_admin", "security_analyst", "read_only_auditor")),
    db: AsyncSession = Depends(get_db),
) -> AlertItem:
    """Get alert by ID. Returns 404 on cross-tenant (not 403)."""
    repo = AlertRepository(db, tenant_id=user.tenant_id)
    alert = await repo.get_by_id(alert_id)
    if not alert:
        raise HTTPException(404, "Alert not found")
    return AlertItem.model_validate(alert)


@router.post("/alerts/{alert_id}/status", response_model=AlertItem)
async def update_alert_status(
    alert_id: str,
    body: UpdateAlertStatusRequest,
    user: User = Depends(require_roles("tenant_admin", "security_analyst")),
    db: AsyncSession = Depends(get_db),
) -> AlertItem:
    """Update alert status with audit trail."""
    repo = AlertRepository(db, tenant_id=user.tenant_id)
    alert = await repo.get_by_id(alert_id)
    if not alert:
        raise HTTPException(404, "Alert not found")

    old_status = alert.status
    alert.status = body.new_status  # type: ignore[assignment]

    db.add(AlertStatusChange(
        id=str(uuid4()), tenant_id=user.tenant_id, alert_id=alert_id,
        old_status=old_status, new_status=body.new_status,
        changed_by_user_id=user.id, justification=body.justification,
        changed_at=datetime.now(UTC),
    ))
    await db.flush()
    return AlertItem.model_validate(alert)


# --- AGENTS ---


@router.get("/agents", response_model=PaginatedAgentResponse)
async def list_agents(
    status: str | None = None,
    offset: int = Query(0, ge=0),
    limit: int = Query(50, ge=1, le=200),
    user: User = Depends(require_roles("tenant_admin", "security_analyst", "read_only_auditor")),
    db: AsyncSession = Depends(get_db),
) -> PaginatedAgentResponse:
    """List agents in tenant."""
    stmt = select(Agent).where(Agent.tenant_id == user.tenant_id)
    count_stmt = select(func.count()).select_from(Agent).where(Agent.tenant_id == user.tenant_id)

    if status:
        stmt = stmt.where(Agent.status == status)
        count_stmt = count_stmt.where(Agent.status == status)

    total = (await db.execute(count_stmt)).scalar_one()
    items_result = await db.execute(stmt.offset(offset).limit(limit))
    items = [AgentItem.model_validate(a) for a in items_result.scalars().all()]
    return PaginatedAgentResponse(items=items, total=total, offset=offset, limit=limit)


@router.get("/agents/{agent_id}", response_model=AgentItem)
async def get_agent(
    agent_id: str,
    user: User = Depends(require_roles("tenant_admin", "security_analyst", "read_only_auditor")),
    db: AsyncSession = Depends(get_db),
) -> AgentItem:
    """Get agent by ID. 404 on cross-tenant."""
    from ransomguard_grid.db.repositories.agent_repository import AgentRepository

    repo = AgentRepository(db, tenant_id=user.tenant_id)
    agent = await repo.get_by_id(agent_id)
    if not agent:
        raise HTTPException(404, "Agent not found")
    return AgentItem.model_validate(agent)


# --- METRICS ---


@router.get("/metrics/summary", response_model=MetricsSummary)
async def get_metrics_summary(
    user: User = Depends(require_roles("tenant_admin", "security_analyst", "read_only_auditor")),
    db: AsyncSession = Depends(get_db),
) -> MetricsSummary:
    """Top KPIs for dashboard."""
    tid = user.tenant_id
    now = datetime.now(UTC)
    day_ago = now - timedelta(hours=24)

    total_agents = (await db.execute(
        select(func.count()).select_from(Agent).where(Agent.tenant_id == tid)
    )).scalar_one()
    active_agents = (await db.execute(
        select(func.count()).select_from(Agent).where(Agent.tenant_id == tid, Agent.status == "active")
    )).scalar_one()
    alerts_24h = (await db.execute(
        select(func.count()).select_from(Alert).where(Alert.tenant_id == tid, Alert.detected_at >= day_ago)
    )).scalar_one()
    critical_24h = (await db.execute(
        select(func.count()).select_from(Alert).where(
            Alert.tenant_id == tid, Alert.detected_at >= day_ago, Alert.severity == Severity.Critical,
        )
    )).scalar_one()

    return MetricsSummary(
        total_agents=total_agents, active_agents=active_agents,
        alerts_24h=alerts_24h, critical_alerts_24h=critical_24h,
    )


# --- COMMANDS ---


@router.post("/commands", response_model=CommandItem)
async def issue_command(
    body: IssueCommandRequest,
    user: User = Depends(require_roles("tenant_admin")),
    db: AsyncSession = Depends(get_db),
) -> CommandItem:
    """Issue command to agent (queued for next heartbeat)."""
    from ransomguard_grid.db.repositories.agent_repository import AgentRepository

    agent_repo = AgentRepository(db, tenant_id=user.tenant_id)
    agent = await agent_repo.get_by_id(body.agent_id)
    if not agent:
        raise HTTPException(404, "Agent not found")

    cmd = CommandQueue(
        id=str(uuid4()), tenant_id=user.tenant_id, agent_id=body.agent_id,
        command_type=body.command_type, parameters_json=body.parameters,
        status=CommandStatus.Pending, issued_by_user_id=user.id, issued_at=datetime.now(UTC),
    )
    db.add(cmd)
    await db.flush()
    return CommandItem.model_validate(cmd)


# --- USERS ---


@router.get("/users", response_model=PaginatedUserResponse)
async def list_users(
    offset: int = Query(0, ge=0),
    limit: int = Query(50, ge=1, le=200),
    user: User = Depends(require_roles("tenant_admin")),
    db: AsyncSession = Depends(get_db),
) -> PaginatedUserResponse:
    """List users in tenant (admin only)."""
    from ransomguard_grid.db.repositories.user_repository import UserRepository

    repo = UserRepository(db, tenant_id=user.tenant_id)
    items, total = await repo.list_paginated(offset=offset, limit=limit)
    return PaginatedUserResponse(
        items=[UserItem.model_validate(u) for u in items],
        total=total, offset=offset, limit=limit,
    )


@router.post("/users", response_model=UserItem, status_code=201)
async def create_user(
    body: CreateUserRequest,
    admin: User = Depends(require_roles("tenant_admin")),
    db: AsyncSession = Depends(get_db),
) -> UserItem:
    """Create user in admin's tenant."""
    new_user = User(
        id=str(uuid4()), tenant_id=admin.tenant_id,
        email=body.email, hashed_password=hash_password(body.password),
        full_name=body.full_name, is_active=True, created_at=datetime.now(UTC),
    )
    db.add(new_user)
    await db.flush()

    # Grant roles
    if body.roles:
        from ransomguard_grid.db.repositories.role_repository import RoleRepository

        role_repo = RoleRepository(db)
        user_role_repo = UserRoleRepository(db)
        for role_name in body.roles:
            role = await role_repo.get_by_name(role_name)
            if role:
                await user_role_repo.grant_role(new_user.id, role.id, admin.id)

    return UserItem.model_validate(new_user)


@router.post("/users/{user_id}/disable", response_model=UserItem)
async def disable_user(
    user_id: str,
    admin: User = Depends(require_roles("tenant_admin")),
    db: AsyncSession = Depends(get_db),
) -> UserItem:
    """Disable a user."""
    from ransomguard_grid.db.repositories.user_repository import UserRepository

    repo = UserRepository(db, tenant_id=admin.tenant_id)
    target = await repo.get_by_id(user_id)
    if not target:
        raise HTTPException(404, "User not found")
    target.is_active = False
    await db.flush()
    return UserItem.model_validate(target)
