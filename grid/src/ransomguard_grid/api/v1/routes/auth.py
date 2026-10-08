"""Authentication endpoints: login, refresh, logout.

GRID-SEC-001 FIX (Sprint 8): Refresh token is now set as an HttpOnly
Secure SameSite=Strict cookie instead of being returned in the JSON body.
The access_token is still returned in JSON (consumed by in-memory Zustand store).
"""

import asyncio
from datetime import UTC, datetime
from uuid import uuid4

from fastapi import APIRouter, Depends, HTTPException, Request, Response
from sqlalchemy.ext.asyncio import AsyncSession

from ransomguard_grid.api.v1.dependencies.jwt_auth import get_current_user, get_jwt_service, oauth2_scheme
from ransomguard_grid.api.v1.schemas.auth import LoginRequest, LoginResponse, RefreshRequest
from ransomguard_grid.core.config import get_settings
from ransomguard_grid.core.jwt_service import JwtService, blacklist_token
from ransomguard_grid.core.security import verify_password
from ransomguard_grid.db.models.notifications import UserActionLog
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

    access_token = jwt_svc.create_access_token(
        user_id=user.id, tenant_id=tenant.id, roles=role_names,
        expires_minutes=_settings.jwt_access_token_expire_minutes,
    )
    refresh_token = jwt_svc.create_refresh_token(user_id=user.id, tenant_id=tenant.id)

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

    # Set refresh token as HttpOnly cookie (GRID-SEC-001 fix)
    _set_refresh_cookie(response, refresh_token)

    return LoginResponse(
        access_token=access_token, refresh_token="",  # Not in body anymore
        expires_in=_settings.jwt_access_token_expire_minutes * 60,
        user_id=user.id, tenant_id=tenant.id, roles=role_names,
    )


@router.post("/refresh")
async def refresh(
    request: Request,
    response: Response,
    body: RefreshRequest = RefreshRequest(),
    db: AsyncSession = Depends(get_db),
    jwt_svc: JwtService = Depends(get_jwt_service),
) -> LoginResponse:
    """Use refresh token to get new access token.

    Reads refresh token from HttpOnly cookie (preferred) or from body (legacy).
    """
    from ransomguard_grid.core.exceptions import AuthenticationError

    # Try cookie first, fall back to body
    raw_token = request.cookies.get(_REFRESH_COOKIE_NAME) or body.refresh_token
    if not raw_token:
        raise HTTPException(401, "No refresh token provided")

    try:
        payload = jwt_svc.decode(raw_token)
    except AuthenticationError:
        _clear_refresh_cookie(response)
        raise HTTPException(401, "Invalid refresh token") from None

    if payload.token_type != "refresh":
        raise HTTPException(401, "Not a refresh token")

    user_repo = UserRepository(db, tenant_id=payload.tenant_id)
    user = await user_repo.get_by_id(payload.sub)
    if not user or not user.is_active:
        _clear_refresh_cookie(response)
        raise HTTPException(401, "User inactive")

    role_repo = UserRoleRepository(db)
    role_names = await role_repo.get_user_role_names(user.id)

    access_token = jwt_svc.create_access_token(
        user_id=user.id, tenant_id=payload.tenant_id, roles=role_names,
        expires_minutes=_settings.jwt_access_token_expire_minutes,
    )
    new_refresh = jwt_svc.create_refresh_token(user_id=user.id, tenant_id=payload.tenant_id)

    # Rotate refresh token cookie
    _set_refresh_cookie(response, new_refresh)

    return LoginResponse(
        access_token=access_token, refresh_token="",
        expires_in=_settings.jwt_access_token_expire_minutes * 60,
        user_id=user.id, tenant_id=payload.tenant_id, roles=role_names,
    )


@router.post("/logout")
async def logout(
    response: Response,
    user: User = Depends(get_current_user),
    token: str = Depends(oauth2_scheme),
    jwt_svc: JwtService = Depends(get_jwt_service),
    db: AsyncSession = Depends(get_db),
) -> dict[str, str]:
    """Revoke current access token and clear refresh cookie."""
    payload = jwt_svc.decode(token)
    blacklist_token(payload.jti)

    # Clear the HttpOnly refresh cookie
    _clear_refresh_cookie(response)

    db.add(UserActionLog(
        id=str(uuid4()),
        tenant_id=user.tenant_id,
        actor_user_id=user.id,
        action_type="logout",
        created_at=datetime.now(UTC),
    ))

    return {"detail": "Logged out successfully"}
