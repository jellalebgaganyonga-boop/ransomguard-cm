"""What the GRID says about a workstation, for the offline bench test.

status      one line: the workstation's state and how old its signal is
alerts      every alert for it, with the time it was DETECTED on the workstation and the time it
            REACHED the GRID -- the whole point of the test is that the first survives the gap
suspensions what the console would show in Suspended processes
resume      press Resume on whatever is frozen

    python deployment/bench/offline_probe.py alerts PC-LAB
"""
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "scripts"))
from system_check_probe import accounts, call  # noqa: E402

WHAT = sys.argv[1] if len(sys.argv) > 1 else "status"
HOSTNAME = sys.argv[2] if len(sys.argv) > 2 else "PC-LAB"

r, t, e, p = next(a for a in accounts() if a[0] == "tenant_admin" and a[1].startswith("yde"))


def token() -> str:
    return call("POST", "/api/v1/auth/login",
                {"email": e, "password": p, "tenant_code": t})[1]["access_token"]


def agent(tok):
    rows = call("GET", "/api/v1/dashboard/agents?include_decommissioned=false", token=tok)[1]
    for a in rows.get("items", []):
        if a["hostname"] == HOSTNAME:
            return a
    return None


def main() -> int:
    tok = token()
    a = agent(tok)
    if a is None:
        print(f"{HOSTNAME} is not in the console")
        return 1

    if WHAT == "status":
        print(f"{HOSTNAME} {a['status']}  signal {a.get('signal_age_seconds')}s  "
              f"protection {a.get('protection_state')}")
        return 0

    if WHAT == "alerts":
        items = call("GET", f"/api/v1/dashboard/alerts?agent_id={a['id']}&page_size=60",
                     token=tok)[1].get("items", [])
        items.sort(key=lambda x: x.get("detected_at") or "")
        print(f"{'detected on the workstation':30} {'reached the GRID':22} alert")
        for x in items:
            print(f"{str(x.get('detected_at')):30} {str(x.get('ingested_at')):22} {x.get('alert_type')}")
        canaries = [x for x in items if x.get("alert_type") == "SentinelCanaryModified"]
        print(f"\n{len(items)} alert(s), {len(canaries)} SentinelCanaryModified")
        return 0

    if WHAT == "suspensions":
        rows = call("GET", f"/api/v1/dashboard/agents/{a['id']}/suspensions", token=tok)[1]
        rows = rows if isinstance(rows, list) else []
        if not rows:
            print("Suspended processes: empty")
            return 0
        for row in rows:
            print(f"frozen: {row.get('process_name')} (PID {row.get('process_id')})")
        return 0

    if WHAT == "resume":
        rows = call("GET", f"/api/v1/dashboard/agents/{a['id']}/suspensions", token=tok)[1]
        rows = rows if isinstance(rows, list) else []
        if not rows:
            print("nothing is frozen")
            return 0
        for row in rows:
            code, _ = call("POST", "/api/v1/dashboard/commands",
                           {"agent_id": a["id"], "command_type": "resume_process",
                            "parameters": {"alert_id": row.get("alert_id"),
                                           "suspension_id": row.get("suspension_id")}},
                           token=tok)
            print(f"Resume {row.get('process_name')} (PID {row.get('process_id')}) -> HTTP {code}")
        return 0

    print(f"unknown: {WHAT}")
    return 2


if __name__ == "__main__":
    sys.exit(main())
