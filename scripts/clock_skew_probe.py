"""How far each workstation's clock is from the GRID's.

The bench VMs have no Guest Additions, so nothing keeps their clocks with the host's: they
free-run and are corrected by hand. That matters more than it sounds. The time an alert was
detected is the workstation's own and travels with it, so a drifted workstation makes every alert
on the console look hours old -- and until 2026-10-28 it also blinded attribution outright,
because the ETW session's records and ``Process.StartTime`` ended up in different frames.

There is no way to ask the guest for its clock from the host without Guest Additions. There is a
better signal anyway: every audit entry carries the time the workstation recorded it
(``payload.created_at``, the agent's clock) beside the time the GRID received it
(``received_at``, the server's). The agent uploads every thirty seconds, so there is always a
recent pair, and the difference between them is the skew minus the delivery delay -- which is
seconds, against a drift measured in hours.

Only a promptly delivered entry can be used. The agent sends its backlog when it first starts
uploading, and those entries were recorded hours before they arrived -- the difference then
measures the backlog, not the clock. So the newest entry is trusted only if the GRID received it
recently; otherwise this says it could not measure, which is a different answer from "fine".

A workstation that has just been restarted always has fresh entries, and restarting is the
second half of the fix for a drifted clock anyway (the ETW session has to be reopened against the
corrected clock).

Prints one line per agent. Exits 1 if any clock is out of step, 0 otherwise.

    python scripts/clock_skew_probe.py [--max-seconds 120] [--fresh-minutes 30]
"""
import argparse
import sys
from datetime import datetime, timezone
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from system_check_probe import accounts, call  # noqa: E402


def _parse(value: str | None) -> datetime | None:
    if not value:
        return None
    text = value.replace("Z", "+00:00")
    try:
        parsed = datetime.fromisoformat(text)
    except ValueError:
        return None
    return parsed if parsed.tzinfo else parsed.replace(tzinfo=timezone.utc)


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--max-seconds", type=int, default=120)
    ap.add_argument("--fresh-minutes", type=int, default=30,
                    help="how recently the newest entry must have arrived to be trusted")
    args = ap.parse_args()

    role, tenant, email, password = next(
        a for a in accounts() if a[0] == "tenant_admin" and a[1].startswith("yde"))
    token = call("POST", "/api/v1/auth/login",
                 {"email": email, "password": password, "tenant_code": tenant})[1]["access_token"]

    agents = call("GET", "/api/v1/dashboard/agents?include_decommissioned=false",
                  token=token)[1].get("items", [])
    live = [a for a in agents if a.get("status") != "decommissioned"]
    if not live:
        print("no workstation is enrolled; nothing to compare")
        return 0

    worst = 0
    for agent in live:
        rows = call("GET", f"/api/v1/dashboard/audit-logs?agent_id={agent['id']}&offset=0&limit=1",
                    token=token)[1].get("items", [])
        if not rows:
            # Said, never assumed: an agent that has uploaded nothing cannot be measured, and a
            # silent pass here is how a drifted clock reaches a demonstration.
            print(f"{agent['hostname']:14} no audit entry yet: clock NOT measured")
            continue

        entry = rows[0]
        on_workstation = _parse(entry.get("recorded_at"))
        at_the_grid = _parse(entry.get("received_at"))
        if on_workstation is None or at_the_grid is None:
            print(f"{agent['hostname']:14} audit entry carries no usable time: clock NOT measured")
            continue

        age = (datetime.now(timezone.utc) - at_the_grid).total_seconds()
        if age > args.fresh_minutes * 60:
            print(f"{agent['hostname']:14} newest entry arrived {int(age / 60)} min ago: "
                  f"clock NOT measured (restart the VM, or run the trigger, to get a fresh one)")
            continue

        skew = int((at_the_grid - on_workstation).total_seconds())
        worst = max(worst, abs(skew))
        verdict = "ok" if abs(skew) <= args.max_seconds else "OUT OF STEP"
        behind_or_ahead = "behind" if skew > 0 else "ahead of"
        print(f"{agent['hostname']:14} {abs(skew):6d}s {behind_or_ahead} the GRID   {verdict}")

    if worst > args.max_seconds:
        print()
        print(f"A workstation clock is more than {args.max_seconds}s from the GRID's.")
        print("In the VM, as administrator:")
        print('  Set-Date -Date "<the correct local time>"   (or Settings > Time & language)')
        print("then restart the VM, so the agent's ETW session is opened against the new clock.")
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
