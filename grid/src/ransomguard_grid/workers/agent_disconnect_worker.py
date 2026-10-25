"""Background worker: marks agents 'disconnected' when their heartbeat times out, and purges
module transitions older than the retention window once a day."""

import asyncio
from datetime import UTC, datetime, timedelta
from typing import Any, cast

from sqlalchemy import CursorResult, delete, update
from sqlalchemy.ext.asyncio import AsyncSession, async_sessionmaker

from ransomguard_grid.core.config import get_settings
from ransomguard_grid.core.logging import get_logger
from ransomguard_grid.db.models.agent import Agent, AgentPostureChange
from ransomguard_grid.db.models.enums import AgentStatus

logger = get_logger("agent_disconnect_worker")


class AgentDisconnectWorker:
    """Periodically checks for agents whose last heartbeat exceeds the timeout threshold."""

    def __init__(
        self,
        session_factory: async_sessionmaker[AsyncSession],
        check_interval_seconds: int = 60,
    ) -> None:
        self._session_factory = session_factory
        self._check_interval = check_interval_seconds
        self._task: asyncio.Task[None] | None = None
        self._settings = get_settings()
        # In memory: after a restart the purge runs again on the first pass. Harmless, the
        # DELETE is idempotent.
        self._last_purge_at: datetime | None = None

    async def start(self) -> None:
        if self._task is not None:
            return
        self._task = asyncio.create_task(self._run_loop())
        logger.info(
            "Agent disconnect worker started",
            timeout_minutes=self._settings.agent_disconnect_timeout_minutes,
        )

    async def stop(self) -> None:
        if self._task is not None:
            self._task.cancel()
            try:
                await self._task
            except asyncio.CancelledError:
                pass
            self._task = None
            logger.info("Agent disconnect worker stopped")

    async def _run_loop(self) -> None:
        while True:
            try:
                await self._check_disconnected()
            except Exception:
                logger.error("Agent disconnect check failed", exc_info=True)
            try:
                await self.purge_posture_history()
            except Exception:
                logger.error("Agent posture history purge failed", exc_info=True)
            await asyncio.sleep(self._check_interval)

    async def _check_disconnected(self) -> None:
        """Mark active agents as disconnected if heartbeat is stale."""
        timeout = timedelta(minutes=self._settings.agent_disconnect_timeout_minutes)
        cutoff = datetime.now(UTC) - timeout

        async with self._session_factory() as session:
            # Find active agents with stale heartbeats
            stmt = (
                update(Agent)
                .where(
                    Agent.status == AgentStatus.active,
                    Agent.last_heartbeat_at < cutoff,
                )
                .values(status=AgentStatus.disconnected)
                .execution_options(synchronize_session=False)
            )
            result = await session.execute(stmt)
            await session.commit()

            # An UPDATE returns a CursorResult, which is where rowcount lives.
            marked = cast(CursorResult[Any], result).rowcount
            if marked > 0:
                logger.warning(
                    "Agents marked disconnected",
                    count=marked,
                    timeout_minutes=self._settings.agent_disconnect_timeout_minutes,
                )

    async def purge_posture_history(self, now: datetime | None = None) -> int:
        """Delete module transitions older than the retention window; at most once per 24 h."""
        now = now or datetime.now(UTC)
        if self._last_purge_at is not None and now - self._last_purge_at < timedelta(hours=24):
            return 0
        cutoff = now - timedelta(days=self._settings.agent_posture_history_retention_days)

        async with self._session_factory() as session:
            result = await session.execute(
                delete(AgentPostureChange)
                .where(AgentPostureChange.created_at < cutoff)
                .execution_options(synchronize_session=False)
            )
            await session.commit()

        self._last_purge_at = now
        purged: int = result.rowcount or 0  # type: ignore[attr-defined]
        if purged:
            logger.info("Agent posture history purged", count=purged, cutoff=cutoff.isoformat())
        return purged
