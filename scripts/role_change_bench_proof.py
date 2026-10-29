"""Proves on the running GRID that a role change reaches a signed-in user at once.

The owner's question was when a role change takes effect for somebody who is already signed in.
The answer is "on their next request", and this shows it on the bench rather than only in the
test suite: a dedicated account signs in, does something only an analyst may do, is demoted by an
administrator, and is refused the very next time it tries -- holding the same token throughout.

The account is created on the first run and its password is written to the bench credentials note
(deployment/bench/credentials.local.md, gitignored), which is the one place the bench writes
passwords down. It is never printed here. Later runs reuse the account and the note.

    python scripts/role_change_bench_proof.py

It leaves the account as a security_analyst, ready for the demo.
"""
import secrets
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from system_check_probe import CREDENTIALS, accounts, call  # noqa: E402

BENCH_EMAIL = "bench-roles@hopital-yaounde.cm"
BENCH_NAME = "Bench Role Test"


def _admin() -> tuple[str, str, str, str]:
    for role, tenant, email, password in accounts():
        if role.startswith("tenant_admin") or role == "tenant_admin":
            return role, tenant, email, password
    raise SystemExit("no tenant_admin row in the bench credentials note")


def _login(tenant: str, email: str, password: str) -> str:
    status, body = call("POST", "/api/v1/auth/login",
                        {"email": email, "password": password, "tenant_code": tenant})
    if status != 200:
        raise SystemExit(f"sign-in failed for {email}: {status}")
    return body["access_token"]


def _bench_password(tenant: str) -> str:
    """The account's password from the note, or a new one written there."""
    for _role, row_tenant, email, password in accounts():
        if email == BENCH_EMAIL and row_tenant == tenant:
            return password

    # Generated here, written once, never printed. 24 url-safe characters plus the classes the
    # GRID's password policy asks for.
    password = secrets.token_urlsafe(18) + "aA1!"
    note = CREDENTIALS.read_text(encoding="utf-8")
    marker = "The tenant is"
    row = (f"| `{BENCH_EMAIL}` | security_analyst | {tenant} | `{password}` |\n\n"
           "The `bench-roles` account above exists only to show a role change taking effect while\n"
           "somebody is signed in (scripts/role_change_bench_proof.py). It is left as a\n"
           "security_analyst after each run.\n\n")
    if marker in note:
        note = note.replace(marker, row + marker, 1)
    else:
        note = note.rstrip() + "\n\n" + row
    CREDENTIALS.write_text(note, encoding="utf-8")
    print(f"created {BENCH_EMAIL}; its password is in {CREDENTIALS.name} (not printed)")
    return password


def _set_roles(admin_token: str, user_id: str, roles: list[str]) -> list[str]:
    status, body = call("PUT", f"/api/v1/dashboard/users/{user_id}/roles",
                        {"role_names": roles}, token=admin_token)
    if status != 200:
        raise SystemExit(f"role change refused: {status} {body}")
    return body["roles"]


def main() -> int:
    _role, tenant, admin_email, admin_password = _admin()
    admin = _login(tenant, admin_email, admin_password)

    password = _bench_password(tenant)

    status, body = call("GET", "/api/v1/dashboard/users?limit=200", token=admin)
    existing = next((u for u in body["items"] if u["email"] == BENCH_EMAIL), None)
    if existing is None:
        status, body = call("POST", "/api/v1/dashboard/users", {
            "email": BENCH_EMAIL, "password": password, "full_name": BENCH_NAME,
            "roles": ["security_analyst"],
        }, token=admin)
        if status != 201:
            raise SystemExit(f"could not create the bench account: {status} {body}")
        user_id = body["id"]
    else:
        user_id = existing["id"]
        _set_roles(admin, user_id, ["security_analyst"])

    # The account signs in once and keeps this token for the whole proof.
    token = _login(tenant, BENCH_EMAIL, password)
    me = call("GET", "/api/v1/dashboard/me", token=token)[1]
    print(f"signed in as {BENCH_EMAIL}: roles {me['roles']}")

    # Something only an analyst or an admin may do: move a New alert to Investigating.
    alerts = call("GET", "/api/v1/dashboard/alerts?status=New&limit=1", token=token)[1]
    if not alerts["items"]:
        print("no alert is in New, so the analyst action cannot be shown; "
              "trigger the harmless canary on a workstation first")
        return 1
    alert_id = alerts["items"][0]["id"]
    status, _ = call("POST", f"/api/v1/dashboard/alerts/{alert_id}/status",
                     {"new_status": "Investigating",
                      "justification": "bench proof that an analyst may triage"}, token=token)
    print(f"as security_analyst, triaging alert {alert_id[:8]}: HTTP {status}"
          f"{'  ok' if status == 200 else '  UNEXPECTED'}")
    if status != 200:
        return 1

    after = _set_roles(admin, user_id, ["read_only_auditor"])
    print(f"administrator changes the roles to {after}")

    # Same token, no sign-out, no refresh.
    status, _ = call("POST", f"/api/v1/dashboard/alerts/{alert_id}/status",
                     {"new_status": "Resolved",
                      "justification": "bench proof that an auditor may not"}, token=token)
    print(f"the same token tries again: HTTP {status}"
          f"{'  refused, as it must be' if status == 403 else '  UNEXPECTED'}")
    refused = status == 403

    me = call("GET", "/api/v1/dashboard/me", token=token)[1]
    reads = call("GET", "/api/v1/dashboard/alerts?limit=1", token=token)[0]
    print(f"still signed in, roles now {me['roles']}, and can still read (HTTP {reads})")

    _set_roles(admin, user_id, ["security_analyst"])
    print("left as security_analyst")

    if not (refused and me["roles"] == ["read_only_auditor"] and reads == 200):
        return 1
    print("PASS: the demotion took effect on the next request, with no sign-out")
    return 0


if __name__ == "__main__":
    sys.exit(main())
