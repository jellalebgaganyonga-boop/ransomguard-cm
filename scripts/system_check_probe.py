#!/usr/bin/env python3
"""The parts of the health check that talk to the GRID.

Separate from system-check.ps1 for one reason: the host's curl is built against Schannel, which
ignores --cacert, so a TLS-validated call from PowerShell would either fail or have to skip
verification. Nothing here skips verification -- every call validates against the GRID's own CA.

Each check prints one JSON object per line: {"id", "name", "status", "detail", "fix"}. The
PowerShell script renders them and decides the exit code, so the two agree on what a failure is.

Passwords are read from deployment/bench/credentials.local.md and never printed.
"""

from __future__ import annotations

import argparse
import json
import re
import ssl
import sys
import urllib.error
import urllib.request
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
CA = REPO / "deployment" / "grid" / "pki" / "ca.crt"
CREDENTIALS = REPO / "deployment" / "bench" / "credentials.local.md"

GRID = "https://192.168.56.1:8443"
CONSOLE = "http://localhost:5175"

PASS, FAIL, SKIP = "PASS", "FAIL", "NOT TESTABLE HERE"

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")


SECTION = "General"


def section(title: str) -> None:
    """The heading the following checks belong to."""
    global SECTION
    SECTION = title
    print(json.dumps({"section_heading": title}))


def emit(check_id: str, name: str, status: str, detail: str = "", fix: str = "") -> None:
    print(json.dumps({"id": check_id, "name": name, "status": status, "detail": detail,
                      "fix": fix, "section": SECTION}))


def context() -> ssl.SSLContext:
    return ssl.create_default_context(cafile=str(CA))


def call(method: str, path: str, body=None, token: str | None = None, base: str = GRID):
    data = json.dumps(body).encode() if body is not None else None
    request = urllib.request.Request(base + path, data=data, method=method)
    request.add_header("Content-Type", "application/json")
    if token:
        request.add_header("Authorization", "Bearer " + token)
    try:
        with urllib.request.urlopen(request, context=context(), timeout=30) as response:
            raw = response.read().decode()
            return response.status, (json.loads(raw) if raw else None)
    except urllib.error.HTTPError as error:
        raw = error.read().decode()
        try:
            return error.code, json.loads(raw)
        except json.JSONDecodeError:
            return error.code, raw


def accounts() -> list[tuple[str, str, str, str]]:
    """(role label, tenant code, email, password) from the credentials note.

    The note is the one place the bench writes passwords down, and it is gitignored. Reading them
    here keeps them out of the script, out of the report and out of the terminal.
    """
    if not CREDENTIALS.exists():
        return []

    found: list[tuple[str, str, str, str]] = []
    for line in CREDENTIALS.read_text(encoding="utf-8").splitlines():
        if not line.startswith("|") or "@" not in line:
            continue
        cells = [c.strip().strip("`") for c in line.strip("|").split("|")]
        if len(cells) < 4:
            continue
        email, role, tenant, password = cells[0], cells[1], cells[2], cells[3]
        if "@" not in email or " " in email:
            continue
        # The admin row carries a note after the password; keep the code-quoted part only.
        password = password.split("`")[0].strip() if "`" in password else password.split(" ")[0]
        found.append((role, tenant, email, password))
    return found


def check_health() -> None:
    try:
        status, body = call("GET", "/api/v1/health")
        if status == 200 and isinstance(body, dict) and body.get("status") == "ok":
            emit("grid.health", "GRID answers over validated TLS", PASS,
                 f"version {body.get('version')}")
        else:
            emit("grid.health", "GRID answers over validated TLS", FAIL, f"HTTP {status}",
                 "check `docker ps` and the grid-api logs")
    except Exception as error:  # noqa: BLE001 - any failure here is a failure to report
        emit("grid.health", "GRID answers over validated TLS", FAIL, str(error)[:160],
             "is the GRID up, and is deployment/grid/pki/ca.crt the CA it serves?")


