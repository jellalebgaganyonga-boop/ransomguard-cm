"""Agent heartbeat endpoint — mTLS required."""

from datetime import UTC, datetime
from uuid import uuid4

from fastapi import APIRouter, Depends, HTTPException
from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from ransomguard_grid.api.v1.dependencies.auth import get_authenticated_agent
from ransomguard_grid.api.v1.schemas.agent import HeartbeatRequest, HeartbeatResponse
from ransomguard_grid.db.models.agent import Agent, AgentHeartbeat
from ransomguard_grid.db.models.enums import CommandStatus
from ransomguard_grid.db.models.operations import CommandQueue
from ransomguard_grid.db.repositories.agent_heartbeat_repository import AgentHeartbeatRepository
from ransomguard_grid.db.session import get_db

router = APIRouter(prefix="/agents/{agent_id}", tags=["heartbeat"])


@router.post("/heartbeat", response_model=HeartbeatResponse)
async def heartbeat(
    agent_id: str,
    body: HeartbeatRequest,
    agent: Agent = Depends(get_authenticated_agent),
    db: AsyncSession = Depends(get_db),
) -> HeartbeatResponse:
    """Record agent heartbeat and return pending commands."""
    if agent.id != agent_id:
        raise HTTPException(403, "Agent ID mismatch")

    now = datetime.now(UTC)

    # Update agent last_heartbeat_at
    agent.last_heartbeat_at = now
    await db.flush()

    # Persist heartbeat record
    hb_repo = AgentHeartbeatRepository(db, agent.tenant_id)
    await hb_repo.append(AgentHeartbeat(
        id=str(uuid4()),
        tenant_id=agent.tenant_id,
        agent_id=agent.id,
        received_at=now,
        agent_uptime_seconds=body.agent_uptime_seconds,
        threat_intel_version=body.threat_intel_version,
        modules_status_json=body.modules_status,
    ))

    # Check for pending commands
    pending_stmt = (
        select(CommandQueue.id)
        .where(
            CommandQueue.tenant_id == agent.tenant_id,
            CommandQueue.agent_id == agent.id,
            CommandQueue.status == CommandStatus.Pending,
        )
        .limit(20)
    )
    pending_result = await db.execute(pending_stmt)
    pending_ids = [row[0] for row in pending_result]

    return HeartbeatResponse(
        next_heartbeat_in_seconds=60,
        pending_commands=pending_ids,
        server_time=now,
    )
