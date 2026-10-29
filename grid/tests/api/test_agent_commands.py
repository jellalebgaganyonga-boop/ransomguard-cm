"""Commands between the console and the agent (plan de finition, J1 § 1.3).

GET /api/v1/agents/{agent_id}/commands and POST .../commands/{id}/result, partitioned like the
alerts; POST /api/v1/dashboard/commands for suspend_process / resume_process, where the alert
decides (its attribution, its reported suspension), never the client; and the alert detail that
shows commands and responses.
"""

from datetime import UTC, datetime, timedelta
from typing import Any
from uuid import uuid4

import pytest
from httpx import AsyncClient
from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from ransomguard_grid.db.models.agent import AgentCertificate
from ransomguard_grid.db.models.alerts import Alert, AlertDetail
from ransomguard_grid.db.models.enums import CommandStatus, Severity
from ransomguard_grid.db.models.operations import CommandQueue, CommandResponse, SystemLog
from tests.api.test_alert_attribution import Ids, _alert, _tenant_and_agent
from tests.api.test_tenant_isolation import _create_two_tenants, _login

CERT = {"X-Client-Cert": ""}


async def _queue(db: AsyncSession, ids: Ids, command_type: str = "resume_process", **parameters: Any) -> str:
    command_id = str(uuid4())
    db.add(CommandQueue(
        id=command_id, tenant_id=ids.tenant_id, agent_id=ids.agent_id, command_type=command_type,
        parameters_json=parameters, status=CommandStatus.Pending, issued_by_user_id=await _any_user(db, ids),
        issued_at=datetime.now(UTC),
    ))
    await db.commit()
    return command_id


async def _any_user(db: AsyncSession, ids: Ids) -> str:
    from ransomguard_grid.db.models.tenant_user import User
    user = (await db.execute(select(User).where(User.tenant_id == ids.tenant_id))).scalars().first()
    if user is not None:
        return user.id
    from ransomguard_grid.core.security import hash_password
    user_id = str(uuid4())
    db.add(User(id=user_id, tenant_id=ids.tenant_id, email=f"u-{user_id[:8]}@t.local", full_name="u",
                hashed_password=hash_password("x" * 16), is_active=True, created_at=datetime.now(UTC)))
    await db.flush()
    return user_id


async def _status(db: AsyncSession, command_id: str) -> CommandQueue:
    db.expire_all()
    return (await db.execute(select(CommandQueue).where(CommandQueue.id == command_id))).scalar_one()


# ── The agent's side ──────────────────────────────────────────────────────────


@pytest.mark.asyncio
async def test_the_agent_fetches_its_own_open_commands_oldest_first_and_they_become_dispatched(
    client: AsyncClient, db_session: AsyncSession, test_cert_serial: str, test_cert_pem: str,
) -> None:
    ids = await _tenant_and_agent(db_session, test_cert_serial)
    other = await _tenant_and_agent(db_session)
    first = await _queue(db_session, ids, "resume_process", suspension_id="s-1")
    second = await _queue(db_session, ids, "isolate", reason="x")
    await _queue(db_session, other, "resume_process", suspension_id="s-other")

    response = await client.get(f"/api/v1/agents/{ids.agent_id}/commands", headers={"X-Client-Cert": test_cert_pem})

    assert response.status_code == 200
    commands = response.json()["commands"]
    assert [c["id"] for c in commands] == [first, second]
    assert commands[0]["parameters"] == {"suspension_id": "s-1"}
    assert (await _status(db_session, first)).status == CommandStatus.Dispatched
    assert (await _status(db_session, first)).dispatched_at is not None

    # Fetched again until answered: an agent that died between the fetch and the execution asks again.
    again = await client.get(f"/api/v1/agents/{ids.agent_id}/commands", headers={"X-Client-Cert": test_cert_pem})
    assert [c["id"] for c in again.json()["commands"]] == [first, second]


@pytest.mark.asyncio
async def test_an_agent_cannot_fetch_or_answer_in_the_name_of_another_agent(
    client: AsyncClient, db_session: AsyncSession, test_cert_serial: str, test_cert_pem: str,
) -> None:
    ids = await _tenant_and_agent(db_session, test_cert_serial)
    other = await _tenant_and_agent(db_session)
    theirs = await _queue(db_session, other, "resume_process", suspension_id="s-other")

    assert (await client.get(f"/api/v1/agents/{other.agent_id}/commands",
                             headers={"X-Client-Cert": test_cert_pem})).status_code == 403
    assert (await client.post(f"/api/v1/agents/{other.agent_id}/commands/{theirs}/result", json={"success": True},
                              headers={"X-Client-Cert": test_cert_pem})).status_code == 403
    # Its own path, another agent's command: 404, nothing revealed.
    assert (await client.post(f"/api/v1/agents/{ids.agent_id}/commands/{theirs}/result", json={"success": True},
                              headers={"X-Client-Cert": test_cert_pem})).status_code == 404
    assert (await _status(db_session, theirs)).status == CommandStatus.Pending


