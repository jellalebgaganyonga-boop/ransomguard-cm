"""SQLAlchemy declarative base with naming conventions and common column types."""

from datetime import datetime, timezone
from typing import Annotated
from uuid import uuid4

from sqlalchemy import DateTime, MetaData, String
from sqlalchemy.orm import DeclarativeBase, mapped_column

metadata = MetaData(
    naming_convention={
        "ix": "ix_%(column_0_label)s",
        "uq": "uq_%(table_name)s_%(column_0_name)s",
        "ck": "ck_%(table_name)s_%(constraint_name)s",
        "fk": "fk_%(table_name)s_%(column_0_name)s_%(referred_table_name)s",
        "pk": "pk_%(table_name)s",
    }
)


class Base(DeclarativeBase):
    """Base class for all GRID SQLAlchemy models."""

    metadata = metadata


# Reusable annotated column types
uuid_pk = Annotated[
    str,
    mapped_column(String(36), primary_key=True, default=lambda: str(uuid4())),
]
tenant_col = Annotated[
    str,
    mapped_column(String(36), index=True, nullable=False),
]
created_at_col = Annotated[
    datetime,
    mapped_column(
        DateTime(timezone=True),
        default=lambda: datetime.now(timezone.utc),
        nullable=False,
    ),
]
