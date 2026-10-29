"""Audit log signature stored as base64 text, not raw bytes

Revision ID: f1a9c3d5b720
Revises: e5f8b2c4a713
Create Date: 2026-10-27 23:10:00.000000

Every agent audit upload was answered 500. Storing the Ed25519 signature as LargeBinary goes
through aiomysql's bytes escaping, and aiomysql 0.3.2 against PyMySQL 1.2.3 raises
"'str' object is not callable" there: escape_bytes_prefixed is a string in that pairing. Nothing
caught it because nothing uploaded -- the agent gained its uploader the same day.

The signature arrives base64 and an auditor re-checks it base64, so that is what is kept. The
column is empty everywhere this runs: no agent ever successfully wrote a row to it.
"""

from collections.abc import Sequence

import sqlalchemy as sa
from alembic import op

revision: str = "f1a9c3d5b720"
down_revision: str | None = "e5f8b2c4a713"
branch_labels: str | Sequence[str] | None = None
depends_on: str | Sequence[str] | None = None


def upgrade() -> None:
    with op.batch_alter_table("audit_logs_grid") as batch:
        batch.alter_column(
            "ed25519_signature",
            existing_type=sa.LargeBinary(),
            type_=sa.String(128),
            existing_nullable=False,
        )


def downgrade() -> None:
    with op.batch_alter_table("audit_logs_grid") as batch:
        batch.alter_column(
            "ed25519_signature",
            existing_type=sa.String(128),
            type_=sa.LargeBinary(),
            existing_nullable=False,
        )
