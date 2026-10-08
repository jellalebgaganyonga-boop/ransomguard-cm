"""Durable notification queue backed by a Redis Stream.

Why a queue at all
------------------
Notifications used to be sent from inside the alert-ingestion request. An SMTP
relay can take seconds to answer (or ten, on timeout), and the agent posting the
alert waited for every recipient before getting its response -- precisely while
an attack is unfolding. A failed send was swallowed by a `try/except` and lost.

The queue turns the send into a durable hand-off: the API writes one entry
(sub-millisecond) and returns; the `grid-notifier` service consumes it, retries
on failure and records what happened.

Why a Redis Stream rather than a list
-------------------------------------
A consumer group gives us delivery acknowledgement: an entry read by a worker
that crashes before acknowledging stays in the pending list and is reclaimed,
instead of vanishing. `LPUSH`/`BRPOP` cannot do that -- a crash between pop and
send loses the notification silently, which is the very failure mode we are
removing.
"""

from __future__ import annotations

import json
from dataclasses import asdict, dataclass
from datetime import UTC, datetime
from typing import Any

import redis.asyncio as redis

from ransomguard_grid.core.config import get_settings
from ransomguard_grid.core.logging import get_logger

logger = get_logger("notification_queue")

STREAM_KEY = "grid:notifications"
CONSUMER_GROUP = "notifiers"
MAX_STREAM_LENGTH = 10_000  # approximate cap; oldest entries are trimmed


@dataclass(frozen=True)
class NotificationJob:
    """One notification to deliver to one recipient."""

    tenant_id: str
    tenant_name: str
    user_id: str
    to_email: str
    to_name: str
    alert_id: str
    alert_type: str
    severity: str
    summary: str
    agent_hostname: str
    detected_at: str  # ISO-8601; the stream carries strings only
    attempt: int = 1

    def to_fields(self) -> dict[str, str]:
        return {k: str(v) for k, v in asdict(self).items()}

    @classmethod
    def from_fields(cls, fields: dict[str, Any]) -> NotificationJob:
        decoded = {
            (k.decode() if isinstance(k, bytes) else k): (v.decode() if isinstance(v, bytes) else v)
            for k, v in fields.items()
        }
        decoded["attempt"] = int(decoded.get("attempt", 1))
        return cls(**decoded)  # type: ignore[arg-type]

    @property
    def detected_at_dt(self) -> datetime:
        try:
            return datetime.fromisoformat(self.detected_at)
        except ValueError:
            return datetime.now(UTC)

    def throttle_key(self) -> str:
        """Identity used to collapse repeats of the same event for the same person."""
        return f"grid:notif:throttle:{self.tenant_id}:{self.user_id}:{self.alert_type}:{self.severity}"


class NotificationQueue:
    """Publisher and consumer over the notification stream."""

    def __init__(self, client: redis.Redis | None = None) -> None:
        self._settings = get_settings()
        self._client = client or redis.from_url(self._settings.redis_url)

    @property
    def client(self) -> redis.Redis:
        return self._client

    async def close(self) -> None:
        await self._client.aclose()

    # ── Producer ──────────────────────────────────────────────────
    async def publish(self, job: NotificationJob) -> str:
        """Append a job to the stream. Returns the stream entry id."""
        entry_id = await self._client.xadd(
            STREAM_KEY,
            job.to_fields(),
            maxlen=MAX_STREAM_LENGTH,
            approximate=True,
        )
        return entry_id.decode() if isinstance(entry_id, bytes) else str(entry_id)

    # ── Consumer ──────────────────────────────────────────────────
    async def ensure_group(self) -> None:
        """Create the consumer group, tolerating an already-created one."""
        try:
            await self._client.xgroup_create(STREAM_KEY, CONSUMER_GROUP, id="0", mkstream=True)
            logger.info("Consumer group created", group=CONSUMER_GROUP, stream=STREAM_KEY)
        except redis.ResponseError as exc:  # BUSYGROUP: already exists
            if "BUSYGROUP" not in str(exc):
                raise

    async def read(self, consumer: str, count: int = 10, block_ms: int = 5000) -> list[tuple[str, NotificationJob]]:
        """Read new entries for this consumer. Returns (entry_id, job) pairs."""
        response = await self._client.xreadgroup(
            groupname=CONSUMER_GROUP,
            consumername=consumer,
            streams={STREAM_KEY: ">"},
            count=count,
            block=block_ms,
        )
        return self._decode(response)

    async def reclaim_stalled(
        self, consumer: str, min_idle_ms: int = 60_000, count: int = 10
    ) -> list[tuple[str, NotificationJob]]:
        """Take over entries a dead worker read but never acknowledged."""
        result = await self._client.xautoclaim(
            name=STREAM_KEY,
            groupname=CONSUMER_GROUP,
            consumername=consumer,
            min_idle_time=min_idle_ms,
            count=count,
        )
        # xautoclaim returns (next_cursor, entries, deleted) depending on version
        entries = result[1] if len(result) > 1 else []
        return [(self._as_str(eid), NotificationJob.from_fields(fields)) for eid, fields in entries]

    async def ack(self, entry_id: str) -> None:
        await self._client.xack(STREAM_KEY, CONSUMER_GROUP, entry_id)

    # ── Throttling ────────────────────────────────────────────────
    async def should_send(self, job: NotificationJob, window_seconds: int) -> bool:
        """False when an identical notification was already sent inside the window.

        A ransomware run fires many alerts of the same type within seconds. One
        email per event would bury the recipient and burn the provider quota,
        so repeats inside the window are suppressed -- the alert itself is still
        recorded in full, only the email is collapsed.
        """
        if window_seconds <= 0:
            return True
        # SET NX returns None when the key already exists.
        acquired = await self._client.set(job.throttle_key(), "1", nx=True, ex=window_seconds)
        return bool(acquired)

    # ── Helpers ───────────────────────────────────────────────────
    @staticmethod
    def _as_str(value: Any) -> str:
        return value.decode() if isinstance(value, bytes) else str(value)

    @classmethod
    def _decode(cls, response: Any) -> list[tuple[str, NotificationJob]]:
        jobs: list[tuple[str, NotificationJob]] = []
        for _stream, entries in response or []:
            for entry_id, fields in entries:
                try:
                    jobs.append((cls._as_str(entry_id), NotificationJob.from_fields(fields)))
                except (TypeError, ValueError):
                    logger.error("Undecodable stream entry, skipping", entry_id=cls._as_str(entry_id))
        return jobs


def job_from_alert(
    *,
    tenant_id: str,
    tenant_name: str,
    user_id: str,
    to_email: str,
    to_name: str,
    alert_id: str,
    alert_type: str,
    severity: str,
    summary: str,
    agent_hostname: str,
    detected_at: datetime,
) -> NotificationJob:
    """Build a job from an ingested alert."""
    return NotificationJob(
        tenant_id=tenant_id,
        tenant_name=tenant_name,
        user_id=user_id,
        to_email=to_email,
        to_name=to_name,
        alert_id=alert_id,
        alert_type=alert_type,
        severity=severity,
        summary=summary,
        agent_hostname=agent_hostname,
        detected_at=detected_at.isoformat(),
    )


def dumps(job: NotificationJob) -> str:
    """Compact representation used in logs and delivery records."""
    return json.dumps(asdict(job), ensure_ascii=False, separators=(",", ":"))
