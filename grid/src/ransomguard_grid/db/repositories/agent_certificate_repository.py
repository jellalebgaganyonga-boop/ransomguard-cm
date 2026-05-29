"""Agent certificate repository — system-level (no tenant filter for mTLS lookup)."""

from datetime import UTC, datetime

from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession
from sqlalchemy.orm import joinedload

from ransomguard_grid.db.models.agent import AgentCertificate


class AgentCertificateRepository:
    """System-level repository for agent certificates.

    mTLS lookup by serial_number must work BEFORE tenant context is known,
    so this does NOT inherit BaseRepository (no tenant filter).
    """

    def __init__(self, session: AsyncSession) -> None:
        self.session = session

    async def find_by_serial_number(self, serial: str) -> AgentCertificate | None:
        """Find cert by serial number with eager-loaded agent."""
        stmt = (
            select(AgentCertificate)
            .options(joinedload(AgentCertificate.agent))
            .where(AgentCertificate.serial_number == serial)
        )
        result = await self.session.execute(stmt)
        return result.scalar_one_or_none()

    async def find_by_fingerprint(self, fingerprint: str) -> AgentCertificate | None:
        """Find cert by SHA-256 fingerprint."""
        stmt = select(AgentCertificate).where(AgentCertificate.fingerprint_sha256 == fingerprint)
        result = await self.session.execute(stmt)
        return result.scalar_one_or_none()

    async def add(self, cert: AgentCertificate) -> AgentCertificate:
        """Persist a new agent certificate."""
        self.session.add(cert)
        await self.session.flush()
        return cert

    async def revoke(self, cert_id: str, reason: str) -> None:
        """Mark a certificate as revoked."""
        stmt = select(AgentCertificate).where(AgentCertificate.id == cert_id)
        result = await self.session.execute(stmt)
        cert = result.scalar_one_or_none()
        if cert:
            cert.revoked_at = datetime.now(UTC)
            cert.revocation_reason = reason
            await self.session.flush()