def check_console() -> None:
    try:
        with urllib.request.urlopen(CONSOLE, timeout=15) as response:
            if response.status == 200:
                emit("console.reachable", "Console answers on localhost:5175", PASS)
                return
            emit("console.reachable", "Console answers on localhost:5175", FAIL,
                 f"HTTP {response.status}", "start it with bash ~/grid-local/start-console-real.sh")
    except Exception as error:  # noqa: BLE001
        emit("console.reachable", "Console answers on localhost:5175", FAIL, str(error)[:160],
             "start it with bash ~/grid-local/start-console-real.sh")


def check_sign_in() -> str | None:
    """Signs in as each bench account. Returns an admin token for the checks that need one."""
    people = accounts()
    if not people:
        emit("auth.accounts", "The bench accounts can sign in", FAIL,
             "deployment/bench/credentials.local.md is missing or has no account table",
             "that file is gitignored; it has to exist on this machine")
        return None

    admin_token = None
    for role, tenant, email, password in people:
        status, body = call("POST", "/api/v1/auth/login",
                            {"email": email, "password": password, "tenant_code": tenant})
        name = f"Sign-in: {email} ({role})"
        if status == 200 and isinstance(body, dict) and body.get("access_token"):
            emit(f"auth.{email}", name, PASS, f"tenant {tenant}")
            if role == "tenant_admin" and tenant.startswith("yde"):
                admin_token = body["access_token"]
        else:
            emit(f"auth.{email}", name, FAIL, f"HTTP {status}",
                 "check the password in credentials.local.md, and that the account is enabled")
    return admin_token


def check_agents(token: str | None) -> list[dict]:
    if not token:
        emit("agents.list", "Workstations as the GRID reports them", FAIL,
             "no admin token (sign-in failed)", "fix the sign-in failure above first")
        return []

    status, body = call("GET", "/api/v1/dashboard/agents", token=token)
    if status != 200 or not isinstance(body, dict):
        emit("agents.list", "Workstations as the GRID reports them", FAIL, f"HTTP {status}", "")
        return []

    agents = body.get("items", [])
    if not agents:
        emit("agents.list", "Workstations as the GRID reports them", FAIL,
             "no workstation is enrolled",
             "install the agent on a bench VM (docs/OWNER_TEST_GUIDE.md, steps 6 and 7)")
        return []

    emit("agents.list", "Workstations as the GRID reports them", PASS,
         f"{len(agents)} enrolled (decommissioned ones hidden)")

    detailed = []
    for agent in agents:
        host = agent.get("hostname")
        state = agent.get("status")
        posture = agent.get("protection_state")
        reasons = ", ".join(agent.get("reason_codes") or []) or "none"
        check_status = PASS if state == "active" else FAIL
        emit(f"agent.{host}", f"  {host}: {state}, protection {posture}", check_status,
             f"reasons: {reasons}",
             "" if check_status == PASS else "is the workstation on, and can it reach the GRID?")

        status, detail = call("GET", f"/api/v1/dashboard/agents/{agent['id']}", token=token)
        if status == 200 and isinstance(detail, dict):
            detailed.append(detail)
            modules = (detail.get("posture") or {}).get("modules", [])
            for module in modules:
                code, mstate = module.get("code"), module.get("state")
                reason = module.get("reason_code")
                label = f"    {host} / {code}: {mstate}" + (f" ({reason})" if reason else "")
                if mstate == "active":
                    emit(f"module.{host}.{code}", label, PASS)
                elif code == "ANTI_TAMPERING" and reason == "not_implemented":
                    emit(f"module.{host}.{code}", label, "NOT BUILT",
                         "the watchdog has not been written")
                elif code == "EXFIL_WATCH" and reason == "learning_phase":
                    emit(f"module.{host}.{code}", label, SKIP,
                         "it learns the normal traffic before it can judge any")
                else:
                    emit(f"module.{host}.{code}", label, FAIL, f"reason {reason}",
                         "open the agent page in the console for what it says it is missing")

            rejected = (detail.get("posture") or {}).get("rejected_alerts")
            if rejected:
                emit(f"agent.{host}.rejected", f"  {host}: alerts the GRID refused", FAIL,
                     f"{rejected} rejected",
                     "the agent and the GRID disagree on an alert's shape; check the agent log")
            else:
                emit(f"agent.{host}.rejected", f"  {host}: no alert was refused", PASS)
    return detailed


