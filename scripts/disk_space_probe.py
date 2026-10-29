"""Is there room to run a demonstration -- on this machine and on every workstation?

A disk that fills does not degrade anything gracefully. On 2026-10-27 both bench VMs reached
zero bytes free: the agent could not write its database, the installer could not extract, and the
first symptom anybody saw was a workstation that had simply stopped saying anything. Nothing
checked for it, so it was found by hand, twice.

Three readings, because the demonstration needs all three:

  * this machine, where the GRID's containers, their images and the virtual machines' disks all
    live on the same volume;
  * each live workstation, from the free space its own agent reports in every heartbeat;
  * what the virtual machines could still grow to, which is the figure that turns "25 GB free"
    into "not enough" when two VMs are 10 GB short of their ceiling.

    python scripts/disk_space_probe.py [--host-min-gb 10] [--agent-min-gb 3]

Exit 1 when something is below its floor. A workstation that does not report its disk says so and
is never counted as a pass.
"""
import argparse
import shutil
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from system_check_probe import REPO, accounts, call  # noqa: E402

GB = 1024 ** 3


def _vm_headroom() -> list[tuple[str, float]]:
    """(name, gigabytes a VM's disk could still grow by), from VirtualBox's own figures."""
    import subprocess

    vbox = Path(r"C:\Program Files\Oracle\VirtualBox\VBoxManage.exe")
    if not vbox.exists():
        return []
    try:
        listing = subprocess.run([str(vbox), "list", "hdds", "--long"], capture_output=True,
                                 text=True, timeout=60).stdout
    except (OSError, subprocess.SubprocessError):
        return []

    headroom: list[tuple[str, float]] = []
    location = capacity = None
    for line in listing.splitlines():
        if line.startswith("Location:"):
            location = line.split(":", 1)[1].strip()
        elif line.startswith("Capacity:"):
            capacity = line.split(":", 1)[1].strip().split()[0]
        if location and capacity:
            path = Path(location)
            if path.exists() and ("PC-LAB" in location or "PC-RECEPTION" in location):
                could_grow = int(capacity) / 1024 - path.stat().st_size / GB
                headroom.append((path.parent.name, max(0.0, could_grow)))
            location = capacity = None
    return headroom


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--host-min-gb", type=float, default=10.0)
    ap.add_argument("--agent-min-gb", type=float, default=3.0)
    args = ap.parse_args()

    short = False

    free_gb = shutil.disk_usage(REPO).free / GB
    verdict = "ok" if free_gb >= args.host_min_gb else "LOW"
    if verdict == "LOW":
        short = True
    print(f"{'this machine':14} {free_gb:7.1f} GB free   {verdict}")

    for name, could_grow in _vm_headroom():
        print(f"{'  ' + name:14} {could_grow:7.1f} GB its disk could still grow by")

    try:
        role, tenant, email, password = next(
            a for a in accounts() if a[0] == "tenant_admin" and a[1].startswith("yde"))
    except StopIteration:
        print("no tenant_admin in the bench credentials note; workstations not checked")
        return 1 if short else 0

    token = call("POST", "/api/v1/auth/login",
                 {"email": email, "password": password, "tenant_code": tenant})[1]["access_token"]
    agents = call("GET", "/api/v1/dashboard/agents?include_decommissioned=false",
                  token=token)[1].get("items", [])
    live = [a for a in agents if a.get("status") != "decommissioned"]
    if not live:
        print("no workstation is enrolled; none to check")

    for agent in live:
        detail = call("GET", f"/api/v1/dashboard/agents/{agent['id']}", token=token)[1]
        posture = detail.get("posture") or {}
        free = posture.get("system_free_bytes")
        if free is None:
            # Said, never assumed: an agent that cannot measure its disk is not a workstation
            # with room on it.
            print(f"{agent['hostname']:14} does not report its disk: NOT measured")
            continue
        gb = free / GB
        if gb < args.agent_min_gb:
            short = True
            print(f"{agent['hostname']:14} {gb:7.1f} GB free   LOW")
        else:
            print(f"{agent['hostname']:14} {gb:7.1f} GB free   ok")

    if short:
        print()
        print(f"Below the floor ({args.host_min_gb:.0f} GB here, {args.agent_min_gb:.0f} GB on a "
              f"workstation). A disk that fills stops the agent writing, and the first thing")
        print("anybody notices is a workstation that has gone quiet. To make room on this machine:")
        print("  docker builder prune -f                 (the build cache, rebuilt on demand)")
        print("  docker image prune -f                   (images nothing uses)")
        print("  VBoxManage list hdds                    (the VMs that are not the bench's)")
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
