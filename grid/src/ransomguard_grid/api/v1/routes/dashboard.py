"""Dashboard REST API endpoints — JWT authenticated, RBAC enforced, tenant-isolated."""

from datetime import UTC, datetime, timedelta
from uuid import uuid4

from fastapi import APIRouter, Depends, HTTPException, Query
from sqlalchemy import func, select
from sqlalchemy.ext.asyncio import AsyncSession
from sqlalchemy.orm import selectinload

from ransomguard_grid.api.v1.dependencies.jwt_auth import get_current_user, require_roles
from ransomguard_grid.api.v1.schemas.dashboard import (
    AgentItem,
    AlertItem,
    AuditLogItem,
    CommandItem,
    CreateUserRequest,
    IssueCommandRequest,
    MetricsSummary,
    NotificationPreferenceItem,
    PaginatedAgentResponse,
    PaginatedAlertResponse,
    PaginatedAuditLogResponse,
    PaginatedUserActionLogResponse,
    PaginatedUserResponse,
    PaginatedUserWithRolesResponse,
    ProvisionAgentResponse,
    UpdateAlertStatusRequest,
    UpdateNotificationPreferenceRequest,
    UpdateUserRolesRequest,
    UserActionLogItem,
    UserItem,
    UserItemWithRoles,
    UserWithRolesItem,
)
from ransomguard_grid.core.security import hash_password
from ransomguard_grid.db.models.agent import Agent
from ransomguard_grid.db.models.alerts import Alert, AlertStatusChange
from ransomguard_grid.db.models.enums import AgentStatus, AlertStatus, CommandStatus, Severity
from ransomguard_grid.db.models.notifications import NotificationPreference, UserActionLog
from ransomguard_grid.db.models.operations import CommandQueue
from ransomguard_grid.db.models.tenant_user import User
from ransomguard_grid.db.repositories.alert_repository import AlertRepository
from ransomguard_grid.db.repositories.role_repository import UserRoleRepository
from ransomguard_grid.db.session import get_db
from ransomguard_grid.schemas.dashboard.me import MeResponse, TenantContext, UserPreferences

router = APIRouter(prefix="/dashboard", tags=["dashboard"])


# --- ME ---


@router.get("/me", response_model=MeResponse)
async def get_me(
    current_user: "User" = Depends(get_current_user),
    db: AsyncSession = Depends(get_db),
) -> MeResponse:
    """Return the authenticated user's identity, tenant context, roles, and preferences.

    Defense-in-depth: re-queries DB to catch state changes since JWT was issued
    (e.g., user disabled, tenant suspended). No audit log — this is a pure read.
    """
    stmt = (
        select(User)
        .where(User.id == current_user.id)
        .options(selectinload(User.tenant))
    )
    result = await db.execute(stmt)
    user = result.scalar_one_or_none()

    if user is None:
        raise HTTPException(
            status_code=401,
            detail="Utilisateur introuvable",
            headers={"WWW-Authenticate": "Bearer"},
        )

    if not user.is_active:
        raise HTTPException(status_code=403, detail="Compte utilisateur désactivé")

    if user.tenant is None:
        raise HTTPException(status_code=500, detail="Configuration tenant invalide")

    if user.tenant.status != "active":
        raise HTTPException(status_code=403, detail="Tenant inactif ou suspendu")

    role_repo = UserRoleRepository(db)
    role_names = await role_repo.get_user_role_names(user.id)

    preferences = UserPreferences(
        language=getattr(user, "preferred_language", "fr") or "fr",
        timezone=getattr(user, "preferred_timezone", "Africa/Douala") or "Africa/Douala",
    )

    return MeResponse(
        user_id=user.id,
        email=user.email,
        full_name=user.full_name,
        is_active=user.is_active,
        tenant=TenantContext(
            id=user.tenant.id,
            name=user.tenant.name,
            status=user.tenant.status,
        ),
        roles=role_names,
        last_login_at=user.last_login_at,
        preferences=preferences,
    )


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
    await _log_user_action(
        db, user=user, action_type="alert_status_change",
        target_type="alert", target_id=alert_id,
        details={"old_status": str(old_status.value), "new_status": str(body.new_status.value)},
    )
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

    cmd_id = str(uuid4())
    cmd = CommandQueue(
        id=cmd_id, tenant_id=user.tenant_id, agent_id=body.agent_id,
        command_type=body.command_type, parameters_json=body.parameters,
        status=CommandStatus.Pending, issued_by_user_id=user.id, issued_at=datetime.now(UTC),
    )
    db.add(cmd)
    await _log_user_action(
        db, user=user, action_type="command_issued",
        target_type="agent", target_id=body.agent_id,
        details={"command_type": body.command_type, "command_id": cmd_id},
    )
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

    # Grant roles
    if body.roles:
        from ransomguard_grid.db.repositories.role_repository import RoleRepository

        role_repo = RoleRepository(db)
        user_role_repo = UserRoleRepository(db)
        for role_name in body.roles:
            role = await role_repo.get_by_name(role_name)
            if role:
                await user_role_repo.grant_role(new_user.id, role.id, admin.id)

    await _log_user_action(
        db, user=admin, action_type="user_create",
        target_type="user", target_id=new_user.id,
        details={"email": body.email, "roles": body.roles},
    )
    await db.flush()
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
    await _log_user_action(
        db, user=admin, action_type="user_disable",
        target_type="user", target_id=user_id,
        details={"email": target.email},
    )
    await db.flush()
    return UserItem.model_validate(target)


