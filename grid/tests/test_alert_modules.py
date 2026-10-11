"""MODULE: every alert_type the agent can send maps to a module; anything else is UNKNOWN."""

import json
from datetime import UTC, datetime, timedelta
from pathlib import Path
from uuid import uuid4

import pytest
from httpx import AsyncClient
from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from ransomguard_grid.api.v1.routes.alerts import record_unmapped_alert_type, unmapped_alert_type_log_id
from ransomguard_grid.core.alert_modules import MODULES, UNKNOWN, module_for
from ransomguard_grid.db.models.operations import SystemLog
from tests.api.test_alerts import _setup_agent_with_cert

# Kept equal to what the agent can send by the agent's AlertTypeContractTests.
_CONTRACT = Path(__file__).resolve().parents[2] / "shared" / "contracts" / "agent-alert-types.json"
_AGENT_ALERT_TYPES: list[str] = json.loads(_CONTRACT.read_text(encoding="utf-8"))["alert_types"]


def test_contract_is_not_empty() -> None:
    assert len(_AGENT_ALERT_TYPES) >= 16


@pytest.mark.parametrize("alert_type", _AGENT_ALERT_TYPES)
def test_every_alert_type_the_agent_can_send_has_a_module(alert_type: str) -> None:
    module = module_for(alert_type)
    assert module != UNKNOWN, f"{alert_type} has no module: the MODULE column would show UNKNOWN"
    assert module in MODULES


@pytest.mark.parametrize(
    ("alert_type", "module"),
    [
        ("SentinelCanaryModified", "SENTINEL"),
        ("EntropySuddenEntropyDelta", "ENTROPY"),
        ("UsbGuard", "USB_GUARD"),
        ("ExfilWatchDnsTunneling", "EXFIL_WATCH"),
    ],
)
def test_module_codes(alert_type: str, module: str) -> None:
    assert module_for(alert_type) == module


@pytest.mark.parametrize("alert_type", ["", "USB", "IndicatorRemovalDefenderTampering", "sentinelcanarymodified"])
def test_unknown_alert_type_maps_to_unknown_never_empty(alert_type: str) -> None:
    assert module_for(alert_type) == UNKNOWN


async def _ingest(client: AsyncClient, agent_id: str, cert_pem: str, alert_type: str) -> str:
    response = await client.post(
        f"/api/v1/agents/{agent_id}/alerts",
        json={
            "client_message_id": f"msg-{uuid4().hex[:20]}",
            "alert_type": alert_type,
            "severity": "Medium",
            "detected_at": datetime.now(UTC).isoformat(),
            "summary": "module test",
        },
        headers={"X-Client-Cert": cert_pem},
    )
    assert response.status_code == 200
    alert_id: str = response.json()["alert_id"]
    return alert_id


async def _unmapped_logs(db: AsyncSession, alert_type: str | None = None) -> list[SystemLog]:
    db.expire_all()  # the API wrote through another session
    logs = (await db.execute(select(SystemLog).where(SystemLog.component == "alert-ingest"))).scalars().all()
    return [log for log in logs if alert_type is None or (log.context_json or {}).get("alert_type") == alert_type]


@pytest.mark.asyncio
async def test_a_burst_of_one_unknown_type_writes_one_system_log_carrying_the_count(
    client: AsyncClient, db_session: AsyncSession, test_cert_serial: str, test_cert_pem: str,
) -> None:
    _, agent = await _setup_agent_with_cert(db_session, test_cert_serial, f"mod-{uuid4().hex[:6]}")

    alert_ids = [await _ingest(client, agent.id, test_cert_pem, "BrandNewModuleEvent") for _ in range(5)]

    logs = await _unmapped_logs(db_session, "BrandNewModuleEvent")
    assert len(logs) == 1, "one entry per (tenant, alert_type, hour), not one per alert"
    context = logs[0].context_json or {}
    assert context["count"] == 5
    assert context["first_alert_id"] == alert_ids[0]
    assert context["last_alert_id"] == alert_ids[-1]
    assert "5 alert(s) since" in logs[0].message


