"""Agent heartbeat endpoint — mTLS required."""

from datetime import UTC, datetime

from fastapi import APIRouter, Depends, HTTPException
from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from ransomguard_grid.api.v1.dependencies.auth import get_authenticated_agent
from ransomguard_grid.api.v1.schemas.agent import HeartbeatRequest, HeartbeatResponse
from ransomguard_grid.db.models.agent import Agent
from ransomguard_grid.db.models.enums import AgentStatus, CommandStatus
from ransomguard_grid.db.models.operations import CommandQueue
from ransomguard_grid.db.session import get_db
from ransomguard_grid.services.posture_store import record_heartbeat

router = APIRouter(prefix="/agents/{agent_id}", tags=["heartbeat"])


@router.post("/heartbeat", response_model=HeartbeatResponse)
async def heartbeat(
    agent_id: str,
    body: HeartbeatRequest,
    agent: Agent = Depends(get_authenticated_agent),
    db: AsyncSession = Depends(get_db),
) -> HeartbeatResponse:
    """Record the agent's posture and return pending commands.

    The agent and its tenant come from the authenticated certificate, never from the body; the
    path must name that same agent. Freshness is the server's reception time.
    """
    if agent.id != agent_id:
        raise HTTPException(403, "Agent ID mismatch")

    now = datetime.now(UTC)

    # Re-activate disconnected agents on heartbeat
    if agent.status == AgentStatus.disconnected:
        agent.status = AgentStatus.active
    agent.last_heartbeat_at = now
    await db.flush()

    # Current posture, module states, and a transition row only when a module changes
    # (agent_heartbeats, one row per beat never read back, is gone).
    await record_heartbeat(db, agent, body, now)

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