@pytest.mark.asyncio
async def test_the_agents_answer_is_final_executed_or_not_with_its_reason_and_logged(
    client: AsyncClient, db_session: AsyncSession, test_cert_serial: str, test_cert_pem: str,
) -> None:
    ids = await _tenant_and_agent(db_session, test_cert_serial)
    done = await _queue(db_session, ids, "resume_process", suspension_id="s-1")
    refused = await _queue(db_session, ids, "isolate", reason="x")
    headers = {"X-Client-Cert": test_cert_pem}

    ok = await client.post(f"/api/v1/agents/{ids.agent_id}/commands/{done}/result",
                           json={"success": True, "payload": {"state": "resumed"}}, headers=headers)
    assert ok.status_code == 200
    assert ok.json() == {"command_id": done, "status": "Completed", "result": "applied"}

    no = await client.post(f"/api/v1/agents/{ids.agent_id}/commands/{refused}/result",
                           json={"success": False, "payload": {"reason": "not_implemented"}}, headers=headers)
    assert no.json()["status"] == "Failed"

    # Final: a second answer changes nothing.
    again = await client.post(f"/api/v1/agents/{ids.agent_id}/commands/{done}/result",
                              json={"success": False, "payload": {"reason": "late"}}, headers=headers)
    assert again.json()["result"] == "unchanged"
    assert (await _status(db_session, done)).status == CommandStatus.Completed

    db_session.expire_all()
    answers = (await db_session.execute(select(CommandResponse).where(CommandResponse.command_id == done))).scalars().all()
    assert len(answers) == 1 and answers[0].success is True and answers[0].response_payload_json == {"state": "resumed"}
    logs = (await db_session.execute(select(SystemLog).where(SystemLog.component == "agent-command"))).scalars().all()
    assert sorted(str(log.context_json["event"]) for log in logs if log.context_json) == ["CommandExecuted", "CommandNotExecuted"]


@pytest.mark.asyncio
async def test_the_result_body_is_bounded(
    client: AsyncClient, db_session: AsyncSession, test_cert_serial: str, test_cert_pem: str,
) -> None:
    ids = await _tenant_and_agent(db_session, test_cert_serial)
    command = await _queue(db_session, ids)
    headers = {"X-Client-Cert": test_cert_pem}
    url = f"/api/v1/agents/{ids.agent_id}/commands/{command}/result"

    assert (await client.post(url, json={"success": True, "extra": 1}, headers=headers)).status_code == 422
    assert (await client.post(url, json={"success": True, "payload": {"reason": "x" * 2001}}, headers=headers)).status_code == 422
    assert (await client.post(url, json={"success": True, "payload": {"nested": {"a": 1}}}, headers=headers)).status_code == 422
    assert (await _status(db_session, command)).status == CommandStatus.Pending


# ── The console's side ────────────────────────────────────────────────────────


async def _console(client: AsyncClient, db_session: AsyncSession, serial: str) -> tuple[dict[str, str], str, Ids]:
    """Tenant A of the isolation fixture, its admin logged in, its agent holding the test certificate."""
    setup = await _create_two_tenants()
    ids = Ids(setup["tenant_a_id"], setup["agent_a_id"])
    now = datetime.now(UTC)
    db_session.add(AgentCertificate(
        id=str(uuid4()), agent_id=ids.agent_id, serial_number=serial, fingerprint_sha256="f" * 64,
        not_before=now - timedelta(days=1), not_after=now + timedelta(days=365),
    ))
    await db_session.commit()
    token = await _login(client, setup["tenant_a_code"], setup["user_a_email"])
    return {"Authorization": f"Bearer {token}"}, setup["alert_a_id"], ids


async def _attributed(db: AsyncSession, ids: Ids, alert_id: str) -> None:
    for key, value in (("attribution_state", "resolved"), ("process_id", "4242"),
                       ("process_created_at", "2026-10-21T09:57:00+00:00"), ("process_name", "evil")):
        db.add(AlertDetail(id=str(uuid4()), tenant_id=ids.tenant_id, alert_id=alert_id, key=key, value=value))
    await db.commit()


