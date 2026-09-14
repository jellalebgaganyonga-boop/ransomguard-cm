"""Seed initial data: default tenant, 3 roles, admin user.

Idempotent: every entity is created only when missing, so the script is safe to
re-run on every deployment (the CD pipeline calls it after `alembic upgrade`).

Credentials come from the environment so that production never ships with the
development password:

    GRID_SEED_TENANT_NAME     (default: "Default Tenant")
    GRID_SEED_TENANT_CODE     (default: "default")      -- used at login
    GRID_SEED_TENANT_EMAIL    (default: "admin@example.local")
    GRID_SEED_ADMIN_EMAIL     (default: "admin@example.local")
    GRID_SEED_ADMIN_NAME      (default: "Default Administrator")
    GRID_SEED_ADMIN_PASSWORD  (default: "ChangeMe123!" -- refused when
                               GRID_ENVIRONMENT=production)

One account per remaining role is created when its password is provided, so a
fresh installation can be exercised against the full RBAC matrix:

    GRID_SEED_ANALYST_EMAIL / GRID_SEED_ANALYST_PASSWORD   (security_analyst)
    GRID_SEED_AUDITOR_EMAIL / GRID_SEED_AUDITOR_PASSWORD   (read_only_auditor)
"""

import asyncio
import os
import sys
from datetime import UTC, datetime
from uuid import uuid4

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "src"))

from dotenv import load_dotenv

load_dotenv()

from sqlalchemy import select

from ransomguard_grid.core.security import hash_password
from ransomguard_grid.db.models.enums import TenantStatus
from ransomguard_grid.db.models.tenant_user import Role, Tenant, User, UserRole
from ransomguard_grid.db.session import AsyncSessionLocal

DEV_PASSWORD = "ChangeMe123!"

ROLE_DEFINITIONS = [
    ("tenant_admin", "Full administrative access within tenant"),
    ("security_analyst", "Read alerts and update status"),
    ("read_only_auditor", "Read-only audit access"),
]


async def seed() -> None:
    """Insert default tenant, roles, and admin user when they do not exist."""
    tenant_name = os.getenv("GRID_SEED_TENANT_NAME", "Default Tenant")
    tenant_code = os.getenv("GRID_SEED_TENANT_CODE", "default")
    tenant_email = os.getenv("GRID_SEED_TENANT_EMAIL", "admin@example.local")
    admin_email = os.getenv("GRID_SEED_ADMIN_EMAIL", "admin@example.local")
    admin_name = os.getenv("GRID_SEED_ADMIN_NAME", "Default Administrator")
    admin_password = os.getenv("GRID_SEED_ADMIN_PASSWORD", DEV_PASSWORD)

    if admin_password == DEV_PASSWORD and os.getenv("GRID_ENVIRONMENT") == "production":
        print(
            "[FAIL] GRID_SEED_ADMIN_PASSWORD must be set to a real password when "
            "GRID_ENVIRONMENT=production.",
            file=sys.stderr,
        )
        sys.exit(1)

    created = []

    async with AsyncSessionLocal() as session:
        # ── Tenant ────────────────────────────────────────────────
        tenant = (
            await session.execute(select(Tenant).where(Tenant.code == tenant_code))
        ).scalar_one_or_none()
        if tenant is None:
            tenant = Tenant(
                id=str(uuid4()),
                name=tenant_name,
                code=tenant_code,
                contact_email=tenant_email,
                status=TenantStatus.active,
                created_at=datetime.now(UTC),
            )
            session.add(tenant)
            await session.flush()
            created.append(f"tenant {tenant_code}")

        # ── Roles ─────────────────────────────────────────────────
        roles: dict[str, Role] = {}
        for name, description in ROLE_DEFINITIONS:
            role = (
                await session.execute(select(Role).where(Role.name == name))
            ).scalar_one_or_none()
            if role is None:
                role = Role(id=str(uuid4()), name=name, description=description)
                session.add(role)
                await session.flush()
                created.append(f"role {name}")
            roles[name] = role

        # ── Admin user ────────────────────────────────────────────
        admin = (
            await session.execute(select(User).where(User.email == admin_email))
        ).scalar_one_or_none()
        if admin is None:
            admin = User(
                id=str(uuid4()),
                tenant_id=tenant.id,
                email=admin_email,
                hashed_password=hash_password(admin_password),
                full_name=admin_name,
                is_active=True,
                created_at=datetime.now(UTC),
            )
            session.add(admin)
            await session.flush()
            created.append(f"user {admin_email}")

        # ── Admin role binding ────────────────────────────────────
        binding = (
            await session.execute(
                select(UserRole).where(
                    UserRole.user_id == admin.id,
                    UserRole.role_id == roles["tenant_admin"].id,
                )
            )
        ).scalar_one_or_none()
        if binding is None:
            session.add(
                UserRole(
                    user_id=admin.id,
                    role_id=roles["tenant_admin"].id,
                    granted_at=datetime.now(UTC),
                    granted_by_user_id=admin.id,
                )
            )
            created.append("role binding tenant_admin")

        # ── Additional role accounts ──────────────────────────────
        # Created only when a password is supplied; an installation that does
        # not want them simply leaves the variables empty.
        for role_name, prefix, default_name in (
            ("security_analyst", "ANALYST", "Analyste securite"),
            ("read_only_auditor", "AUDITOR", "Auditeur"),
        ):
            password = os.getenv(f"GRID_SEED_{prefix}_PASSWORD", "")
            if not password:
                continue
            email = os.getenv(
                f"GRID_SEED_{prefix}_EMAIL", f"{role_name.split('_')[0]}@ransomguard.local"
            )
            user = (
                await session.execute(select(User).where(User.email == email))
            ).scalar_one_or_none()
            if user is None:
                user = User(
                    id=str(uuid4()),
                    tenant_id=tenant.id,
                    email=email,
                    hashed_password=hash_password(password),
                    full_name=os.getenv(f"GRID_SEED_{prefix}_NAME", default_name),
                    is_active=True,
                    created_at=datetime.now(UTC),
                )
                session.add(user)
                await session.flush()
                created.append(f"user {email}")

            bound = (
                await session.execute(
                    select(UserRole).where(
                        UserRole.user_id == user.id,
                        UserRole.role_id == roles[role_name].id,
                    )
                )
            ).scalar_one_or_none()
            if bound is None:
                session.add(
                    UserRole(
                        user_id=user.id,
                        role_id=roles[role_name].id,
                        granted_at=datetime.now(UTC),
                        granted_by_user_id=admin.id,
                    )
                )
                created.append(f"role binding {role_name}")

        await session.commit()

    if created:
        print(f"[OK] Created: {', '.join(created)}")
    else:
        print("[OK] Nothing to do -- database already seeded")
    print(f"[OK] Tenant code (login field): {tenant_code}")
    print(f"[OK] Admin email: {admin_email}")
    if admin_password == DEV_PASSWORD:
        print("[WARN] Admin password is the development default -- change it now")


if __name__ == "__main__":
    asyncio.run(seed())
