"""Posture: the workstation's own clock at each heartbeat

Revision ID: a7c2f1d94b38
Revises: f1a9c3d5b720
Create Date: 2026-10-28 08:10:00.000000

The bench VMs have no Guest Additions, so nothing keeps their clocks with anything: they
free-run and are corrected by hand. PC-LAB was six hours and nine minutes behind on 2026-10-28,
and the only symptoms were alerts that looked hours old on the console and -- until the same day
-- attribution that could not name anyone, because the ETW session's records and the wall clock
had ended up in different frames.

Reading that drift from an alert or an audit entry only works while something is being recorded;
a quiet morning reports the skew of whatever was recorded last, which may be from before the
clock was corrected. The heartbeat is the one thing a workstation sends whether or not anything
has happened on it, so the clock rides with it and is kept here, beside the server's received_at.

Nullable: an agent from before this does not send it.
"""

from collections.abc import Sequence

import sqlalchemy as sa
from alembic import op

revision: str = "a7c2f1d94b38"
down_revision: str | None = "f1a9c3d5b720"
branch_labels: str | Sequence[str] | None = None
depends_on: str | Sequence[str] | None = None


def upgrade() -> None:
    with op.batch_alter_table("agent_posture") as batch:
        batch.add_column(sa.Column("agent_clock_utc", sa.DateTime(timezone=True), nullable=True))


def downgrade() -> None:
    with op.batch_alter_table("agent_posture") as batch:
        batch.drop_column("agent_clock_utc")