async def _suspension_reported(db: AsyncSession, ids: Ids, alert_id: str, suspension_id: str) -> str:
    """The agent's response alert, linked by response_to to the canary alert."""
    db.expire_all()
    origin = (await db.execute(select(Alert).where(Alert.id == alert_id))).scalar_one()
    response_id = str(uuid4())
    db.add(Alert(
        id=response_id, tenant_id=ids.tenant_id, agent_id=ids.agent_id, client_message_id=f"resp-{uuid4().hex[:12]}",
        alert_type="SentinelProcessSuspended", severity=Severity.Critical, detected_at=datetime.now(UTC),
        ingested_at=datetime.now(UTC), summary="Process suspended", raw_payload_json={},
    ))
    await db.flush()
    for key, value in (("response", "process_suspended"), ("response_to", origin.client_message_id),
                       ("suspension_id", suspension_id), ("process_id", "4242"), ("process_name", "evil")):
        db.add(AlertDetail(id=str(uuid4()), tenant_id=ids.tenant_id, alert_id=response_id, key=key, value=value))
    await db.commit()
    return response_id


@pytest.mark.asyncio
async def test_suspend_process_takes_the_pid_from_the_alerts_attribution_never_from_the_client(
    client: AsyncClient, db_session: AsyncSession, test_cert_serial: str, test_cert_pem: str,
) -> None:
    headers, alert_id, ids = await _console(client, db_session, test_cert_serial)
    await _attributed(db_session, ids, alert_id)

    issued = await client.post("/api/v1/dashboard/commands", headers=headers, json={
        "agent_id": ids.agent_id, "command_type": "suspend_process",
        "parameters": {"alert_id": alert_id, "pid": 1},  # a client-side pid is ignored
    })

    assert issued.status_code == 200, issued.text
    fetched = await client.get(f"/api/v1/agents/{ids.agent_id}/commands", headers={"X-Client-Cert": test_cert_pem})
    [command] = fetched.json()["commands"]
    assert command["command_type"] == "suspend_process"
    assert command["parameters"]["pid"] == 4242
    assert command["parameters"]["process_created_at"].startswith("2026-10-21T09:57:00")
    assert command["parameters"]["alert_id"] == alert_id
    assert command["parameters"]["alert_client_message_id"]  # what the agent matches its local alert on

    detail = await client.get(f"/api/v1/dashboard/alerts/{alert_id}", headers=headers)
    [shown] = detail.json()["commands"]
    assert shown["id"] == command["id"] and shown["status"] == "Dispatched" and shown["success"] is None


@pytest.mark.asyncio
async def test_suspend_process_is_refused_without_a_resolved_attribution(
    client: AsyncClient, db_session: AsyncSession, test_cert_serial: str,
) -> None:
    headers, alert_id, ids = await _console(client, db_session, test_cert_serial)

    refused = await client.post("/api/v1/dashboard/commands", headers=headers, json={
        "agent_id": ids.agent_id, "command_type": "suspend_process", "parameters": {"alert_id": alert_id},
    })

    assert refused.status_code == 409
    assert (await client.get(f"/api/v1/dashboard/alerts/{alert_id}", headers=headers)).json()["commands"] == []


@pytest.mark.asyncio
async def test_resume_process_needs_a_suspension_the_agent_reported_on_this_alert(
    client: AsyncClient, db_session: AsyncSession, test_cert_serial: str, test_cert_pem: str,
) -> None:
    headers, alert_id, ids = await _console(client, db_session, test_cert_serial)
    await _attributed(db_session, ids, alert_id)
    body = {"agent_id": ids.agent_id, "command_type": "resume_process",
            "parameters": {"alert_id": alert_id, "suspension_id": "susp-1"}}

    assert (await client.post("/api/v1/dashboard/commands", headers=headers, json=body)).status_code == 409

    response_id = await _suspension_reported(db_session, ids, alert_id, "susp-1")
    detail = (await client.get(f"/api/v1/dashboard/alerts/{alert_id}", headers=headers)).json()
    assert detail["responses"] == [{
        "alert_id": response_id, "alert_type": "SentinelProcessSuspended", "response": "process_suspended",
        "detected_at": detail["responses"][0]["detected_at"], "suspension_id": "susp-1",
        "process_id": 4242, "process_name": "evil", "reason": None,
    }]

    issued = await client.post("/api/v1/dashboard/commands", headers=headers, json=body)
    assert issued.status_code == 200, issued.text
    command_id = issued.json()["id"]
    ok = await client.post(f"/api/v1/agents/{ids.agent_id}/commands/{command_id}/result",
                           json={"success": True, "payload": {"state": "resumed"}}, headers={"X-Client-Cert": test_cert_pem})
    assert ok.status_code == 200
    [shown] = (await client.get(f"/api/v1/dashboard/alerts/{alert_id}", headers=headers)).json()["commands"]
    assert shown["status"] == "Completed" and shown["success"] is True and shown["result"] == {"state": "resumed"}


