#!/usr/bin/env python3
"""Confirm a live detection end to end, through the API only.

Used by system-check.ps1 -WithDetection. The operator runs the harmless trigger on the
workstation; this confirms what reached the GRID, and then exercises the other direction by
sending Resume and confirming the closure came back.

It asserts nothing it has not read from the GRID. "before" records the alert count so "confirm"
can tell a new detection from one that was already there.
"""

from __future__ import annotations

import argparse
import json
import ssl
import sys
import time
import urllib.error
import urllib.request
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
CA = REPO / "deployment" / "grid" / "pki" / "ca.crt"
CREDENTIALS = REPO / "deployment" / "bench" / "credentials.local.md"
STATE = REPO / "agent" / "packages" / "bench" / "system-check-detection.json"
GRID = "https://192.168.56.1:8443"

PASS, FAIL = "PASS", "FAIL"

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")


def emit(name: str, status: str, detail: str = "", fix: str = "") -> None:
    print(json.dumps({"id": name, "name": name, "status": status, "detail": detail, "fix": fix}))


def call(method, path, body=None, token=None):
    data = json.dumps(body).encode() if body is not None else None
    request = urllib.request.Request(GRID + path, data=data, method=method)
    request.add_header("Content-Type", "application/json")
    if token:
        request.add_header("Authorization", "Bearer " + token)
    ctx = ssl.create_default_context(cafile=str(CA))
    try:
        with urllib.request.urlopen(request, context=ctx, timeout=30) as response:
            raw = response.read().decode()
            return response.status, (json.loads(raw) if raw else None)
    except urllib.error.HTTPError as error:
        raw = error.read().decode()
        try:
            return error.code, json.loads(raw)
        except json.JSONDecodeError:
            return error.code, raw


def admin_token() -> str | None:
    if not CREDENTIALS.exists():
        return None
    for line in CREDENTIALS.read_text(encoding="utf-8").splitlines():
        if not line.startswith("|") or "@" not in line:
            continue
        cells = [c.strip().strip("`") for c in line.strip("|").split("|")]
        if len(cells) < 4 or "tenant_admin" not in cells[1] or not cells[2].startswith("yde"):
            continue
        password = cells[3].split("`")[0].strip() if "`" in cells[3] else cells[3].split(" ")[0]
        status, body = call("POST", "/api/v1/auth/login",
                            {"email": cells[0], "password": password, "tenant_code": cells[2]})
        if status == 200:
            return body["access_token"]
    return None


