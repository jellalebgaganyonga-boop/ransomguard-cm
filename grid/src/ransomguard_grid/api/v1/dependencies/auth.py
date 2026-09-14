"""Agent authentication dependency — mTLS (production) or agent_id path (dev)."""

from datetime import UTC, datetime
from urllib.parse import unquote

from fastapi import Depends, HTTPException, Request, status
from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from ransomguard_grid.core.config import get_settings
from ransomguard_grid.core.logging import get_logger
from ransomguard_grid.db.models.agent import Agent
from ransomguard_grid.db.models.enums import AgentStatus
from ransomguard_grid.db.repositories.agent_certificate_repository import AgentCertificateRepository
from ransomguard_grid.db.session import get_db

logger = get_logger("auth")


async def get_authenticated_agent(
    request: Request,
    db: AsyncSession = Depends(get_db),
) -> Agent:
    """Authenticate an agent via mTLS client certificate or agent_id path parameter (dev mode).

    Production: nginx terminates TLS, verifies client cert, passes PEM in X-Client-Cert header.
    Development: if no client cert header, fall back to agent_id from URL path.
    Testing: inject X-Client-Cert header directly.
    """
    client_cert_pem = request.headers.get("X-Client-Cert")

    if client_cert_pem:
        return await _authenticate_via_mtls(request, db, client_cert_pem)

    # Dev fallback: authenticate by agent_id from path
    settings = get_settings()
    if settings.environment != "development":
        raise HTTPException(
            status_code=status.HTTP_401_UNAUTHORIZED,
            detail="Client certificate required",
            headers={"WWW-Authenticate": "Mutual-TLS"},
        )

    return await _authenticate_via_agent_id(request, db)


async def _authenticate_via_mtls(
    request: Request, db: AsyncSession, client_cert_pem: str
) -> Agent:
    """Production path: verify X-Client-Cert against stored agent certificates."""
    try:
        from cryptography import x509
        from cryptography.hazmat.backends import default_backend

        cert_pem = unquote(client_cert_pem)
        cert = x509.load_pem_x509_certificate(cert_pem.encode(), default_backend())
        serial_number = format(cert.serial_number, "x").upper()
    except Exception:
        raise HTTPException(status.HTTP_401_UNAUTHORIZED, "Invalid client certificate format") from None

    cert_repo = AgentCertificateRepository(db)
    agent_cert = await cert_repo.find_by_serial_number(serial_number)
    if not agent_cert:
        raise HTTPException(status.HTTP_401_UNAUTHORIZED, "Unknown client certificate")

    if agent_cert.revoked_at is not None:
        raise HTTPException(status.HTTP_401_UNAUTHORIZED, "Certificate revoked")

    now = datetime.now(UTC)
    not_after = agent_cert.not_after.replace(tzinfo=UTC) if agent_cert.not_after.tzinfo is None else agent_cert.not_after
    not_before = agent_cert.not_before.replace(tzinfo=UTC) if agent_cert.not_before.tzinfo is None else agent_cert.not_before
    if not_after < now:
        raise HTTPException(status.HTTP_401_UNAUTHORIZED, "Certificate expired")
    if not_before > now:
        raise HTTPException(status.HTTP_401_UNAUTHORIZED, "Certificate not yet valid")

    agent = agent_cert.agent
    if not agent or agent.status in (AgentStatus.decommissioned,):
        raise HTTPException(status.HTTP_403_FORBIDDEN, "Agent inactive")

    request.state.tenant_id = agent.tenant_id
    request.state.agent_id = agent.id
    return agent


async def _authenticate_via_agent_id(request: Request, db: AsyncSession) -> Agent:
    """Dev fallback: look up agent by the agent_id path parameter."""
    agent_id = request.path_params.get("agent_id")
    if not agent_id:
        raise HTTPException(status.HTTP_401_UNAUTHORIZED, "Missing agent_id in path")

    result = await db.execute(select(Agent).where(Agent.id == agent_id))
    agent = result.scalar_one_or_none()
    if not agent:
        raise HTTPException(status.HTTP_401_UNAUTHORIZED, "Unknown agent_id")
    if agent.status in (AgentStatus.decommissioned,):
        raise HTTPException(status.HTTP_403_FORBIDDEN, "Agent inactive")

    logger.warning("Dev mode: agent authenticated by ID without mTLS", agent_id=agent_id)
    request.state.tenant_id = agent.tenant_id
    request.state.agent_id = agent.id
    return agent


def get_tenant_id(request: Request) -> str:
    """Get tenant_id from request state (set by get_authenticated_agent)."""
    tenant_id: str | None = getattr(request.state, "tenant_id", None)
    if not tenant_id:
        raise HTTPException(500, "tenant_id not set")
    return tenant_id
