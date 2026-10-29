"""Users: preferred language and timezone (Settings, Fix C)

Revision ID: e5f8b2c4a713
Revises: d4e7a1c93f20
Create Date: 2026-10-21 20:00:00.000000

Until now /dashboard/me returned a fixed language ("fr") read from an attribute the users table
never had; the console could not keep a choice beyond the browser. Null = never chosen: the
console then follows its default (English since 2026-10-21) and the browser's language.
"""

from collections.abc import Sequence

import sqlalchemy as sa
from alembic import op

revision: str = "e5f8b2c4a713"
down_revision: str | None = "d4e7a1c93f20"
branch_labels: str | Sequence[str] | None = None
depends_on: str | Sequence[str] | None = None


def upgrade() -> None:
    with op.batch_alter_table("users") as batch:
        batch.add_column(sa.Column("preferred_language", sa.String(5), nullable=True))
        batch.add_column(sa.Column("preferred_timezone", sa.String(64), nullable=True))


def downgrade() -> None:
    with op.batch_alter_table("users") as batch:
        batch.drop_column("preferred_timezone")
        batch.drop_column("preferred_language")