def check_installer(token: str | None) -> None:
    if not token:
        emit("installer.download", "The console can hand out an installer", FAIL,
             "no admin token", "fix the sign-in failure above first")
        return

    status, body = call("POST", "/api/v1/dashboard/agents/provision",
                        {"hostname": "system-check-probe"}, token=token)
    if status != 200 or not isinstance(body, dict) or not body.get("otp"):
        emit("installer.download", "The console can hand out an installer", FAIL,
             f"provision returned HTTP {status}",
             "check that installer parts are published (deployment/grid/publish-local-installer.ps1)")
        return

    minutes = body.get("expires_in_minutes")
    sha = body.get("installer_sha256") or body.get("agent_package_sha256")
    emit("installer.code", "An enrollment code is issued", PASS,
         f"valid {minutes} minutes, single use")

    request = urllib.request.Request(
        GRID + "/api/v1/dashboard/agents/installer", method="POST",
        data=json.dumps({"host": "192.168.56.1", "port": 443, "otp": body["otp"]}).encode())
    request.add_header("Content-Type", "application/json")
    request.add_header("Authorization", "Bearer " + token)
    try:
        with urllib.request.urlopen(request, context=context(), timeout=120) as response:
            disposition = response.headers.get("content-disposition", "")
            size = 0
            head = b""
            while chunk := response.read(1 << 20):
                if not head:
                    head = chunk[:2]
                size += len(chunk)
    except Exception as error:  # noqa: BLE001
        emit("installer.download", "The console can hand out an installer", FAIL, str(error)[:160],
             "run deployment/grid/publish-local-installer.ps1 and try again")
        return

    name = re.search(r'filename="([^"]+)"', disposition)
    looks_like_exe = head == b"MZ"
    if size > 50_000_000 and looks_like_exe:
        emit("installer.download", "The console can hand out an installer", PASS,
             f"{name.group(1) if name else 'installer'}, {size / 1_000_000:.1f} MB, valid PE header"
             + (f", sha256 {sha[:12]}..." if sha else ""))
    else:
        emit("installer.download", "The console can hand out an installer", FAIL,
             f"{size} bytes, PE header {'yes' if looks_like_exe else 'no'}",
             "run deployment/grid/publish-local-installer.ps1")


def check_isolation(token: str | None) -> None:
    """One customer must not see another's workstations."""
    people = accounts()
    clinic = next(((r, t, e, p) for r, t, e, p in people if not t.startswith("yde")), None)
    if not clinic or not token:
        emit("saas.isolation", "One customer sees nothing of another", SKIP,
             "needs both a hospital admin and a second-customer admin", "")
        return

    _, tenant, email, password = clinic
    status, body = call("POST", "/api/v1/auth/login",
                        {"email": email, "password": password, "tenant_code": tenant})
    if status != 200:
        emit("saas.isolation", "One customer sees nothing of another", FAIL,
             f"the second customer could not sign in (HTTP {status})", "")
        return

    other = body["access_token"]
    _, their_agents = call("GET", "/api/v1/dashboard/agents", token=other)
    _, their_alerts = call("GET", "/api/v1/dashboard/alerts", token=other)
    _, our_agents = call("GET", "/api/v1/dashboard/agents", token=token)

    theirs = (their_agents or {}).get("total", -1)
    alerts = (their_alerts or {}).get("total", -1)
    ours = (our_agents or {}).get("total", -1)

    if theirs == 0 and alerts == 0 and ours > 0:
        emit("saas.isolation", "One customer sees nothing of another", PASS,
             f"{tenant}: 0 workstations, 0 alerts; the hospital: {ours} workstation(s)")
    else:
        emit("saas.isolation", "One customer sees nothing of another", FAIL,
             f"{tenant} sees {theirs} workstation(s) and {alerts} alert(s)",
             "this is a tenant-scoping bug and is serious; stop and report it")


