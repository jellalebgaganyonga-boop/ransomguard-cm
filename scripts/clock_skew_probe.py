"""How far each workstation's clock is from the GRID's.

The bench VMs have no Guest Additions, so nothing keeps their clocks with the host's: they
free-run and are corrected by hand. That matters more than it sounds. The time an alert was
detected is the workstation's own and travels with it, so a drifted workstation makes every alert
on the console look hours old -- and until 2026-10-28 it blinded attribution outright, because
the ETW session's records and ``Process.StartTime`` ended up in different frames and every writer
looked like a recycled PID.

The figure comes from the heartbeat, which carries the workstation's own clock. That is the one
thing a workstation sends whether or not anything has happened on it. Reading the drift from an
alert or an audit entry instead only works while something is being recorded: a quiet morning
reports the skew of whatever was recorded last, which may well be from before the clock was
corrected -- the first version of this probe did exactly that and reported three hours of drift
on a workstation whose clock was right.

An agent too old to report its clock says so, and is not counted as agreeing.

    python scripts/clock_skew_probe.py [--max-seconds 120]
"""
import argparse
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from system_check_probe import accounts, call  # noqa: E402


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--max-seconds", type=int, default=120)
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

    out_of_step = False
    for agent in live:
        detail = call("GET", f"/api/v1/dashboard/agents/{agent['id']}", token=token)[1]
        posture = detail.get("posture") or {}
        skew = posture.get("clock_skew_seconds")

        if skew is None:
            # Said, never assumed. A silent pass here is how a drifted clock reaches a demo.
            print(f"{agent['hostname']:14} does not report its clock "
                  f"(agent older than 2026-10-28): NOT measured")
            continue

        behind_or_ahead = "behind" if skew > 0 else "ahead of"
        if abs(skew) > args.max_seconds:
            out_of_step = True
            print(f"{agent['hostname']:14} {abs(skew):6d}s {behind_or_ahead} the GRID   OUT OF STEP")
        else:
            print(f"{agent['hostname']:14} {abs(skew):6d}s {behind_or_ahead} the GRID   ok")

    if out_of_step:
        print()
        print(f"A workstation clock is more than {args.max_seconds}s from the GRID's.")
        print("In the VM, as administrator:")
        print('  Set-Date -Date "<the correct local time>"   (or Settings > Time & language)')
        print("then restart the VM, so the agent's ETW session is opened against the new clock.")
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