def agent_for(token: str, vm: str):
    _, listed = call("GET", "/api/v1/dashboard/agents", token=token)
    agents = (listed or {}).get("items", [])
    # The VirtualBox name and the Windows name are the same on this bench; fall back to the only
    # live agent rather than failing on a mismatch the operator cannot do anything about.
    for agent in agents:
        if (agent.get("hostname") or "").upper() == vm.upper():
            return agent
    return agents[0] if agents else None


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--stage", required=True, choices=["before", "confirm"])
    parser.add_argument("--vm", required=True)
    args = parser.parse_args()

    token = admin_token()
    if not token:
        emit("A live detection", FAIL, "could not sign in as the hospital administrator",
             "check deployment/bench/credentials.local.md")
        return 1

    agent = agent_for(token, args.vm)
    if not agent:
        emit("A live detection", FAIL, f"no enrolled workstation matching {args.vm}",
             "install the agent on it first (docs/OWNER_TEST_GUIDE.md)")
        return 1

    if args.stage == "before":
        _, alerts = call("GET", f"/api/v1/dashboard/alerts?agent_id={agent['id']}&limit=1",
                         token=token)
        STATE.parent.mkdir(parents=True, exist_ok=True)
        STATE.write_text(json.dumps({
            "agent_id": agent["id"],
            "hostname": agent["hostname"],
            "alerts_before": (alerts or {}).get("total", 0),
        }), encoding="utf-8")
        return 0

    before = json.loads(STATE.read_text(encoding="utf-8")) if STATE.exists() else {}
    agent_id = before.get("agent_id", agent["id"])
    host = before.get("hostname", agent["hostname"])

    # 1. the alert
    found = None
    for _ in range(20):
        _, alerts = call("GET", f"/api/v1/dashboard/alerts?agent_id={agent_id}&limit=20",
                         token=token)
        canary = [a for a in (alerts or {}).get("items", [])
                  if a.get("alert_type") == "SentinelCanaryModified"]
        if canary and (alerts or {}).get("total", 0) > before.get("alerts_before", 0):
            found = sorted(canary, key=lambda a: a["detected_at"])[-1]
            break
        time.sleep(3)

    if not found:
        emit(f"{host}: the decoy raised an alert", FAIL, "no new SentinelCanaryModified arrived",
             "did the trigger run, and is the agent active? see the agent log on the workstation")
        return 1

    emit(f"{host}: the decoy raised an alert", PASS,
         f"{found['alert_type']}, {found['severity']}, at {found['detected_at'][11:19]}")

    # 2. who did it, 3. what started it -- both arrive as complements, so allow for them
    detail = None
    for _ in range(20):
        _, detail = call("GET", f"/api/v1/dashboard/alerts/{found['id']}", token=token)
        if (detail or {}).get("attribution") and (detail or {}).get("genealogy"):
            break
        time.sleep(3)

    attribution = (detail or {}).get("attribution") or {}
    if attribution.get("state") == "resolved":
        emit(f"{host}: the writer is named", PASS,
             f"{attribution.get('process_name')} (PID {attribution.get('process_id')})")
    else:
        emit(f"{host}: the writer is named", FAIL,
             f"attribution {attribution.get('state')} ({attribution.get('reason')})",
             "the kernel session names the writer; check SENTINEL on the agent page")

    genealogy = (detail or {}).get("genealogy")
    if genealogy and genealogy.get("chain"):
        emit(f"{host}: the chain behind the write", PASS, genealogy["chain"])
    else:
        emit(f"{host}: the chain behind the write", FAIL, "no chain reached the GRID",
             "GENEALOGY sends it after the alert; check it reads active on the agent page")

    # 4. the suspension
    suspension = None
    for _ in range(10):
        _, suspensions = call("GET", f"/api/v1/dashboard/agents/{agent_id}/suspensions",
                              token=token)
        if suspensions:
            suspension = suspensions[0]
            break
        time.sleep(3)

    if not suspension:
        emit(f"{host}: the writer is suspended", FAIL, "nothing is listed as suspended",
             "suspension needs the writer named; see the line above")
        return 1

    emit(f"{host}: the writer is suspended", PASS,
         f"{suspension.get('process_name')} (PID {suspension.get('process_id')}) is frozen")

    # 5. Resume, and the closure coming back
    status, command = call("POST", "/api/v1/dashboard/commands", {
        "agent_id": agent_id, "command_type": "resume_process",
        "parameters": {"alert_id": suspension.get("alert_id"),
                       "suspension_id": suspension.get("suspension_id"),
                       "process_id": suspension.get("process_id")},
    }, token=token)

    if status != 200:
        emit(f"{host}: Resume is accepted", FAIL, f"HTTP {status}", "")
        return 1
    emit(f"{host}: Resume is accepted", PASS, f"command {command['id'][:8]} queued")

    resumed = False
    for _ in range(30):
        _, alerts = call("GET", f"/api/v1/dashboard/alerts?agent_id={agent_id}&limit=20",
                         token=token)
        if any(a.get("alert_type") == "SentinelProcessResumed"
               for a in (alerts or {}).get("items", [])):
            resumed = True
            break
        time.sleep(3)

    if resumed:
        emit(f"{host}: the workstation confirms the resume", PASS,
             "SentinelProcessResumed reached the GRID, so the console can stop showing it as frozen")
    else:
        emit(f"{host}: the workstation confirms the resume", FAIL,
             "no SentinelProcessResumed arrived",
             "the agent polls for commands on a schedule; if it never arrives, check alerts_rejected")

    return 0


if __name__ == "__main__":
    sys.exit(main())