def check_roles(token: str | None) -> None:
    """The auditor must not be able to change anything."""
    people = accounts()
    auditor = next(((r, t, e, p) for r, t, e, p in people if "auditor" in e), None)
    if not auditor or not token:
        emit("roles.auditor", "The auditor can read but not act", SKIP, "no auditor account", "")
        return

    _, tenant, email, password = auditor
    status, body = call("POST", "/api/v1/auth/login",
                        {"email": email, "password": password, "tenant_code": tenant})
    if status != 200:
        emit("roles.auditor", "The auditor can read but not act", FAIL,
             f"the auditor could not sign in (HTTP {status})", "")
        return

    read, _ = call("GET", "/api/v1/dashboard/alerts", token=body["access_token"])
    write, _ = call("POST", "/api/v1/dashboard/users",
                    {"email": "should-not-exist@yde-hospital.cm", "full_name": "probe",
                     "password": "NeverCreated!2026", "roles": ["read_only_auditor"]},
                    token=body["access_token"])

    if read == 200 and write == 403:
        emit("roles.auditor", "The auditor can read but not act", PASS,
             "reads alerts (200), refused creating a user (403)")
    else:
        emit("roles.auditor", "The auditor can read but not act", FAIL,
             f"read {read}, write {write} (write must be 403)",
             "this is a permissions bug and is serious; stop and report it")


def not_built() -> None:
    for check_id, name, why in [
        ("missing.anti_tampering", "Anti-tampering (watchdog)",
         "the services are registered but never started; the agent reports it as not implemented"),
        ("missing.mtls_per_agent", "A client certificate per agent",
         "agents authenticate with an enrollment token, not a per-agent certificate"),
        ("missing.installer_signing", "A signed installer",
         "Windows shows 'Publisher: Unknown' on the UAC prompt"),
        ("missing.ironclad_hw", "IRONCLAD against real hardware",
         "the code exists and is tested against a simulated Arduino only"),
    ]:
        emit(check_id, name, "NOT BUILT", why)

    for check_id, name, why in [
        ("skip.usb", "USB GUARD against a real key",
         "needs a USB device passed through to the VM; the module itself reports active"),
        ("skip.exfil", "EXFIL WATCH raising an alert",
         "it learns the normal traffic first and reports learning_phase until it has a baseline"),
        ("skip.deployed", "The deployed server",
         "OPS-001: the Tailscale server is not touched from here"),
    ]:
        emit(check_id, name, SKIP, why)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--section", default="all",
                        choices=["all", "platform", "accounts", "agents", "installer", "missing"])
    args = parser.parse_args()

    # Signed in once for the whole run. Signing in per section meant sixteen logins in a few
    # seconds, which the session store answered with 503 -- a failure the checker caused itself
    # and then reported as a failure of the platform.
    token = None

    if args.section in ("all", "platform"):
        section("The platform answers")
        check_health()
        check_console()

    if args.section in ("all", "accounts", "agents", "installer"):
        section("The bench accounts, isolation and roles")
        token = check_sign_in()

    if args.section in ("all", "accounts"):
        check_isolation(token)
        check_roles(token)

    if args.section in ("all", "agents"):
        section("The workstations and what each module reports")
        check_agents(token)

    if args.section in ("all", "installer"):
        section("The installer the console hands out")
        check_installer(token)

    if args.section in ("all", "missing"):
        section("Not built, and not testable on this bench")
        not_built()

    return 0


if __name__ == "__main__":
    sys.exit(main())