@pytest.mark.asyncio
async def test_the_suspension_alert_shows_the_detection_it_answers(
    client: AsyncClient, db_session: AsyncSession, test_cert_serial: str,
) -> None:
    """The page the owner actually opens.

    "Process suspended after writing a canary: powershell (PID 3220)" is the alert anyone clicks,
    and on 2026-10-28 it was a sentence with nothing underneath it: no attribution, no chain, no
    way to let the process go. All of that is recorded on the canary detection the response
    answers, so the response alert is now read through it.
    """
    headers, alert_id, ids = await _console(client, db_session, test_cert_serial)
    await _attributed(db_session, ids, alert_id)
    response_id = await _suspension_reported(db_session, ids, alert_id, "susp-7")

    shown = (await client.get(f"/api/v1/dashboard/alerts/{response_id}", headers=headers)).json()

    assert shown["id"] == response_id, "it is still its own alert"
    assert shown["alert_type"] == "SentinelProcessSuspended"
    assert shown["attribution"]["state"] == "resolved"
    assert shown["attribution"]["process_id"] == 4242
    assert [r["suspension_id"] for r in shown["responses"]] == ["susp-7"], (
        "the console decides there is something to resume from this"
    )


@pytest.mark.asyncio
async def test_resume_can_be_asked_from_the_suspension_alert(
    client: AsyncClient, db_session: AsyncSession, test_cert_serial: str, test_cert_pem: str,
) -> None:
    """And the decision can be taken there, not only on the detection.

    The command still belongs to the detection: that is the client_message_id the agent matches
    its own suspension against, so asking from either page sends the agent the same thing.
    """
    headers, alert_id, ids = await _console(client, db_session, test_cert_serial)
    await _attributed(db_session, ids, alert_id)
    response_id = await _suspension_reported(db_session, ids, alert_id, "susp-8")

    issued = await client.post("/api/v1/dashboard/commands", headers=headers, json={
        "agent_id": ids.agent_id, "command_type": "resume_process",
        "parameters": {"alert_id": response_id, "suspension_id": "susp-8"},
    })

    assert issued.status_code == 200, issued.text
    fetched = await client.get(f"/api/v1/agents/{ids.agent_id}/commands",
                               headers={"X-Client-Cert": test_cert_pem})
    [command] = fetched.json()["commands"]
    assert command["command_type"] == "resume_process"
    assert command["parameters"]["suspension_id"] == "susp-8"
    assert command["parameters"]["alert_id"] == alert_id, "the detection, not the response"

    # And it is the same command on both pages, with the same answer.
    on_detection = (await client.get(f"/api/v1/dashboard/alerts/{alert_id}", headers=headers)).json()
    on_response = (await client.get(f"/api/v1/dashboard/alerts/{response_id}", headers=headers)).json()
    assert [c["id"] for c in on_detection["commands"]] == [command["id"]]
    assert [c["id"] for c in on_response["commands"]] == [command["id"]]


@pytest.mark.asyncio
async def test_a_suspension_that_was_never_reported_cannot_be_resumed_from_the_response_alert(
    client: AsyncClient, db_session: AsyncSession, test_cert_serial: str,
) -> None:
    """Reading through the detection does not loosen the check: the suspension must be one the
    agent reported."""
    headers, alert_id, ids = await _console(client, db_session, test_cert_serial)
    await _attributed(db_session, ids, alert_id)
    response_id = await _suspension_reported(db_session, ids, alert_id, "susp-9")

    refused = await client.post("/api/v1/dashboard/commands", headers=headers, json={
        "agent_id": ids.agent_id, "command_type": "resume_process",
        "parameters": {"alert_id": response_id, "suspension_id": "susp-not-reported"},
    })

    assert refused.status_code == 409


@pytest.mark.asyncio
async def test_an_alert_command_on_another_agents_alert_is_404(
    client: AsyncClient, db_session: AsyncSession, test_cert_serial: str,
) -> None:
    headers, alert_id, ids = await _console(client, db_session, test_cert_serial)
    other = await _tenant_and_agent(db_session)
    foreign_alert = await _alert(db_session, other, "cmid-foreign-0001", state="resolved")

    assert (await client.post("/api/v1/dashboard/commands", headers=headers, json={
        "agent_id": other.agent_id, "command_type": "suspend_process", "parameters": {"alert_id": foreign_alert},
    })).status_code == 404
    assert (await client.post("/api/v1/dashboard/commands", headers=headers, json={
        "agent_id": ids.agent_id, "command_type": "suspend_process", "parameters": {"alert_id": foreign_alert},
    })).status_code == 404
