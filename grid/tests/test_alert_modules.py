"""MODULE: every alert_type the agent can send maps to a module; anything else is UNKNOWN."""

import json
from datetime import UTC, datetime
from pathlib import Path
from uuid import uuid4

import pytest
from httpx import AsyncClient
from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

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


@pytest.mark.asyncio
async def test_ingesting_an_alert_type_without_module_writes_a_system_log(
    client: AsyncClient, db_session: AsyncSession, test_cert_serial: str, test_cert_pem: str,
) -> None:
    _, agent = await _setup_agent_with_cert(db_session, test_cert_serial, f"mod-{uuid4().hex[:6]}")

    alert_id = await _ingest(client, agent.id, test_cert_pem, "BrandNewModuleEvent")

    logs = (await db_session.execute(select(SystemLog).where(SystemLog.component == "alert-ingest"))).scalars().all()
    assert any((log.context_json or {}).get("alert_id") == alert_id for log in logs)


@pytest.mark.asyncio
async def test_ingesting_a_known_alert_type_writes_no_system_log(
    client: AsyncClient, db_session: AsyncSession, test_cert_serial: str, test_cert_pem: str,
) -> None:
    _, agent = await _setup_agent_with_cert(db_session, test_cert_serial, f"mod-{uuid4().hex[:6]}")

    alert_id = await _ingest(client, agent.id, test_cert_pem, "SentinelCanaryModified")

    logs = (await db_session.execute(select(SystemLog).where(SystemLog.component == "alert-ingest"))).scalars().all()
    assert not any((log.context_json or {}).get("alert_id") == alert_id for log in logs)
