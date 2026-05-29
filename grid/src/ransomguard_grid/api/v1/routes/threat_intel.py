"""Threat intel public endpoints — mTLS required."""

import base64
from pathlib import Path
from uuid import uuid4

from fastapi import APIRouter, Depends, HTTPException
from fastapi.responses import StreamingResponse
from sqlalchemy.ext.asyncio import AsyncSession

from ransomguard_grid.api.v1.dependencies.auth import get_authenticated_agent
from ransomguard_grid.api.v1.schemas.threat_intel import AppliedVersionRequest, AppliedVersionResponse, ManifestResponse
from ransomguard_grid.db.models.agent import Agent
from ransomguard_grid.db.models.threat_intel import AgentThreatIntelVersion
from ransomguard_grid.db.repositories.threat_intel_repository import (
    AgentThreatIntelVersionRepository,
    ThreatIntelPackageRepository,
    ThreatIntelVersionRepository,
)
from ransomguard_grid.db.session import get_db

router = APIRouter(prefix="/threat-intel", tags=["threat-intel"])


@router.get("/manifest", response_model=ManifestResponse)
async def get_latest_manifest(
    agent: Agent = Depends(get_authenticated_agent),
    db: AsyncSession = Depends(get_db),
) -> ManifestResponse:
    """Returns metadata of latest published threat intel version."""
    repo = ThreatIntelVersionRepository(db)
    latest = await repo.get_latest_published()
    if not latest:
        raise HTTPException(404, "No threat intel published yet")

    return ManifestResponse(
        version=latest.version_string,
        published_at=latest.published_at,
        package_size_bytes=latest.package_size_bytes,
        package_sha256=latest.package_sha256,
        package_url=f"/api/v1/threat-intel/package/{latest.version_string}",
        ed25519_signature_base64=base64.b64encode(latest.ed25519_signature).decode(),
    )


@router.get("/package/{version}")
async def download_package(
    version: str,
    agent: Agent = Depends(get_authenticated_agent),
    db: AsyncSession = Depends(get_db),
) -> StreamingResponse:
    """Streams the ZIP package for given version."""
    version_repo = ThreatIntelVersionRepository(db)
    version_record = await version_repo.get_by_version_string(version)
    if not version_record:
        raise HTTPException(404, f"Version {version} not found")

    pkg_repo = ThreatIntelPackageRepository(db)
    package = await pkg_repo.get_by_version_id(version_record.id)
    if not package or not Path(package.file_path).exists():
        raise HTTPException(500, "Package file missing on server")

    def file_iter():  # type: ignore[no-untyped-def]
        with open(package.file_path, "rb") as f:
            while chunk := f.read(8192):
                yield chunk

    return StreamingResponse(
        file_iter(),
        media_type="application/zip",
        headers={
            "Content-Disposition": f'attachment; filename="{package.file_name}"',
            "X-Package-SHA256": package.file_sha256,
        },
    )


@router.post("/agents/{agent_id}/threat-intel-version", response_model=AppliedVersionResponse)
async def report_applied_version(
    agent_id: str,
    body: AppliedVersionRequest,
    agent: Agent = Depends(get_authenticated_agent),
    db: AsyncSession = Depends(get_db),
) -> AppliedVersionResponse:
    """Agent reports which version it has applied."""
    if agent.id != agent_id:
        raise HTTPException(403, "Agent ID mismatch")

    version_repo = ThreatIntelVersionRepository(db)
    version = await version_repo.get_by_version_string(body.applied_version)
    if not version:
        raise HTTPException(404, f"Version {body.applied_version} not found")

    tracking_repo = AgentThreatIntelVersionRepository(db, agent.tenant_id)
    await tracking_repo.add(AgentThreatIntelVersion(
        id=str(uuid4()),
        tenant_id=agent.tenant_id,
        agent_id=agent.id,
        version_id=version.id,
        applied_at=body.applied_at,
        applied_status=body.applied_status,
        error_message=body.error_message,
    ))
    return AppliedVersionResponse(success=True)