@pytest.mark.asyncio
async def test_a_batch_of_one_unknown_type_writes_one_system_log_carrying_the_count(
    client: AsyncClient, db_session: AsyncSession, test_cert_serial: str, test_cert_pem: str,
) -> None:
    _, agent = await _setup_agent_with_cert(db_session, test_cert_serial, f"mod-{uuid4().hex[:6]}")
    alert = {
        "alert_type": "BatchedUnknownEvent",
        "severity": "Medium",
        "detected_at": datetime.now(UTC).isoformat(),
        "summary": "batch",
    }

    response = await client.post(
        f"/api/v1/agents/{agent.id}/alerts/batch",
        json={"alerts": [{**alert, "client_message_id": f"msg-{uuid4().hex[:20]}"} for _ in range(30)]},
        headers={"X-Client-Cert": test_cert_pem},
    )

    assert response.status_code == 200
    [log] = await _unmapped_logs(db_session, "BatchedUnknownEvent")
    assert (log.context_json or {})["count"] == 30


@pytest.mark.asyncio
async def test_each_unknown_type_gets_its_own_entry(
    client: AsyncClient, db_session: AsyncSession, test_cert_serial: str, test_cert_pem: str,
) -> None:
    _, agent = await _setup_agent_with_cert(db_session, test_cert_serial, f"mod-{uuid4().hex[:6]}")

    await _ingest(client, agent.id, test_cert_pem, "FirstUnknownEvent")
    await _ingest(client, agent.id, test_cert_pem, "SecondUnknownEvent")
    await _ingest(client, agent.id, test_cert_pem, "SecondUnknownEvent")

    assert [(log.context_json or {})["count"] for log in await _unmapped_logs(db_session, "FirstUnknownEvent")] == [1]
    assert [(log.context_json or {})["count"] for log in await _unmapped_logs(db_session, "SecondUnknownEvent")] == [2]


@pytest.mark.asyncio
async def test_ingesting_a_known_alert_type_writes_no_system_log(
    client: AsyncClient, db_session: AsyncSession, test_cert_serial: str, test_cert_pem: str,
) -> None:
    _, agent = await _setup_agent_with_cert(db_session, test_cert_serial, f"mod-{uuid4().hex[:6]}")

    await _ingest(client, agent.id, test_cert_pem, "SentinelCanaryModified")

    assert await _unmapped_logs(db_session) == []


@pytest.mark.asyncio
async def test_a_new_hour_opens_a_new_entry(db_session: AsyncSession, test_cert_serial: str) -> None:
    tenant, agent = await _setup_agent_with_cert(db_session, test_cert_serial, f"mod-{uuid4().hex[:6]}")
    ten_past = datetime(2026, 10, 11, 9, 10, tzinfo=UTC)

    for now in (ten_past, ten_past + timedelta(minutes=40), ten_past + timedelta(minutes=55)):
        await record_unmapped_alert_type(db_session, tenant.id, agent.id, "HourlyEvent", str(uuid4()), now)
    await db_session.flush()

    counts = sorted(
        ((log.context_json or {})["window_start"], (log.context_json or {})["count"])
        for log in await _unmapped_logs(db_session, "HourlyEvent")
    )
    # 09:10 and 09:50 share the 09:00 entry; 10:05 opens the 10:00 one.
    assert counts == [("2026-10-11T09:00:00+00:00", 2), ("2026-10-11T10:00:00+00:00", 1)]


def test_the_entry_id_is_per_tenant_alert_type_and_hour() -> None:
    nine = datetime(2026, 10, 11, 9, tzinfo=UTC)
    same = unmapped_alert_type_log_id("tenant-a", "X", nine)

    assert unmapped_alert_type_log_id("tenant-a", "X", nine) == same
    assert unmapped_alert_type_log_id("tenant-b", "X", nine) != same
    assert unmapped_alert_type_log_id("tenant-a", "Y", nine) != same
    assert unmapped_alert_type_log_id("tenant-a", "X", nine + timedelta(hours=1)) != same


