"""FastAPI application factory for GRID central server."""

from collections.abc import AsyncGenerator
from contextlib import asynccontextmanager
from uuid import uuid4

from fastapi import FastAPI, Request
from fastapi.middleware.cors import CORSMiddleware
from fastapi.responses import JSONResponse

from ransomguard_grid.core.config import get_settings
from ransomguard_grid.core.exceptions import GridBaseError
from ransomguard_grid.core.logging import configure_logging, get_logger
from ransomguard_grid.db.session import engine

logger = get_logger("main")


@asynccontextmanager
async def lifespan(app: FastAPI) -> AsyncGenerator[None, None]:
    """Startup and shutdown lifecycle with worker wiring."""
    import httpx

    from ransomguard_grid.db.session import AsyncSessionLocal
    from ransomguard_grid.db.repositories.threat_intel_repository import ThreatIntelVersionRepository
    from ransomguard_grid.services.grid_signing_key_service import GridSigningKeyService
    from ransomguard_grid.services.threat_intel_aggregator import ThreatIntelAggregator
    from ransomguard_grid.services.threat_intel_package_builder import ThreatIntelPackageBuilder
    from ransomguard_grid.services.threat_intel_sources.tor_project import TorProjectSource
    from ransomguard_grid.services.threat_intel_sources.threatfox import ThreatFoxSource
    from ransomguard_grid.services.threat_intel_sources.aws import AwsIpRangesSource
    from ransomguard_grid.services.threat_intel_sources.gcp import GcpIpRangesSource
    from ransomguard_grid.services.threat_intel_sources.alienvault_otx import AlienVaultOtxSource
    from ransomguard_grid.services.threat_intel_sources import ThreatIntelSource
    from ransomguard_grid.workers.threat_intel_updater_worker import ThreatIntelUpdaterWorker

    settings = get_settings()
    configure_logging(
        log_level=settings.log_level,
        json_output=settings.environment != "development",
    )
    logger.info("GRID server starting", environment=settings.environment)

    # Initialize GRID Ed25519 signing service
    signing_service = GridSigningKeyService(settings.threat_intel_signing_key_path)
    signing_service.initialize()
    app.state.grid_signing_service = signing_service

    # Initialize threat intel worker
    http_client = httpx.AsyncClient(timeout=30.0)
    sources: list[ThreatIntelSource] = [
        TorProjectSource(http_client),
        ThreatFoxSource(http_client),
        AwsIpRangesSource(http_client),
        GcpIpRangesSource(http_client),
    ]
    if settings.alienvault_otx_api_key:
        sources.append(AlienVaultOtxSource(http_client, settings.alienvault_otx_api_key))

    aggregator = ThreatIntelAggregator(sources)
    builder = ThreatIntelPackageBuilder(signing_service, settings.threat_intel_packages_dir)
    worker = ThreatIntelUpdaterWorker(
        aggregator=aggregator, builder=builder,
        session_factory=AsyncSessionLocal,
        interval_hours=settings.threat_intel_update_interval_hours,
    )

    app.state.threat_intel_worker = worker
    app.state.threat_intel_http_client = http_client

    # Skip auto-start in test/debug (worker runs real HTTP calls)
    if settings.environment != "development":
        await worker.start()

    yield

    # Shutdown
    if worker._task is not None:
        await worker.stop()
    await http_client.aclose()
    await engine.dispose()
    logger.info("GRID server stopped")


def create_app() -> FastAPI:
    """Create and configure the FastAPI application."""
    settings = get_settings()

    app = FastAPI(
        title="RansomGuard-CM GRID Server",
        version="0.8.0",
        description="Central management server for RansomGuard-CM agents",
        docs_url="/docs" if settings.debug else None,
        redoc_url="/redoc" if settings.debug else None,
        lifespan=lifespan,
    )

    # CORS
    app.add_middleware(
        CORSMiddleware,
        allow_origins=["http://localhost:3000"] if settings.debug else [],
        allow_credentials=True,
        allow_methods=["*"],
        allow_headers=["*"],
    )

    # Request ID middleware
    @app.middleware("http")
    async def add_request_id(request: Request, call_next):  # type: ignore[no-untyped-def]
        request_id = request.headers.get("X-Request-ID", str(uuid4()))
        request.state.request_id = request_id
        response = await call_next(request)
        response.headers["X-Request-ID"] = request_id
        return response

    # Exception handlers
    @app.exception_handler(GridBaseError)
    async def grid_exception_handler(request: Request, exc: GridBaseError) -> JSONResponse:
        return JSONResponse(
            status_code=exc.status_code,
            content={"detail": exc.message},
        )

    # Health endpoints
    @app.get("/api/v1/health", tags=["health"])
    async def health() -> dict[str, str]:
        return {"status": "ok", "version": "0.8.0"}

    @app.get("/api/v1/health/ready", tags=["health"])
    async def health_ready() -> dict[str, str | bool]:
        try:
            async with engine.connect() as conn:
                await conn.execute(
                    __import__("sqlalchemy").text("SELECT 1")
                )
            db_ok = True
        except Exception:
            db_ok = False
        return {"status": "ready" if db_ok else "degraded", "database": db_ok}

    # Agent API routes (mTLS authenticated except enrollment)
    from ransomguard_grid.api.v1.routes.enrollment import router as enrollment_router
    from ransomguard_grid.api.v1.routes.alerts import router as alerts_router
    from ransomguard_grid.api.v1.routes.audit_log import router as audit_log_router
    from ransomguard_grid.api.v1.routes.heartbeat import router as heartbeat_router

    app.include_router(enrollment_router, prefix="/api/v1")
    app.include_router(alerts_router, prefix="/api/v1")
    app.include_router(audit_log_router, prefix="/api/v1")
    app.include_router(heartbeat_router, prefix="/api/v1")

    # Threat intel endpoints (mTLS authenticated)
    from ransomguard_grid.api.v1.routes.threat_intel import router as threat_intel_router

    app.include_router(threat_intel_router, prefix="/api/v1")

    # Dashboard API routes (JWT authenticated)
    from ransomguard_grid.api.v1.routes.auth import router as auth_router
    from ransomguard_grid.api.v1.routes.dashboard import router as dashboard_router

    app.include_router(auth_router, prefix="/api/v1")
    app.include_router(dashboard_router, prefix="/api/v1")

    return app


app = create_app()
