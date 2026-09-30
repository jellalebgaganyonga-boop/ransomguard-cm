"""Tests for the notification queue (Redis Stream publisher/consumer).

No Redis here on purpose: the client is a double. These tests pin the contract
the worker depends on -- field encoding, throttle identity, decoding of stream
responses -- so a CI runner needs no extra service to catch a regression.
"""

from datetime import UTC, datetime
from unittest.mock import AsyncMock

import pytest

from ransomguard_grid.services.notification_queue import (
    CONSUMER_GROUP,
    STREAM_KEY,
    NotificationJob,
    NotificationQueue,
    job_from_alert,
)


def make_job(**overrides) -> NotificationJob:
    base = dict(
        tenant_id="tenant-1",
        tenant_name="Hopital Central",
        user_id="user-1",
        to_email="admin@example.org",
        to_name="Administrateur",
        alert_id="alert-1",
        alert_type="EntropyAbsoluteHigh",
        severity="Critical",
        summary="Chiffrement massif detecte",
        agent_hostname="POSTE-01",
        detected_at="2026-09-28T12:00:00+00:00",
    )
    base.update(overrides)
    return NotificationJob(**base)  # type: ignore[arg-type]


class TestJobEncoding:
    def test_roundtrip_through_stream_fields(self) -> None:
        """A Redis stream carries strings only; the job must survive the trip."""
        job = make_job()
        assert NotificationJob.from_fields(job.to_fields()) == job

    def test_roundtrip_from_bytes(self) -> None:
        """redis-py returns bytes unless decode_responses is set."""
        job = make_job(attempt=3)
        raw = {k.encode(): v.encode() for k, v in job.to_fields().items()}
        assert NotificationJob.from_fields(raw) == job

    def test_attempt_is_an_int_after_decoding(self) -> None:
        job = NotificationJob.from_fields(make_job(attempt=2).to_fields())
        assert job.attempt == 2 and isinstance(job.attempt, int)

    def test_detected_at_falls_back_to_now_when_unparsable(self) -> None:
        """A malformed timestamp must not crash delivery of a real alert."""
        job = make_job(detected_at="not-a-date")
        assert isinstance(job.detected_at_dt, datetime)

    def test_job_from_alert_serialises_the_datetime(self) -> None:
        job = job_from_alert(
            tenant_id="t", tenant_name="T", user_id="u", to_email="a@b.c", to_name="A",
            alert_id="al", alert_type="CanaryDeleted", severity="Critical", summary="s",
            agent_hostname="h", detected_at=datetime(2026, 9, 28, 12, 0, tzinfo=UTC),
        )
        assert job.detected_at == "2026-09-28T12:00:00+00:00"
        assert job.attempt == 1


class TestThrottleIdentity:
    def test_same_event_for_same_user_shares_a_key(self) -> None:
        a = make_job(alert_id="alert-1")
        b = make_job(alert_id="alert-2")  # different alert, same event shape
        assert a.throttle_key() == b.throttle_key()

    def test_different_recipients_do_not_share_a_key(self) -> None:
        assert make_job(user_id="u1").throttle_key() != make_job(user_id="u2").throttle_key()

    def test_different_alert_types_do_not_share_a_key(self) -> None:
        assert make_job(alert_type="A").throttle_key() != make_job(alert_type="B").throttle_key()

    def test_different_tenants_do_not_share_a_key(self) -> None:
        """Tenant isolation extends to the throttle: one hospital's traffic
        must never silence another's notifications."""
        assert make_job(tenant_id="t1").throttle_key() != make_job(tenant_id="t2").throttle_key()


class TestQueueOperations:
    @pytest.mark.asyncio
    async def test_publish_appends_to_the_stream_with_a_cap(self) -> None:
        client = AsyncMock()
        client.xadd.return_value = b"1700000000-0"
        queue = NotificationQueue(client=client)

        entry_id = await queue.publish(make_job())

        assert entry_id == "1700000000-0"
        args, kwargs = client.xadd.call_args
        assert args[0] == STREAM_KEY
        assert kwargs["approximate"] is True and kwargs["maxlen"] > 0

    @pytest.mark.asyncio
    async def test_read_decodes_entries(self) -> None:
        job = make_job()
        client = AsyncMock()
        client.xreadgroup.return_value = [(STREAM_KEY.encode(), [(b"1-0", job.to_fields())])]
        queue = NotificationQueue(client=client)

        entries = await queue.read("worker-1")

        assert entries == [("1-0", job)]
        assert client.xreadgroup.call_args.kwargs["groupname"] == CONSUMER_GROUP

    @pytest.mark.asyncio
    async def test_throttle_allows_the_first_and_blocks_the_repeat(self) -> None:
        client = AsyncMock()
        client.set.side_effect = [True, None]  # SET NX: acquired, then already held
        queue = NotificationQueue(client=client)
        job = make_job()

        assert await queue.should_send(job, window_seconds=600) is True
        assert await queue.should_send(job, window_seconds=600) is False
        assert client.set.call_args.kwargs == {"nx": True, "ex": 600}

    @pytest.mark.asyncio
    async def test_window_of_zero_disables_throttling(self) -> None:
        """An operator must be able to turn collapsing off without touching Redis."""
        client = AsyncMock()
        queue = NotificationQueue(client=client)

        assert await queue.should_send(make_job(), window_seconds=0) is True
        client.set.assert_not_called()
