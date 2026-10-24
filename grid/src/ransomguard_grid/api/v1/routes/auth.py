"""Authentication endpoints: login, refresh, logout, logout-all.

GRID-SEC-001: the refresh token is an HttpOnly Secure SameSite=Strict cookie, never in a body.
GRID-SEC-004 / GRID-SEC-005: revocation lives in Redis (core/session_store.py), shared by every
worker: logout revokes the session everywhere, each refresh token is used once, a replayed one
revokes its whole family, and "log out every session" invalidates every token of the user.
"""

import asyncio
from datetime import UTC, datetime
from uuid import uuid4

from fastapi import APIRouter, Depends, HTTPException, Request, Response
from sqlalchemy.ext.asyncio import AsyncSession

from ransomguard_grid.api.v1.dependencies.jwt_auth import get_current_user, get_jwt_service, oauth2_scheme
from ransomguard_grid.api.v1.schemas.auth import LoginRequest, LoginResponse
from ransomguard_grid.core.config import get_settings
from ransomguard_grid.core.jwt_service import JwtPayload, JwtService
from ransomguard_grid.core.security import verify_password
from ransomguard_grid.core.session_store import SessionStore, SessionStoreUnavailableError, get_session_store
from ransomguard_grid.db.models.enums import LogLevel
from ransomguard_grid.db.models.notifications import UserActionLog
from ransomguard_grid.db.models.operations import SystemLog
from ransomguard_grid.db.models.tenant_user import User
from ransomguard_grid.db.repositories.role_repository import UserRoleRepository
from ransomguard_grid.db.repositories.tenant_repository import TenantRepository
from ransomguard_grid.db.repositories.user_repository import UserRepository
from ransomguard_grid.db.session import get_db

router = APIRouter(prefix="/auth", tags=["auth"])
_settings = get_settings()

_REFRESH_COOKIE_NAME = "rg_refresh_token"
_REFRESH_COOKIE_MAX_AGE = 7 * 24 * 3600  # 7 days


def _set_refresh_cookie(response: Response, refresh_token: str) -> None:
    """Set the refresh token as an HttpOnly Secure cookie."""
    response.set_cookie(
        key=_REFRESH_COOKIE_NAME,
        value=refresh_token,
        httponly=True,
        secure=True,
        samesite="strict",
        max_age=_REFRESH_COOKIE_MAX_AGE,
        path="/api/v1/auth",  # Only sent to auth endpoints
    )


def _clear_refresh_cookie(response: Response) -> None:
    """Delete the refresh token cookie."""
    response.delete_cookie(
        key=_REFRESH_COOKIE_NAME,
        httponly=True,
        secure=True,
        samesite="strict",
        path="/api/v1/auth",
    )


@router.post("/login")
async def login(
    body: LoginRequest,
    response: Response,
    db: AsyncSession = Depends(get_db),
    jwt_svc: JwtService = Depends(get_jwt_service),
    store: SessionStore = Depends(get_session_store),
) -> LoginResponse:
    """Authenticate user and return JWT tokens."""
    tenant_repo = TenantRepository(db)
    tenant = await tenant_repo.get_by_code(body.tenant_code)
    if not tenant:
        await asyncio.sleep(0.1)  # constant time to prevent enumeration
        raise HTTPException(401, "Invalid credentials")

    user_repo = UserRepository(db, tenant_id=tenant.id)
    user = await user_repo.find_by_email(body.email)
    if not user or not user.is_active:
        await asyncio.sleep(0.1)
        raise HTTPException(401, "Invalid credentials")

    if not verify_password(body.password, user.hashed_password):
        raise HTTPException(401, "Invalid credentials")

    role_repo = UserRoleRepository(db)
    role_names = await role_repo.get_user_role_names(user.id)

    # One login opens one session family; its version is the user's current session version.
    try:
        version = await store.user_version(user.id)
        access_token = await _issue_tokens(
            jwt_svc, store, response, user.id, tenant.id, role_names, family=str(uuid4()), version=version,
        )
    except SessionStoreUnavailableError:
        raise HTTPException(503, "Session store unavailable") from None

    user.last_login_at = datetime.now(UTC)

    # Record login action
    db.add(UserActionLog(
        id=str(uuid4()),
        tenant_id=tenant.id,
        actor_user_id=user.id,
        action_type="login",
        created_at=datetime.now(UTC),
    ))
    await db.flush()

    return LoginResponse(
        access_token=access_token, refresh_token="",  # Not in body anymore
        expires_in=_settings.jwt_access_token_expire_minutes * 60,
        user_id=user.id, tenant_id=tenant.id, roles=role_names,
    )


async def _issue_tokens(
    jwt_svc: JwtService,
    store: SessionStore,
    response: Response,
    user_id: str,
    tenant_id: str,
    roles: list[str],
    *,
    family: str,
    version: int,
) -> str:
    """Issue an access token and a refresh token of the family; the refresh token is registered
    (unused) in the session store and set as the HttpOnly cookie. Returns the access token."""
    access_token = jwt_svc.create_access_token(
        user_id=user_id, tenant_id=tenant_id, roles=roles, family=family, version=version,
        expires_minutes=_settings.jwt_access_token_expire_minutes,
    )
    refresh_token = jwt_svc.create_refresh_token(user_id=user_id, tenant_id=tenant_id, family=family, version=version)
    refresh_payload = jwt_svc.decode(refresh_token)
    await store.register_refresh(refresh_payload.jti, family, _REFRESH_COOKIE_MAX_AGE)
    _set_refresh_cookie(response, refresh_token)
    return access_token


