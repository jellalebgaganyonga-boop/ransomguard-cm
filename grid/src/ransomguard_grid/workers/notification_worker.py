"""grid-notifier — the notification delivery service.

Runs as its own container from the same image as the API (`python -m
ransomguard_grid.workers.notification_worker`). Separate process on purpose: a
stuck SMTP relay must never hold up alert ingestion, and restarting the notifier
must not restart the API.

Delivery contract
-----------------
* at-least-once: an entry stays pending until it is acknowledged, and a worker
  that dies mid-send has its entries reclaimed by the next one;
* bounded retries: transient failures are retried with exponential backoff, and
  a job that exhausts them is recorded as `dead_letter` rather than dropped;
* collapsed repeats: identical notifications inside the throttle window are
  recorded as `suppressed` -- the alert is still stored in full, only the email
  is spared;
* every outcome is written to `notification_deliveries`, so a relay outage is
  visible instead of silent.
"""

from __future__ import annotations

import asyncio
import signal
import socket
from datetime import UTC, datetime
from uuid import uuid4

from sqlalchemy.ext.asyncio import AsyncSession, async_sessionmaker

from ransomguard_grid.core.config import get_settings
from ransomguard_grid.core.logging import get_logger
from ransomguard_grid.db.models.notifications import NotificationDelivery
from ransomguard_grid.services.notification_queue import NotificationJob, NotificationQueue
from ransomguard_grid.services.notification_service import NotificationService

logger = get_logger("notification_worker")

MAX_ATTEMPTS = 5
BACKOFF_BASE_SECONDS = 2  # 2s, 4s, 8s, 16s
RECLAIM_IDLE_MS = 120_000
IDLE_BLOCK_MS = 5_000


class NotificationWorker:
    """Consumes notification jobs and delivers them."""

    def __init__(
        self,
        session_factory: async_sessionmaker[AsyncSession],
        queue: NotificationQueue | None = None,
        notifier: NotificationService | None = None,
        consumer_name: str | None = None,
    ) -> None:
        self._sessions = session_factory
        self._queue = queue or NotificationQueue()
        self._notifier = notifier or NotificationService()
        self._settings = get_settings()
        self._consumer = consumer_name or f"{socket.gethostname()}-{uuid4().hex[:6]}"
        self._stopping = asyncio.Event()

    # ── Lifecycle ─────────────────────────────────────────────────
    async def run(self) -> None:
        await self._queue.ensure_group()
        logger.info(
            "Notification worker started",
            consumer=self._consumer,
            smtp_enabled=self._notifier.is_enabled,
            throttle_minutes=self._settings.notification_throttle_minutes,
        )
        if not self._notifier.is_enabled:
            # Still consume: jobs are recorded as failed rather than piling up
            # in the stream until Redis trims them away unnoticed.
            logger.warning("SMTP is not configured; jobs will be recorded as failed")

        while not self._stopping.is_set():
            try:
                await self._tick()
            except asyncio.CancelledError:
                raise
            except Exception:
                logger.error("Worker loop error", exc_info=True)
                await asyncio.sleep(5)

        await self._queue.close()
        logger.info("Notification worker stopped", consumer=self._consumer)

    def stop(self) -> None:
        self._stopping.set()

    # ── One cycle ─────────────────────────────────────────────────
    async def _tick(self) -> None:
        # Entries a previous worker read but never acknowledged come first:
        # they are the oldest, and they are the ones at risk of being lost.
        reclaimed = await self._queue.reclaim_stalled(self._consumer, min_idle_ms=RECLAIM_IDLE_MS)
        if reclaimed:
            logger.info("Reclaimed stalled notifications", count=len(reclaimed))
        for entry_id, job in reclaimed:
            await self._handle(entry_id, job)

        for entry_id, job in await self._queue.read(self._consumer, block_ms=IDLE_BLOCK_MS):
            await self._handle(entry_id, job)

    async def _handle(self, entry_id: str, job: NotificationJob) -> None:
        window = self._settings.notification_throttle_minutes * 60
        if not await self._queue.should_send(job, window):
            logger.info(
                "Notification collapsed by throttle window",
                alert_type=job.alert_type,
                to=job.to_email,
                window_minutes=self._settings.notification_throttle_minutes,
            )
            await self._record(job, status="suppressed")
            await self._queue.ack(entry_id)
            return

        try:
            if not self._notifier.is_enabled:
                raise RuntimeError("SMTP relay is not configured")

            await self._notifier.notify_alert(
                to_email=job.to_email,
                to_name=job.to_name,
                alert_type=job.alert_type,
                severity=job.severity,
                summary=job.summary,
                agent_hostname=job.agent_hostname,
                detected_at=job.detected_at_dt,
                alert_id=job.alert_id,
                tenant_name=job.tenant_name,
            )
        except Exception as exc:  # noqa: BLE001 -- any failure must be retried, not lost
            await self._retry_or_bury(entry_id, job, exc)
            return

        await self._record(job, status="sent")
        await self._queue.ack(entry_id)
        logger.info("Notification delivered", to=job.to_email, alert_id=job.alert_id, attempt=job.attempt)

    async def _retry_or_bury(self, entry_id: str, job: NotificationJob, exc: Exception) -> None:
        """Re-queue with backoff, or record a dead letter once attempts run out."""
        message = str(exc)[:500]

        if job.attempt >= MAX_ATTEMPTS:
            logger.error(
                "Notification permanently failed",
                to=job.to_email,
                alert_id=job.alert_id,
                attempts=job.attempt,
                error=message,
            )
            await self._record(job, status="dead_letter", error=message)
            await self._queue.ack(entry_id)
            return

        delay = BACKOFF_BASE_SECONDS**job.attempt
        logger.warning(
            "Notification failed, retrying",
            to=job.to_email,
            attempt=job.attempt,
            retry_in_seconds=delay,
            error=message,
        )
        await self._record(job, status="failed", error=message)

        # The retry is a NEW stream entry so the current one can be acknowledged:
        # leaving it pending would have it reclaimed in parallel with the retry.
        retry_job = NotificationJob(**{**job.__dict__, "attempt": job.attempt + 1})
        await self._queue.ack(entry_id)
        await asyncio.sleep(min(delay, 30))
        await self._queue.publish(retry_job)

    # ── Audit ─────────────────────────────────────────────────────
    async def _record(self, job: NotificationJob, *, status: str, error: str | None = None) -> None:
        try:
            async with self._sessions() as session:
                session.add(
                    NotificationDelivery(
                        id=str(uuid4()),
                        tenant_id=job.tenant_id,
                        user_id=job.user_id,
                        alert_id=job.alert_id,
                        channel="email",
                        to_address=job.to_email,
                        status=status,
                        attempt=job.attempt,
                        error_message=error,
                        completed_at=datetime.now(UTC),
                    )
                )
                await session.commit()
        except Exception:
            # Never let the audit write break delivery -- the email matters more
            # than its record, and the log still carries the outcome.
            logger.error("Could not record delivery", status=status, alert_id=job.alert_id, exc_info=True)


async def main() -> None:
    from ransomguard_grid.db.session import AsyncSessionLocal

    worker = NotificationWorker(session_factory=AsyncSessionLocal)

    loop = asyncio.get_running_loop()
    for sig in (signal.SIGTERM, signal.SIGINT):
        try:
            loop.add_signal_handler(sig, worker.stop)
        except NotImplementedError:  # Windows
            signal.signal(sig, lambda *_: worker.stop())

    await worker.run()


if __name__ == "__main__":
    asyncio.run(main())
