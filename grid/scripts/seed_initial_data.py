"""Seed initial data: default tenant, 3 roles, admin user."""

import asyncio
import os
import sys
from datetime import UTC, datetime
from uuid import uuid4

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "src"))

from dotenv import load_dotenv

load_dotenv()

from ransomguard_grid.core.security import hash_password
from ransomguard_grid.db.models.enums import TenantStatus
from ransomguard_grid.db.models.tenant_user import Role, Tenant, User, UserRole
from ransomguard_grid.db.session import AsyncSessionLocal


async def seed() -> None:
    """Insert default tenant, roles, and admin user."""
    async with AsyncSessionLocal() as session:
        default_tenant = Tenant(
            id=str(uuid4()),
            name="Default Tenant",
            code="default",
            contact_email="admin@example.local",
            status=TenantStatus.active,
            created_at=datetime.now(UTC),
        )
        session.add(default_tenant)

        roles = [
            Role(id=str(uuid4()), name="tenant_admin", description="Full administrative access within tenant"),
            Role(id=str(uuid4()), name="security_analyst", description="Read alerts and update status"),
            Role(id=str(uuid4()), name="read_only_auditor", description="Read-only audit access"),
        ]
        for role in roles:
            session.add(role)
        await session.flush()

        admin = User(
            id=str(uuid4()),
            tenant_id=default_tenant.id,
            email="admin@example.local",
            hashed_password=hash_password("ChangeMe123!"),
            full_name="Default Administrator",
            is_active=True,
            created_at=datetime.now(UTC),
        )
        session.add(admin)
        await session.flush()

        session.add(
            UserRole(
                user_id=admin.id,
                role_id=roles[0].id,
                granted_at=datetime.now(UTC),
                granted_by_user_id=admin.id,
            )
        )

        await session.commit()
        print(f"[OK] Seeded tenant_id={default_tenant.id}")
        print(f"[OK] Admin email: {admin.email}")
        print(f"[OK] Admin password: ChangeMe123! (change immediately)")
        print(f"[OK] Roles: tenant_admin, security_analyst, read_only_auditor")


if __name__ == "__main__":
    asyncio.run(seed())