@router.post("/users/{user_id}/enable", response_model=UserItem)
async def enable_user(
    user_id: str,
    admin: User = Depends(require_roles("tenant_admin")),
    db: AsyncSession = Depends(get_db),
) -> UserItem:
    """Reactivate a previously disabled user. Idempotent: returns 200 if already active."""
    from ransomguard_grid.db.repositories.user_repository import UserRepository

    if user_id == admin.id:
        raise HTTPException(422, "Vous ne pouvez pas modifier votre propre statut d'activation")

    repo = UserRepository(db, tenant_id=admin.tenant_id)
    target = await repo.get_by_id(user_id)
    if not target:
        raise HTTPException(404, "User not found")
    target.is_active = True
    await _log_user_action(
        db, user=admin, action_type="user_enable",
        target_type="user", target_id=user_id,
        details={"email": target.email},
    )
    await db.flush()
    return UserItem.model_validate(target)


# --- AUDIT LOGS ---


@router.get("/audit-logs", response_model=PaginatedAuditLogResponse)
async def search_audit_logs(
    agent_id: str | None = None,
    received_after: datetime | None = None,
    received_before: datetime | None = None,
    sequence_after: int | None = None,
    offset: int = Query(0, ge=0),
    limit: int = Query(100, ge=1, le=200),
    user: User = Depends(require_roles("tenant_admin", "read_only_auditor")),
    db: AsyncSession = Depends(get_db),
) -> PaginatedAuditLogResponse:
    """Search audit logs. Read-only for compliance auditors and admins. NOT security analysts."""
    from ransomguard_grid.db.repositories.audit_log_repository import AuditLogRepository

    repo = AuditLogRepository(db, tenant_id=user.tenant_id)
    items, total = await repo.search(
        agent_id=agent_id,
        received_after=received_after,
        received_before=received_before,
        sequence_after=sequence_after,
        offset=offset,
        limit=limit,
    )
    return PaginatedAuditLogResponse(
        items=[AuditLogItem.model_validate(log) for log in items],
        total=total, offset=offset, limit=limit,
    )


# --- USER ROLE UPDATE ---


@router.put("/users/{user_id}/roles", response_model=UserWithRolesItem)
async def update_user_roles(
    user_id: str,
    body: UpdateUserRolesRequest,
    admin: User = Depends(require_roles("tenant_admin")),
    db: AsyncSession = Depends(get_db),
) -> UserWithRolesItem:
    """Replace user's roles. Admin only. Cannot self-demote."""
    from ransomguard_grid.db.repositories.role_repository import RoleRepository, UserRoleRepository
    from ransomguard_grid.db.repositories.user_repository import UserRepository

    user_repo = UserRepository(db, tenant_id=admin.tenant_id)
    target = await user_repo.get_by_id(user_id)
    if not target:
        raise HTTPException(404, "User not found")

    if target.id == admin.id and "tenant_admin" not in body.role_names:
        raise HTTPException(400, "Cannot remove tenant_admin role from yourself")

    role_repo = RoleRepository(db)
    valid_roles = []
    for name in body.role_names:
        role = await role_repo.get_by_name(name)
        if not role:
            raise HTTPException(400, f"Unknown role: {name}")
        valid_roles.append(role)

    user_role_repo = UserRoleRepository(db)
    await user_role_repo.revoke_all_for_user(user_id)
    for role in valid_roles:
        await user_role_repo.grant_role(user_id, role.id, admin.id)

    await db.flush()
    updated_roles = await user_role_repo.get_user_role_names(user_id)
    return UserWithRolesItem(
        id=target.id, email=target.email, full_name=target.full_name,
        is_active=target.is_active, roles=updated_roles,
    )


