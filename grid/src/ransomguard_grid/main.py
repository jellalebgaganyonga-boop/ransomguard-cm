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
    """Startup and shutdown lifecycle."""
    settings = get_settings()
    configure_logging(
        log_level=settings.log_level,
        json_output=settings.environment != "development",
    )
    logger.info("GRID server starting", environment=settings.environment)
    yield
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

    return app


app = create_app()
