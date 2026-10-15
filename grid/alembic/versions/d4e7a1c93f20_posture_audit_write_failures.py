"""Agent posture: audit_write_failures (AGT-AUD-002)

Revision ID: d4e7a1c93f20
Revises: b52d0e8c4a91
Create Date: 2026-10-15 10:00:00.000000

The agent no longer lets an audit write hold up a detection or an action: an entry it cannot
record is logged Critical and counted, and the detection goes on. The count reaches the GRID
in the heartbeat, so the failure shows in the posture (partial, audit_write_failed) instead
of staying in a local log. Null = not reported (an agent older than this column), never 0.
"""

from collections.abc import Sequence

import sqlalchemy as sa
from alembic import op

revision: str = "d4e7a1c93f20"
down_revision: str | None = "b52d0e8c4a91"
branch_labels: str | Sequence[str] | None = None
depends_on: str | Sequence[str] | None = None


def upgrade() -> None:
    with op.batch_alter_table("agent_posture") as batch:
        batch.add_column(sa.Column("audit_write_failures", sa.Integer(), nullable=True))


def downgrade() -> None:
    with op.batch_alter_table("agent_posture") as batch:
        batch.drop_column("audit_write_failures")