# --- NOTIFICATION PREFERENCES ---


@router.get("/notifications/preferences", response_model=NotificationPreferenceItem)
async def get_notification_preferences(
    user: User = Depends(get_current_user),
    db: AsyncSession = Depends(get_db),
) -> NotificationPreferenceItem:
    """Get current user's notification preferences. Creates default if absent."""
    stmt = select(NotificationPreference).where(
        NotificationPreference.user_id == user.id,
        NotificationPreference.tenant_id == user.tenant_id,
    )
    result = await db.execute(stmt)
    pref = result.scalar_one_or_none()

    if not pref:
        pref = NotificationPreference(
            id=str(uuid4()),
            user_id=user.id,
            tenant_id=user.tenant_id,
        )
        db.add(pref)
        await db.flush()

    return NotificationPreferenceItem.model_validate(pref)


@router.put("/notifications/preferences", response_model=NotificationPreferenceItem)
async def update_notification_preferences(
    body: UpdateNotificationPreferenceRequest,
    user: User = Depends(get_current_user),
    db: AsyncSession = Depends(get_db),
) -> NotificationPreferenceItem:
    """Update current user's notification preferences."""
    stmt = select(NotificationPreference).where(
        NotificationPreference.user_id == user.id,
        NotificationPreference.tenant_id == user.tenant_id,
    )
    result = await db.execute(stmt)
    pref = result.scalar_one_or_none()

    if not pref:
        pref = NotificationPreference(
            id=str(uuid4()),
            user_id=user.id,
            tenant_id=user.tenant_id,
        )
        db.add(pref)

    pref.email_critical = body.email_critical
    pref.email_high = body.email_high
    pref.email_medium = body.email_medium
    pref.email_low = body.email_low
    pref.updated_at = datetime.now(UTC)
    await db.flush()

    return NotificationPreferenceItem.model_validate(pref)


# --- USER ACTION LOGS ---


async def _log_user_action(
    db: AsyncSession,
    *,
    user: User,
    action_type: str,
    target_type: str | None = None,
    target_id: str | None = None,
    details: dict | None = None,
    ip_address: str | None = None,
) -> None:
    """Record a user action in the audit trail."""
    db.add(UserActionLog(
        id=str(uuid4()),
        tenant_id=user.tenant_id,
        actor_user_id=user.id,
        action_type=action_type,
        target_type=target_type,
        target_id=target_id,
        details_json=details,
        ip_address=ip_address,
        created_at=datetime.now(UTC),
    ))


@router.get("/user-actions", response_model=PaginatedUserActionLogResponse)
async def list_user_actions(
    action_type: str | None = None,
    actor_user_id: str | None = None,
    after: datetime | None = None,
    before: datetime | None = None,
    offset: int = Query(0, ge=0),
    limit: int = Query(50, ge=1, le=200),
    user: User = Depends(require_roles("tenant_admin", "read_only_auditor")),
    db: AsyncSession = Depends(get_db),
) -> PaginatedUserActionLogResponse:
    """Search user action logs. Admin + auditor only."""
    stmt = select(UserActionLog).where(UserActionLog.tenant_id == user.tenant_id)
    count_stmt = select(func.count()).select_from(UserActionLog).where(UserActionLog.tenant_id == user.tenant_id)

    if action_type:
        stmt = stmt.where(UserActionLog.action_type == action_type)
        count_stmt = count_stmt.where(UserActionLog.action_type == action_type)
    if actor_user_id:
        stmt = stmt.where(UserActionLog.actor_user_id == actor_user_id)
        count_stmt = count_stmt.where(UserActionLog.actor_user_id == actor_user_id)
    if after:
        stmt = stmt.where(UserActionLog.created_at >= after)
        count_stmt = count_stmt.where(UserActionLog.created_at >= after)
    if before:
        stmt = stmt.where(UserActionLog.created_at <= before)
        count_stmt = count_stmt.where(UserActionLog.created_at <= before)

    total = (await db.execute(count_stmt)).scalar_one()

    # JOIN with User to get actor_email
    joined_stmt = (
        stmt.add_columns(User.email.label("actor_email"))
        .outerjoin(User, UserActionLog.actor_user_id == User.id)
        .order_by(UserActionLog.created_at.desc())
        .offset(offset)
        .limit(limit)
    )
    items_result = await db.execute(joined_stmt)
    items = []
    for row in items_result:
        log = row[0]
        actor_email = row[1] if len(row) > 1 else None
        item = UserActionLogItem.model_validate(log)
        item.actor_email = actor_email
        items.append(item)
    return PaginatedUserActionLogResponse(items=items, total=total, offset=offset, limit=limit)