@router.post("/refresh")
async def refresh(
    request: Request,
    response: Response,
    db: AsyncSession = Depends(get_db),
    jwt_svc: JwtService = Depends(get_jwt_service),
    store: SessionStore = Depends(get_session_store),
) -> LoginResponse:
    """Exchange the refresh token (HttpOnly cookie only) for a new access token and a new refresh
    token of the same family.

    * The body path is closed: the refresh token never transits through JavaScript.
    * ``X-Requested-With: XMLHttpRequest`` is required: a plain cross-site form or navigation
      cannot set it (defence in depth with the SameSite=Strict cookie).
    * Each refresh token is consumed once (GETDEL). One that comes back after being used is the
      sign of a theft: its whole family is revoked -- the thief's session and the victim's -- and
      the event is logged.
    """
    from ransomguard_grid.core.exceptions import AuthenticationError

    if request.headers.get("X-Requested-With") != "XMLHttpRequest":
        raise HTTPException(403, "Missing X-Requested-With header")

    raw_token = request.cookies.get(_REFRESH_COOKIE_NAME)
    if not raw_token:
        raise HTTPException(401, "No refresh token provided")

    try:
        payload = jwt_svc.decode(raw_token)
    except AuthenticationError:
        _clear_refresh_cookie(response)
        raise HTTPException(401, "Invalid refresh token") from None

    if payload.token_type != "refresh":
        raise HTTPException(401, "Not a refresh token")

    try:
        if await store.is_family_revoked(payload.fam) or payload.ver < await store.user_version(payload.sub):
            _clear_refresh_cookie(response)
            raise HTTPException(401, "Session revoked")

        family = await store.consume_refresh(payload.jti)
        if family is None:
            # Already used (or never issued): replay. Revoke the whole family.
            await store.revoke_family(payload.fam)
            await _log_refresh_reuse(db, payload)
            _clear_refresh_cookie(response)
            raise HTTPException(401, "Refresh token reuse detected: session revoked")

        user_repo = UserRepository(db, tenant_id=payload.tenant_id)
        user = await user_repo.get_by_id(payload.sub)
        if not user or not user.is_active:
            await store.revoke_family(family)
            _clear_refresh_cookie(response)
            raise HTTPException(401, "User inactive")

        role_repo = UserRoleRepository(db)
        role_names = await role_repo.get_user_role_names(user.id)
        access_token = await _issue_tokens(
            jwt_svc, store, response, user.id, payload.tenant_id, role_names, family=family, version=payload.ver,
        )
    except SessionStoreUnavailableError:
        raise HTTPException(503, "Session store unavailable") from None

    return LoginResponse(
        access_token=access_token, refresh_token="",
        expires_in=_settings.jwt_access_token_expire_minutes * 60,
        user_id=user.id, tenant_id=payload.tenant_id, roles=role_names,
    )


async def _log_refresh_reuse(db: AsyncSession, payload: JwtPayload) -> None:
    """A replayed refresh token is a security event: one line in the user action log and one
    warning in the system log, with the family it revoked."""
    now = datetime.now(UTC)
    details = {"family": payload.fam, "jti": payload.jti}
    db.add(UserActionLog(
        id=str(uuid4()), tenant_id=payload.tenant_id, actor_user_id=payload.sub,
        action_type="refresh_token_reuse", target_type="session", target_id=payload.fam,
        details_json=details, created_at=now,
    ))
    db.add(SystemLog(
        id=str(uuid4()), tenant_id=payload.tenant_id, level=LogLevel.Warning, component="auth",
        message="Refresh token replayed: its session family is revoked (possible token theft)",
        context_json={**details, "user_id": payload.sub}, created_at=now,
    ))
    await db.commit()  # the 401 that follows must not roll the evidence back


def _remaining_seconds(payload: JwtPayload) -> int:
    return payload.exp - int(datetime.now(UTC).timestamp())


@router.post("/logout")
async def logout(
    response: Response,
    user: User = Depends(get_current_user),
    token: str = Depends(oauth2_scheme),
    jwt_svc: JwtService = Depends(get_jwt_service),
    store: SessionStore = Depends(get_session_store),
    db: AsyncSession = Depends(get_db),
) -> dict[str, str]:
    """Revoke this session on every worker: the access token, and its family (so every refresh
    token of this login is dead too, including a copy of the cookie)."""
    payload = jwt_svc.decode(token)
    try:
        await store.revoke_access(payload.jti, _remaining_seconds(payload))
        await store.revoke_family(payload.fam)
    except SessionStoreUnavailableError:
        raise HTTPException(503, "Session store unavailable") from None

    _clear_refresh_cookie(response)

    db.add(UserActionLog(
        id=str(uuid4()),
        tenant_id=user.tenant_id,
        actor_user_id=user.id,
        action_type="logout",
        created_at=datetime.now(UTC),
    ))

    return {"detail": "Logged out successfully"}


@router.post("/logout-all")
async def logout_all(
    response: Response,
    user: User = Depends(get_current_user),
    store: SessionStore = Depends(get_session_store),
    db: AsyncSession = Depends(get_db),
) -> dict[str, str]:
    """Log out every session of this user, on every device (US1.2.2): the user's session version
    is incremented, and every token issued before carries an older one -- refused everywhere."""
    try:
        version = await store.bump_user_version(user.id)
    except SessionStoreUnavailableError:
        raise HTTPException(503, "Session store unavailable") from None

    _clear_refresh_cookie(response)

    db.add(UserActionLog(
        id=str(uuid4()),
        tenant_id=user.tenant_id,
        actor_user_id=user.id,
        action_type="logout_all",
        details_json={"session_version": version},
        created_at=datetime.now(UTC),
    ))

    return {"detail": "Logged out of every session"}
