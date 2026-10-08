"""Sprint 8: notification preferences + user action logs

Revision ID: 3a8f5c201e47
Revises: 0fdc10883121
Create Date: 2026-06-23 12:00:00.000000

"""
from typing import Sequence, Union

from alembic import op
import sqlalchemy as sa


# revision identifiers, used by Alembic.
revision: str = '3a8f5c201e47'
down_revision: Union[str, None] = '0fdc10883121'
branch_labels: Union[str, Sequence[str], None] = None
depends_on: Union[str, Sequence[str], None] = None


def upgrade() -> None:
    op.create_table(
        'notification_preferences',
        sa.Column('id', sa.String(36), primary_key=True),
        sa.Column('user_id', sa.String(36), sa.ForeignKey('users.id', ondelete='RESTRICT'), nullable=False, index=True),
        sa.Column('tenant_id', sa.String(36), sa.ForeignKey('tenants.id', ondelete='RESTRICT'), nullable=False, index=True),
        sa.Column('email_critical', sa.Boolean(), nullable=False, server_default='1'),
        sa.Column('email_high', sa.Boolean(), nullable=False, server_default='1'),
        sa.Column('email_medium', sa.Boolean(), nullable=False, server_default='0'),
        sa.Column('email_low', sa.Boolean(), nullable=False, server_default='0'),
        sa.Column('created_at', sa.DateTime(timezone=True), nullable=False),
        sa.Column('updated_at', sa.DateTime(timezone=True), nullable=False),
    )

    op.create_table(
        'user_action_logs',
        sa.Column('id', sa.String(36), primary_key=True),
        sa.Column('tenant_id', sa.String(36), sa.ForeignKey('tenants.id', ondelete='RESTRICT'), nullable=False, index=True),
        sa.Column('actor_user_id', sa.String(36), sa.ForeignKey('users.id', ondelete='RESTRICT'), nullable=False, index=True),
        sa.Column('action_type', sa.String(50), nullable=False, index=True),
        sa.Column('target_type', sa.String(50), nullable=True),
        sa.Column('target_id', sa.String(36), nullable=True),
        sa.Column('details_json', sa.JSON(), nullable=True),
        sa.Column('ip_address', sa.String(45), nullable=True),
        sa.Column('created_at', sa.DateTime(timezone=True), nullable=False),
    )


def downgrade() -> None:
    op.drop_table('user_action_logs')
    op.drop_table('notification_preferences')
