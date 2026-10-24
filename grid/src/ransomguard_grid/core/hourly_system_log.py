"""Bounded SystemLog entries for signals an agent can repeat at its own rate.

An alert_type with no module, a heartbeat module code the server does not know: one log per
occurrence would let a defective agent fill ``system_logs`` (and the application log). Two
bounds apply, per tenant and per UTC hour:

* one entry per (family, key), carrying the count of occurrences;
* at most ``CAP_PER_TENANT_HOUR`` such entries per family, all keys together. Beyond, every
  occurrence of a new key goes to a single "cap reached" entry, with its own count -- an agent
  inventing a new type at every alert still writes at most 21 rows an hour.

Entries have deterministic ids, so concurrent writers meet on the same row: the row is locked
while its count is incremented, and a concurrent first insert is caught in a savepoint -- the
caller's own writes are never lost to the log. Opening a new entry locks the tenant row first,
so the cap holds under concurrency on MySQL (SQLite serialises writers anyway).
"""

from collections.abc import Callable, Mapping
from datetime import datetime, timedelta
from uuid import NAMESPACE_URL, uuid5

from sqlalchemy import select
from sqlalchemy.exc import IntegrityError
from sqlalchemy.ext.asyncio import AsyncSession

from ransomguard_grid.core.logging import get_logger
from ransomguard_grid.db.models.enums import LogLevel
from ransomguard_grid.db.models.operations import SystemLog
from ransomguard_grid.db.models.tenant_user import Tenant

logger = get_logger("hourly_system_log")

CAP_PER_TENANT_HOUR = 20
_CAP_KEY = "__cap_reached__"


def hourly_log_id(component: str, tenant_id: str, key: str, window_start: datetime) -> str:
    """Id of THE entry for (component, tenant, key, hour): one entry, whatever the rate."""
    return str(uuid5(NAMESPACE_URL, f"rg:{component}:{tenant_id}:{key}:{window_start.isoformat()}"))


def cap_log_id(component: str, tenant_id: str, family: str, window_start: datetime) -> str:
    """Id of the single "cap reached" entry of a family for (tenant, hour)."""
    return hourly_log_id(component, tenant_id, f"{family}:{_CAP_KEY}", window_start)


async def record_hourly(
    db: AsyncSession,
    *,
    tenant_id: str,
    component: str,
    family: str,
    key: str,
    message: Callable[[int, datetime], str],
    first_context: Mapping[str, object],
    latest_context: Mapping[str, object],
    now: datetime,
    log_event: str,
) -> None:
    """Create the hour's entry for ``key`` (count 1) or increment it -- or, once the family has
    ``CAP_PER_TENANT_HOUR`` entries this hour, count the occurrence in the cap entry instead.

    ``message(count, window_start)`` builds the text; it must render agent-supplied values with
    repr() so no raw newline or control character reaches the log.
    """
    window_start = now.replace(minute=0, second=0, microsecond=0)
    log_id = hourly_log_id(component, tenant_id, key, window_start)

    # The caller's pending writes first: an error on them must never be taken for a log conflict.
    await db.flush()

    if await _increment(db, log_id, latest_context, message, window_start):
        return

    # A new entry. Serialise per tenant, then check the family's cap for this hour.
    await db.execute(select(Tenant.id).where(Tenant.id == tenant_id).with_for_update())
    if await _entries_this_hour(db, tenant_id, component, family, window_start) >= CAP_PER_TENANT_HOUR:
        await _record_over_cap(db, tenant_id, component, family, key, now, window_start, log_event)
        return

    await _create_or_increment(
        db, log_id, tenant_id, component, message, window_start, now,
        context={**first_context, **latest_context, "family": family},
        latest_context=latest_context, log_event=log_event, log_fields=first_context,
    )


async def _increment(
    db: AsyncSession,
    log_id: str,
    latest_context: Mapping[str, object],
    message: Callable[[int, datetime], str],
    window_start: datetime,
) -> bool:
    entry = (await db.execute(select(SystemLog).where(SystemLog.id == log_id).with_for_update())).scalar_one_or_none()
    if entry is None:
        return False
    context = dict(entry.context_json or {})
    count = int(str(context.get("count", 1))) + 1
    context.update(latest_context)
    context["count"] = count
    entry.context_json = context  # reassigned, so the JSON change is persisted
    entry.message = message(count, window_start)
    return True


async def _entries_this_hour(
    db: AsyncSession, tenant_id: str, component: str, family: str, window_start: datetime
) -> int:
    """Entries of this family opened this hour for this tenant, the cap entry excluded."""
    rows = (
        await db.execute(
            select(SystemLog.id, SystemLog.context_json).where(
                SystemLog.tenant_id == tenant_id,
                SystemLog.component == component,
                SystemLog.created_at >= window_start,
                SystemLog.created_at < window_start + timedelta(hours=1),
            )
        )
    ).all()
    cap_id = cap_log_id(component, tenant_id, family, window_start)
    return sum(1 for log_id, context in rows if log_id != cap_id and (context or {}).get("family") == family)


async def _record_over_cap(
    db: AsyncSession,
    tenant_id: str,
    component: str,
    family: str,
    key: str,
    now: datetime,
    window_start: datetime,
    log_event: str,
) -> None:
    def message(count: int, start: datetime) -> str:
        return (
            f"{family}: more than {CAP_PER_TENANT_HOUR} distinct values this hour, cap reached; "
            f"{count} further occurrence(s) since {start:%Y-%m-%d %H:%M} UTC, last {key!r}"
        )

    cap_id = cap_log_id(component, tenant_id, family, window_start)
    latest = {"last_key": key}
    if await _increment(db, cap_id, latest, message, window_start):
        return
    await _create_or_increment(
        db, cap_id, tenant_id, component, message, window_start, now,
        context={"family": family, "cap_reached": True, "cap": CAP_PER_TENANT_HOUR, **latest},
        latest_context=latest, log_event=f"{log_event}_cap_reached", log_fields={"family": family},
    )


async def _create_or_increment(
    db: AsyncSession,
    log_id: str,
    tenant_id: str,
    component: str,
    message: Callable[[int, datetime], str],
    window_start: datetime,
    now: datetime,
    *,
    context: Mapping[str, object],
    latest_context: Mapping[str, object],
    log_event: str,
    log_fields: Mapping[str, object],
) -> None:
    try:
        async with db.begin_nested():
            db.add(SystemLog(
                id=log_id,
                tenant_id=tenant_id,
                level=LogLevel.Warning,
                component=component,
                message=message(1, window_start),
                context_json={**context, "count": 1, "window_start": window_start.isoformat()},
                created_at=now,
            ))
        # Application log: once per entry created, not once per occurrence.
        logger.warning(log_event, tenant_id=tenant_id, **{k: str(v) for k, v in log_fields.items()})
    except IntegrityError:
        # Another writer created the entry first: increment it.
        if not await _increment(db, log_id, latest_context, message, window_start):
            # Not reached in practice; never drop the signal silently.
            logger.error(f"{log_event}_not_recorded", tenant_id=tenant_id, log_id=log_id)

