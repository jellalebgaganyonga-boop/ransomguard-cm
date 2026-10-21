"""Agent commands (plan de finition, J1 § 1.3): what the console queued, fetched and answered by the agent.

Partitioned like the alerts: the agent is the authenticated one (its certificate in mtls mode), a
path id that is not its own is 403, and a command of another agent or another tenant is 404 --
exactly as one that does not exist.
"""

from datetime import UTC, datetime
from uuid import uuid4

import structlog
from fastapi import APIRouter, Depends, HTTPException
from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from ransomguard_grid.api.v1.dependencies.auth import get_authenticated_agent
from ransomguard_grid.api.v1.schemas.agent import (
    AgentCommandItem,
    AgentCommandsResponse,
    CommandResultRequest,
    CommandResultResponse,
)
from ransomguard_grid.db.models.agent import Agent
from ransomguard_grid.db.models.enums import CommandStatus, LogLevel
from ransomguard_grid.db.models.operations import CommandQueue, CommandResponse, SystemLog
from ransomguard_grid.db.session import get_db

logger = structlog.get_logger(__name__)
router = APIRouter(prefix="/agents/{agent_id}", tags=["commands"])

COMPONENT = "agent-command"
# Fetched again until answered: an agent that died between the fetch and the execution asks again.
OPEN_STATES = (CommandStatus.Pending, CommandStatus.Dispatched)
FINAL_STATES = (CommandStatus.Completed, CommandStatus.Failed)


@router.get("/commands", response_model=AgentCommandsResponse)
async def fetch_commands(
    agent_id: str,
    agent: Agent = Depends(get_authenticated_agent),
    db: AsyncSession = Depends(get_db),
) -> AgentCommandsResponse:
    """The open commands of this agent, oldest first; Pending ones become Dispatched."""
    if agent.id != agent_id:
        raise HTTPException(403, "Agent ID mismatch")

    rows = (await db.execute(
        select(CommandQueue)
        .where(
            CommandQueue.tenant_id == agent.tenant_id,
            CommandQueue.agent_id == agent.id,
            CommandQueue.status.in_(OPEN_STATES),
        )
        .order_by(CommandQueue.issued_at)
        .limit(20)
    )).scalars().all()

    now = datetime.now(UTC)
    for command in rows:
        if command.status == CommandStatus.Pending:
            command.status = CommandStatus.Dispatched
            command.dispatched_at = now
    await db.flush()

    return AgentCommandsResponse(commands=[
        AgentCommandItem(
            id=command.id,
            command_type=command.command_type,
            parameters=dict(command.parameters_json or {}),
            issued_at=command.issued_at,
        )
        for command in rows
    ])


@router.post("/commands/{command_id}/result", response_model=CommandResultResponse)
async def report_result(
    agent_id: str,
    command_id: str,
    body: CommandResultRequest,
    agent: Agent = Depends(get_authenticated_agent),
    db: AsyncSession = Depends(get_db),
) -> CommandResultResponse:
    """What the agent did with a command: executed, or not, and why. Final once reported."""
    if agent.id != agent_id:
        raise HTTPException(403, "Agent ID mismatch")

    command = (await db.execute(select(CommandQueue).where(
        CommandQueue.id == command_id,
        CommandQueue.tenant_id == agent.tenant_id,
        CommandQueue.agent_id == agent.id,
    ))).scalar_one_or_none()
    if command is None:
        raise HTTPException(404, "Command not found")

    if command.status in FINAL_STATES:
        return CommandResultResponse(command_id=command.id, status=command.status.value, result="unchanged")

    now = datetime.now(UTC)
    db.add(CommandResponse(
        id=str(uuid4()), tenant_id=agent.tenant_id, command_id=command.id,
        response_payload_json=dict(body.payload), received_at=now, success=body.success,
    ))
    command.status = CommandStatus.Completed if body.success else CommandStatus.Failed
    command.completed_at = now

    event = "CommandExecuted" if body.success else "CommandNotExecuted"
    reason = body.payload.get("reason")
    db.add(SystemLog(
        id=str(uuid4()),
        tenant_id=agent.tenant_id,
        level=LogLevel.Info if body.success else LogLevel.Warning,
        component=COMPONENT,
        message=f"{event}: {command.command_type} {command.id}" + (f" ({reason})" if reason else ""),
        context_json={
            "event": event, "command_id": command.id, "command_type": command.command_type,
            "agent_id": agent.id, "reason": reason if isinstance(reason, str) else None,
        },
        created_at=now,
    ))
    await db.flush()
    logger.info(event, command_id=command.id, command_type=command.command_type, agent_id=agent.id, reason=reason)
    return CommandResultResponse(command_id=command.id, status=command.status.value, result="applied")
