"""Tests for the grid-notifier delivery worker.

These cover the properties the queue exists for in the first place: a failure is
retried rather than lost, a hopeless job is buried rather than retried forever,
a flood is collapsed rather than mailed, and every outcome leaves a record.
"""

from unittest.mock import AsyncMock, MagicMock

import pytest

from ransomguard_grid.services.notification_queue import NotificationJob
from ransomguard_grid.workers.notification_worker import MAX_ATTEMPTS, NotificationWorker


def make_job(**overrides) -> NotificationJob:
    base = dict(
        tenant_id="tenant-1",
        tenant_name="Hopital Central",
        user_id="user-1",
        to_email="admin@example.org",
        to_name="Administrateur",
        alert_id="alert-1",
        alert_type="CanaryDeleted",
        severity="Critical",
        summary="Fichier canari supprime",
        agent_hostname="POSTE-01",
        detected_at="2026-09-28T12:00:00+00:00",
    )
    base.update(overrides)
    return NotificationJob(**base)  # type: ignore[arg-type]


class FakeSession:
    """Minimal async-context session that remembers what was added."""

    def __init__(self, sink: list) -> None:
        self._sink = sink

    async def __aenter__(self) -> "FakeSession":
        return self

    async def __aexit__(self, *_exc) -> None:
        return None

    def add(self, obj) -> None:
        self._sink.append(obj)

    async def commit(self) -> None:
        return None


def build_worker(*, throttle_allows: bool = True, send_fails: Exception | None = None):
    records: list = []
    queue = AsyncMock()
    queue.should_send.return_value = throttle_allows

    notifier = MagicMock()
    notifier.is_enabled = True
    notifier.notify_alert = AsyncMock(side_effect=send_fails)

    worker = NotificationWorker(
        session_factory=lambda: FakeSession(records),
        queue=queue,
        notifier=notifier,
        consumer_name="test-worker",
    )
    worker._settings.notification_throttle_minutes = 10  # noqa: SLF001 -- test seam
    return worker, queue, notifier, records


class TestSuccessfulDelivery:
    @pytest.mark.asyncio
    async def test_sends_then_acknowledges(self) -> None:
        worker, queue, notifier, records = build_worker()

        await worker._handle("1-0", make_job())  # noqa: SLF001

        notifier.notify_alert.assert_awaited_once()
        queue.ack.assert_awaited_once_with("1-0")
        assert [r.status for r in records] == ["sent"]

    @pytest.mark.asyncio
    async def test_records_the_recipient_and_alert(self) -> None:
        """The record has to identify who was told about what, or it proves nothing."""
        worker, _queue, _notifier, records = build_worker()

        await worker._handle("1-0", make_job())  # noqa: SLF001

        record = records[0]
        assert record.to_address == "admin@example.org"
        assert record.alert_id == "alert-1"
        assert record.tenant_id == "tenant-1"
        assert record.completed_at is not None


class TestThrottling:
    @pytest.mark.asyncio
    async def test_repeat_inside_the_window_is_suppressed_not_sent(self) -> None:
        worker, queue, notifier, records = build_worker(throttle_allows=False)

        await worker._handle("1-0", make_job())  # noqa: SLF001

        notifier.notify_alert.assert_not_awaited()
        queue.ack.assert_awaited_once_with("1-0")
        assert [r.status for r in records] == ["suppressed"]

    @pytest.mark.asyncio
    async def test_suppression_is_still_recorded(self) -> None:
        """A collapsed notification must stay traceable: silence in the audit
        trail is indistinguishable from a relay outage."""
        worker, _queue, _notifier, records = build_worker(throttle_allows=False)

        await worker._handle("1-0", make_job())  # noqa: SLF001

        assert records and records[0].status == "suppressed"


class TestFailureHandling:
    @pytest.mark.asyncio
    async def test_transient_failure_is_requeued_with_the_next_attempt(self) -> None:
        worker, queue, _notifier, records = build_worker(send_fails=OSError("relay unreachable"))

        await worker._handle("1-0", make_job(attempt=1))  # noqa: SLF001

        queue.publish.assert_awaited_once()
        assert queue.publish.await_args.args[0].attempt == 2
        assert [r.status for r in records] == ["failed"]

    @pytest.mark.asyncio
    async def test_the_failed_entry_is_acknowledged_so_it_is_not_reclaimed_twice(self) -> None:
        """The retry is a new entry; leaving the old one pending would have it
        reclaimed in parallel and deliver the same email twice."""
        worker, queue, _notifier, _records = build_worker(send_fails=OSError("boom"))

        await worker._handle("1-0", make_job())  # noqa: SLF001

        queue.ack.assert_awaited_once_with("1-0")

    @pytest.mark.asyncio
    async def test_last_attempt_is_buried_not_retried(self) -> None:
        worker, queue, _notifier, records = build_worker(send_fails=OSError("still down"))

        await worker._handle("1-0", make_job(attempt=MAX_ATTEMPTS))  # noqa: SLF001

        queue.publish.assert_not_awaited()
        queue.ack.assert_awaited_once_with("1-0")
        assert [r.status for r in records] == ["dead_letter"]

    @pytest.mark.asyncio
    async def test_error_message_is_kept_for_diagnosis(self) -> None:
        worker, _queue, _notifier, records = build_worker(send_fails=OSError("Connection refused"))

        await worker._handle("1-0", make_job(attempt=MAX_ATTEMPTS))  # noqa: SLF001

        assert "Connection refused" in records[0].error_message

    @pytest.mark.asyncio
    async def test_unconfigured_relay_fails_the_job_instead_of_dropping_it(self) -> None:
        """With SMTP off, jobs must still leave a trace -- otherwise a
        misconfiguration looks exactly like 'no alerts happened'."""
        worker, queue, notifier, records = build_worker()
        notifier.is_enabled = False

        await worker._handle("1-0", make_job())  # noqa: SLF001

        notifier.notify_alert.assert_not_awaited()
        assert records[0].status == "failed"
        queue.publish.assert_awaited_once()  # retried later, in case it gets configured
