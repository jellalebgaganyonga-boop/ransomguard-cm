"""Download a double-click installer from a GRID, exactly as the console's "Download the installer".

Same three calls as the console: sign in, provision an enrollment code, ask for the installer
with that code and the agent channel address. TLS is validated against the GRID's own CA
(--ca), hostname included: nothing is skipped, and nothing is added to the machine's store.

Credentials come from the environment and are required -- no default account:
    RG_TENANT, RG_ADMIN_EMAIL, RG_ADMIN_PASSWORD

    python fetch_installer.py --grid https://192.168.56.1:8443 --agent-host 192.168.56.1 \
        --agent-port 443 --ca ../grid/pki/ca.crt --out ./media

The enrollment code inside the file is valid 30 minutes: install within that window.
Prints the file's path, size and SHA-256, and checks its RGINST01 trailer before saying OK.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import ssl
import struct
import sys
import urllib.error
import urllib.request


def _env(name: str) -> str:
    value = os.environ.get(name)
    if not value:
        raise SystemExit(f"{name} is not set (required: RG_TENANT, RG_ADMIN_EMAIL, RG_ADMIN_PASSWORD).")
    return value


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--grid", required=True, help="console URL of the GRID, e.g. https://192.168.56.1:8443")
    parser.add_argument("--agent-host", required=True, help="host the VM reaches the GRID on")
    parser.add_argument("--agent-port", required=True, type=int, help="agent channel port (nginx agent.conf: 443)")
    parser.add_argument("--ca", required=True, help="the GRID's CA certificate (PEM)")
    parser.add_argument("--out", required=True, help="directory to write the installer into")
    args = parser.parse_args()

    ctx = ssl.create_default_context(cafile=args.ca)  # CERT_REQUIRED + hostname check

    def call(method: str, path: str, body: dict | None = None, token: str | None = None):
        headers = {"Content-Type": "application/json"}
        if token:
            headers["Authorization"] = f"Bearer {token}"
        request = urllib.request.Request(
            args.grid.rstrip("/") + path, method=method, headers=headers,
            data=None if body is None else json.dumps(body).encode(),
        )
        try:
            with urllib.request.urlopen(request, context=ctx, timeout=300) as response:
                return response.headers, response.read()
        except urllib.error.HTTPError as error:
            raise SystemExit(f"{method} {path} -> HTTP {error.code}: {error.read()[:300]!r}") from None

    _, raw = call("POST", "/api/v1/auth/login", {
        "tenant_code": _env("RG_TENANT"), "email": _env("RG_ADMIN_EMAIL"), "password": _env("RG_ADMIN_PASSWORD"),
    })
    token = json.loads(raw)["access_token"]
    _, raw = call("POST", "/api/v1/dashboard/agents/provision", {}, token)
    otp = json.loads(raw)["otp"]
    headers, blob = call("POST", "/api/v1/dashboard/agents/installer",
                         {"otp": otp, "host": args.agent_host, "port": args.agent_port}, token)

    disposition = headers.get("Content-Disposition") or ""
    name = disposition.split('filename="')[1].rstrip('"') if 'filename="' in disposition \
        else f"RansomGuard-CM-Installeur-{args.agent_host}.exe"

    # The stub refuses a payload whose trailer or SHA-256 is wrong; check it here first.
    if blob[-56:-48] != b"RGINST01":
        raise SystemExit("the download has no RGINST01 trailer: not an installer")
    zip_len, cfg_len = struct.unpack("<qq", blob[-48:-32])
    if hashlib.sha256(blob[-56 - cfg_len - zip_len:-56]).digest() != blob[-32:]:
        raise SystemExit("the installer's inner SHA-256 does not match: download again")
    config = json.loads(blob[-56 - cfg_len:-56])

    os.makedirs(args.out, exist_ok=True)
    path = os.path.join(args.out, name)
    with open(path, "wb") as handle:
        handle.write(blob)

    print(f"installer     : {path}")
    print(f"size          : {len(blob):,} bytes")
    print(f"sha256        : {hashlib.sha256(blob).hexdigest()}")
    print(f"grid_base_url : {config['grid_base_url']}")
    print(f"code expires  : {config['otp_expires_at']}  (install before this)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
