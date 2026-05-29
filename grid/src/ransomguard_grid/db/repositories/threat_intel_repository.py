"""Repositories for threat intel version tracking."""

from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from ransomguard_grid.db.models.enums import ThreatIntelStatus
from ransomguard_grid.db.models.threat_intel import AgentThreatIntelVersion, ThreatIntelPackage, ThreatIntelVersion
from ransomguard_grid.db.repositories.base_repository import BaseRepository


class ThreatIntelVersionRepository:
    """System-level (global, no tenant filter)."""

    def __init__(self, session: AsyncSession) -> None:
        self.session = session

    async def get_latest_published(self) -> ThreatIntelVersion | None:
        """Get the most recently published version."""
        stmt = (
            select(ThreatIntelVersion)
            .where(ThreatIntelVersion.status == ThreatIntelStatus.Published)
            .order_by(ThreatIntelVersion.published_at.desc())
            .limit(1)
        )
        result = await self.session.execute(stmt)
        return result.scalar_one_or_none()

    async def get_by_version_string(self, version: str) -> ThreatIntelVersion | None:
        """Find a version by its string identifier."""
        stmt = select(ThreatIntelVersion).where(ThreatIntelVersion.version_string == version)
        result = await self.session.execute(stmt)
        return result.scalar_one_or_none()

    async def add(self, version: ThreatIntelVersion) -> ThreatIntelVersion:
        """Persist a new threat intel version."""
        self.session.add(version)
        await self.session.flush()
        return version


class ThreatIntelPackageRepository:
    """System-level repository for package file metadata."""

    def __init__(self, session: AsyncSession) -> None:
        self.session = session

    async def get_by_version_id(self, version_id: str) -> ThreatIntelPackage | None:
        """Get package metadata by version ID."""
        stmt = select(ThreatIntelPackage).where(ThreatIntelPackage.version_id == version_id)
        result = await self.session.execute(stmt)
        return result.scalar_one_or_none()

    async def add(self, package: ThreatIntelPackage) -> ThreatIntelPackage:
        """Persist package metadata."""
        self.session.add(package)
        await self.session.flush()
        return package


class AgentThreatIntelVersionRepository(BaseRepository[AgentThreatIntelVersion]):
    """Tenant-scoped tracking of per-agent applied versions."""

    model = AgentThreatIntelVersion
