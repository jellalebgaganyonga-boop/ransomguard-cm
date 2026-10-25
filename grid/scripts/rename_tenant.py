"""Rename a tenant, and the accounts that belong to it, without losing anything.

A tenant's code is what people type to sign in, and a user's email is what they sign in with.
Both were French (`hopital-yde`, `admin@hopital-yde.cm`) and the system is English now. The
obvious way to change them -- create the new ones, delete the old -- would throw away everything
attached to the row: the agents enrolled under that tenant, their alerts, the audit chain, who
did what and when. So the rows are renamed in place and keep their ids, and every one of those
links still points at them.

Nothing else changes. Passwords are untouched, roles are untouched, and an agent already
enrolled stays enrolled: it is tied to the tenant's id, not to its code.

It is safe to run twice. A tenant already carrying the new code is reported and left alone.

Run inside the API container, which already has the settings and the driver:

    docker exec grid-api python scripts/rename_tenant.py \
        --from-code hopital-yde --to-code yde-hospital \
        --name "Yaounde Regional Hospital" \
        --email admin@hopital-yde.cm=admin@yde-hospital.cm \
        --email analyste@hopital-yde.cm=analyst@yde-hospital.cm
"""

import argparse
import asyncio
import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "src"))

from dotenv import load_dotenv

load_dotenv()

from sqlalchemy import select  # noqa: E402

from ransomguard_grid.db.models.tenant_user import Tenant, User  # noqa: E402
from ransomguard_grid.db.session import AsyncSessionLocal  # noqa: E402


async def rename(from_code: str, to_code: str, name: str | None, emails: dict[str, str]) -> int:
    changed = 0

    async with AsyncSessionLocal() as session:
        tenant = (
            await session.execute(select(Tenant).where(Tenant.code == from_code))
        ).scalar_one_or_none()

        if tenant is None:
            tenant = (
                await session.execute(select(Tenant).where(Tenant.code == to_code))
            ).scalar_one_or_none()
            if tenant is None:
                print(f"[FAIL] no tenant with code {from_code!r} or {to_code!r}", file=sys.stderr)
                return 1
            print(f"[OK] tenant already renamed to {to_code!r}, nothing to do for the code")
        else:
            tenant.code = to_code
            changed += 1
            print(f"[OK] tenant code {from_code!r} -> {to_code!r} (id kept: {tenant.id})")

        if name and tenant.name != name:
            print(f"[OK] tenant name {tenant.name!r} -> {name!r}")
            tenant.name = name
            changed += 1

        for old_email, new_email in emails.items():
            old_email = old_email.strip().lower()
            new_email = new_email.strip().lower()

            user = (
                await session.execute(
                    select(User).where(User.tenant_id == tenant.id, User.email == old_email)
                )
            ).scalar_one_or_none()

            if user is None:
                already = (
                    await session.execute(
                        select(User).where(User.tenant_id == tenant.id, User.email == new_email)
                    )
                ).scalar_one_or_none()
                print(
                    f"[OK] {new_email} already renamed" if already
                    else f"[WARN] no user {old_email} in this tenant, skipped"
                )
                continue

            clash = (
                await session.execute(
                    select(User).where(User.tenant_id == tenant.id, User.email == new_email)
                )
            ).scalar_one_or_none()
            if clash is not None:
                print(f"[FAIL] {new_email} already exists in this tenant", file=sys.stderr)
                return 1

            user.email = new_email
            changed += 1
            print(f"[OK] user {old_email} -> {new_email} (id kept: {user.id}, password unchanged)")

        await session.commit()

    print(f"\n{changed} change(s) committed. Agents, alerts and audit history are untouched.")
    return 0


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--from-code", required=True)
    parser.add_argument("--to-code", required=True)
    parser.add_argument("--name", default=None, help="new display name for the tenant")
    parser.add_argument(
        "--email", action="append", default=[],
        help="old@example.cm=new@example.cm, repeatable",
    )
    args = parser.parse_args()

    emails: dict[str, str] = {}
    for pair in args.email:
        if "=" not in pair:
            print(f"[FAIL] --email wants old=new, got {pair!r}", file=sys.stderr)
            return 1
        old, new = pair.split("=", 1)
        emails[old] = new

    return asyncio.run(rename(args.from_code, args.to_code, args.name, emails))


if __name__ == "__main__":
    sys.exit(main())