@pytest.mark.asyncio
async def test_control_characters_in_alert_type_never_reach_the_message(
    client: AsyncClient, db_session: AsyncSession, test_cert_serial: str, test_cert_pem: str,
) -> None:
    _, agent = await _setup_agent_with_cert(db_session, test_cert_serial, f"mod-{uuid4().hex[:6]}")
    forged = "Bad\nFAKE LOG LINE\x1b[31m"

    await _ingest(client, agent.id, test_cert_pem, forged)

    [log] = await _unmapped_logs(db_session, forged)
    assert "\n" not in log.message
    assert "\x1b" not in log.message
    assert repr(forged) in log.message


@pytest.mark.asyncio
async def test_an_alert_type_over_50_characters_is_refused_before_any_log(
    client: AsyncClient, db_session: AsyncSession, test_cert_serial: str, test_cert_pem: str,
) -> None:
    _, agent = await _setup_agent_with_cert(db_session, test_cert_serial, f"mod-{uuid4().hex[:6]}")

    response = await client.post(
        f"/api/v1/agents/{agent.id}/alerts",
        json={
            "client_message_id": f"msg-{uuid4().hex[:20]}",
            "alert_type": "U" * 51,
            "severity": "Medium",
            "detected_at": datetime.now(UTC).isoformat(),
            "summary": "too long",
        },
        headers={"X-Client-Cert": test_cert_pem},
    )

    assert response.status_code == 422
    assert await _unmapped_logs(db_session) == []


# ── Cap per tenant and hour, all unknown types together ──

async def _second_tenant(db: AsyncSession) -> str:
    from ransomguard_grid.db.models.enums import TenantStatus
    from ransomguard_grid.db.models.tenant_user import Tenant

    tenant = Tenant(id=str(uuid4()), name="T2", code=f"mod2-{uuid4().hex[:6]}", contact_email="t2@t.local",
                    status=TenantStatus.active)
    db.add(tenant)
    await db.flush()
    return tenant.id


@pytest.mark.asyncio
async def test_at_most_20_unknown_type_entries_per_tenant_and_hour_then_one_cap_entry(
    db_session: AsyncSession, test_cert_serial: str,
) -> None:
    tenant, agent = await _setup_agent_with_cert(db_session, test_cert_serial, f"mod-{uuid4().hex[:6]}")
    nine = datetime(2026, 10, 11, 9, 5, tzinfo=UTC)

    for i in range(25):  # an agent inventing a new type at every alert
        await record_unmapped_alert_type(db_session, tenant.id, agent.id, f"Invented{i}", str(uuid4()), nine)
    await record_unmapped_alert_type(db_session, tenant.id, agent.id, "Invented3", str(uuid4()), nine)  # already open
    await db_session.flush()

    logs = await _unmapped_logs(db_session)
    regular = [log for log in logs if not (log.context_json or {}).get("cap_reached")]
    [cap] = [log for log in logs if (log.context_json or {}).get("cap_reached")]
    assert len(regular) == 20
    assert (cap.context_json or {})["count"] == 5
    assert (cap.context_json or {})["last_key"] == "Invented24"
    assert "cap reached" in cap.message
    # An entry opened before the cap keeps counting its own type.
    [invented3] = [log for log in regular if (log.context_json or {})["alert_type"] == "Invented3"]
    assert (invented3.context_json or {})["count"] == 2


@pytest.mark.asyncio
async def test_the_cap_is_per_tenant_and_per_hour(db_session: AsyncSession, test_cert_serial: str) -> None:
    tenant, agent = await _setup_agent_with_cert(db_session, test_cert_serial, f"mod-{uuid4().hex[:6]}")
    other_tenant = await _second_tenant(db_session)
    nine = datetime(2026, 10, 11, 9, 5, tzinfo=UTC)

    for i in range(21):
        await record_unmapped_alert_type(db_session, tenant.id, agent.id, f"Invented{i}", str(uuid4()), nine)
    await record_unmapped_alert_type(db_session, other_tenant, agent.id, "OtherTenantType", str(uuid4()), nine)
    await record_unmapped_alert_type(db_session, tenant.id, agent.id, "NextHourType", str(uuid4()), nine + timedelta(hours=1))
    await db_session.flush()

    logs = await _unmapped_logs(db_session)
    assert [log.tenant_id for log in logs if (log.context_json or {}).get("alert_type") == "OtherTenantType"] == [other_tenant]
    assert [log for log in logs if (log.context_json or {}).get("alert_type") == "NextHourType"] != []
