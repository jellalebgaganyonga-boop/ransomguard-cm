"""Notification delivery audit trail (grid-notifier service)

Revision ID: 7c41b9d2e508
Revises: 3a8f5c201e47
Create Date: 2026-09-28 15:10:00.000000

Records the outcome of every notification the grid-notifier service handles:
sent, failed, suppressed by the throttle window, or dead-lettered after the
retries ran out. Law 2024/017 makes the facility accountable for reacting to an
incident, so "the security officer was told" has to be provable -- and a run of
'failed' rows is how a relay outage becomes visible instead of silent.
"""
from typing import Sequence, Union

from alembic import op
import sqlalchemy as sa


# revision identifiers, used by Alembic.
revision: str = '7c41b9d2e508'
down_revision: Union[str, None] = '3a8f5c201e47'
branch_labels: Union[str, Sequence[str], None] = None
depends_on: Union[str, Sequence[str], None] = None


def upgrade() -> None:
    op.create_table(
        'notification_deliveries',
        sa.Column('id', sa.String(36), primary_key=True),
        sa.Column('tenant_id', sa.String(36), sa.ForeignKey('tenants.id', ondelete='RESTRICT'), nullable=False),
        sa.Column('user_id', sa.String(36), sa.ForeignKey('users.id', ondelete='RESTRICT'), nullable=False),
        sa.Column('alert_id', sa.String(36), nullable=False),
        sa.Column('channel', sa.String(20), nullable=False, server_default='email'),
        sa.Column('to_address', sa.String(255), nullable=False),
        sa.Column('status', sa.String(20), nullable=False),
        sa.Column('attempt', sa.Integer(), nullable=False, server_default='1'),
        sa.Column('error_message', sa.String(500), nullable=True),
        sa.Column('queued_at', sa.DateTime(timezone=True), nullable=False, server_default=sa.func.now()),
        sa.Column('completed_at', sa.DateTime(timezone=True), nullable=True),
    )
    op.create_index('ix_notification_deliveries_tenant_id', 'notification_deliveries', ['tenant_id'])
    op.create_index('ix_notification_deliveries_user_id', 'notification_deliveries', ['user_id'])
    op.create_index('ix_notification_deliveries_alert_id', 'notification_deliveries', ['alert_id'])
    op.create_index('ix_notification_deliveries_status', 'notification_deliveries', ['status'])


def downgrade() -> None:
    op.drop_index('ix_notification_deliveries_status', table_name='notification_deliveries')
    op.drop_index('ix_notification_deliveries_alert_id', table_name='notification_deliveries')
    op.drop_index('ix_notification_deliveries_user_id', table_name='notification_deliveries')
    op.drop_index('ix_notification_deliveries_tenant_id', table_name='notification_deliveries')
    op.drop_table('notification_deliveries')
