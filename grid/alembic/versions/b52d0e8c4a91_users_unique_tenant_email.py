"""Users: one account per (tenant, email), emails stored lower-case (GRID-USR-001)

Revision ID: b52d0e8c4a91
Revises: 81e3f05713a7
Create Date: 2026-10-14 16:00:00.000000

Nothing prevented two accounts with the same email in a tenant; login then failed for both
(find_by_email -> scalar_one_or_none raises on two rows, 500). The upgrade:

1. looks for duplicates on (tenant_id, lower(trim(email))) and REFUSES to run if there are any,
   listing them -- two accounts are never merged silently: a person decides which one stays;
2. stores every email lower-case and trimmed;
3. adds the unique constraint uq_users_tenant_email (tenant_id, email).
"""

from collections.abc import Sequence

import sqlalchemy as sa
from alembic import op

revision: str = "b52d0e8c4a91"
down_revision: str | None = "81e3f05713a7"
branch_labels: str | Sequence[str] | None = None
depends_on: str | Sequence[str] | None = None


def upgrade() -> None:
    bind = op.get_bind()
    duplicates = bind.execute(sa.text(
        "SELECT tenant_id, LOWER(TRIM(email)) AS normalized, COUNT(*) AS n FROM users "
        "GROUP BY tenant_id, LOWER(TRIM(email)) HAVING COUNT(*) > 1"
    )).fetchall()
    if duplicates:
        listing = "; ".join(f"tenant {row.tenant_id}: {row.normalized} x{row.n}" for row in duplicates)
        raise RuntimeError(
            f"users: {len(duplicates)} duplicated (tenant, email) found, migration refused -- "
            f"decide which account stays, then re-run: {listing}"
        )

    bind.execute(sa.text("UPDATE users SET email = LOWER(TRIM(email))"))
    with op.batch_alter_table("users") as batch:
        batch.create_unique_constraint("uq_users_tenant_email", ["tenant_id", "email"])


def downgrade() -> None:
    with op.batch_alter_table("users") as batch:
        batch.drop_constraint("uq_users_tenant_email", type_="unique")