# --- AGENT DECOMMISSION ---


@router.post("/agents/{agent_id}/decommission", response_model=AgentItem)
async def decommission_agent(
    agent_id: str,
    user: User = Depends(require_roles("tenant_admin")),
    db: AsyncSession = Depends(get_db),
) -> AgentItem:
    """Decommission an agent (irreversible). Admin only."""
    from ransomguard_grid.db.repositories.agent_repository import AgentRepository

    repo = AgentRepository(db, tenant_id=user.tenant_id)
    agent = await repo.get_by_id(agent_id)
    if not agent:
        raise HTTPException(404, "Agent not found")

    if agent.status == AgentStatus.decommissioned:
        raise HTTPException(400, "Agent already decommissioned")

    agent.status = AgentStatus.decommissioned  # type: ignore[assignment]
    await db.flush()

    await _log_user_action(
        db, user=user, action_type="agent_decommission",
        target_type="agent", target_id=agent_id,
        details={"hostname": agent.hostname},
    )

    return AgentItem.model_validate(agent)


# --- USERS WITH ROLES (fix for GAP-08) ---


@router.get("/users-with-roles", response_model=PaginatedUserWithRolesResponse)
async def list_users_with_roles(
    offset: int = Query(0, ge=0),
    limit: int = Query(50, ge=1, le=200),
    user: User = Depends(require_roles("tenant_admin")),
    db: AsyncSession = Depends(get_db),
) -> PaginatedUserWithRolesResponse:
    """List users with their roles. Fixes GAP-08 (GET /users had no roles)."""
    from ransomguard_grid.db.repositories.user_repository import UserRepository

    repo = UserRepository(db, tenant_id=user.tenant_id)
    items, total = await repo.list_paginated(offset=offset, limit=limit)

    role_repo = UserRoleRepository(db)
    user_items = []
    for u in items:
        roles = await role_repo.get_user_role_names(u.id)
        user_items.append(UserItemWithRoles(
            id=u.id, email=u.email, full_name=u.full_name,
            is_active=u.is_active, last_login_at=u.last_login_at, roles=roles,
        ))

    return PaginatedUserWithRolesResponse(items=user_items, total=total, offset=offset, limit=limit)


# --- AGENT PROVISIONING (OTP generation) ---


@router.post("/agents/provision", response_model=ProvisionAgentResponse)
async def provision_agent(
    user: User = Depends(require_roles("tenant_admin")),
    db: AsyncSession = Depends(get_db),
) -> ProvisionAgentResponse:
    """Generate a one-time enrollment token for a new agent. Admin only.

    The OTP is valid for 30 minutes and can only be used once.
    Give it to the agent's appsettings.json under Agent.Server.EnrollmentOtp.
    """
    from ransomguard_grid.services.enrollment_otp_service import get_shared_otp_service

    otp_service = get_shared_otp_service()
    otp_string = otp_service.generate(tenant_id=user.tenant_id, validity_minutes=30)

    await _log_user_action(
        db, user=user, action_type="agent_provision",
        target_type="otp", target_id=otp_string[:14],
        details={"otp_prefix": otp_string[:14]},
    )

    return ProvisionAgentResponse(otp=otp_string, expires_in_minutes=30)
