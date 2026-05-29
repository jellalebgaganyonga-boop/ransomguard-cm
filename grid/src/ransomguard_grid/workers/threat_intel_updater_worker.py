"""Background worker that periodically updates threat intel packages."""

import asyncio
import hashlib
from datetime import UTC, datetime
from uuid import uuid4

from sqlalchemy.ext.asyncio import AsyncSession, async_sessionmaker

from ransomguard_grid.core.logging import get_logger
from ransomguard_grid.db.models.enums import ThreatIntelStatus
from ransomguard_grid.db.models.threat_intel import ThreatIntelPackage, ThreatIntelVersion
from ransomguard_grid.db.repositories.threat_intel_repository import ThreatIntelVersionRepository
from ransomguard_grid.services.threat_intel_aggregator import ThreatIntelAggregator
from ransomguard_grid.services.threat_intel_package_builder import ThreatIntelPackageBuilder

logger = get_logger("threat_intel_worker")


class ThreatIntelUpdaterWorker:
    """Periodically fetches, aggregates, signs, and publishes threat intel packages."""

    def __init__(
        self,
        aggregator: ThreatIntelAggregator,
        builder: ThreatIntelPackageBuilder,
        session_factory: async_sessionmaker[AsyncSession],
        interval_hours: int = 24,
    ) -> None:
        self.aggregator = aggregator
        self.builder = builder
        self.session_factory = session_factory
        self.interval_hours = interval_hours
        self._task: asyncio.Task[None] | None = None
        self._stop_event = asyncio.Event()
        self._iteration_count = 0

    @property
    def iteration_count(self) -> int:
        """Number of completed iterations (for testing)."""
        return self._iteration_count

    async def start(self) -> None:
        """Start the background loop."""
        self._stop_event.clear()
        self._task = asyncio.create_task(self._run_loop())
        logger.info("Threat intel worker started", interval_hours=self.interval_hours)

    async def stop(self) -> None:
        """Signal stop and await task completion."""
        self._stop_event.set()
        if self._task:
            try:
                await asyncio.wait_for(self._task, timeout=10)
            except (asyncio.TimeoutError, asyncio.CancelledError):
                pass
        logger.info("Threat intel worker stopped", iterations=self._iteration_count)

    async def run_one_iteration(self) -> None:
        """Run a single fetch-aggregate-build-publish cycle."""
        logger.info("Starting threat intel update iteration")
        try:
            aggregated = await self.aggregator.fetch_all()
            if aggregated.total_entries == 0:
                logger.warning("No threat intel entries from any source, skipping package build")
                return

            version_string = datetime.now(UTC).strftime("%Y-%m-%d") + f"-{self._iteration_count + 1:03d}"
            zip_path = self.builder.build(aggregated, version_string)
            package_size = zip_path.stat().st_size
            package_sha256 = ThreatIntelPackageBuilder.compute_sha256(zip_path)

            with open(zip_path, "rb") as f:
                # Read signature from ZIP (last file)
                import zipfile
                with zipfile.ZipFile(zip_path) as zf:
                    signature = zf.read("signature.bin")

            async with self.session_factory() as session:
                version = ThreatIntelVersion(
                    id=str(uuid4()),
                    version_string=version_string,
                    published_at=datetime.now(UTC),
                    package_size_bytes=package_size,
                    package_sha256=package_sha256,
                    ed25519_signature=signature,
                    status=ThreatIntelStatus.Published,
                )
                session.add(version)

                package = ThreatIntelPackage(
                    id=str(uuid4()),
                    version_id=version.id,
                    file_name=zip_path.name,
                    file_path=str(zip_path),
                    file_size_bytes=package_size,
                    file_sha256=package_sha256,
                )
                session.add(package)
                await session.commit()

            self._iteration_count += 1
            logger.info(
                "Threat intel iteration complete",
                version=version_string,
                entries=aggregated.total_entries,
                size_bytes=package_size,
            )
        except Exception:
            logger.exception("Threat intel update iteration failed")

    async def _run_loop(self) -> None:
        """Main background loop."""
        while not self._stop_event.is_set():
            await self.run_one_iteration()
            try:
                await asyncio.wait_for(
                    self._stop_event.wait(),
                    timeout=self.interval_hours * 3600,
                )
            except asyncio.TimeoutError:
                pass  # Normal interval expiry
