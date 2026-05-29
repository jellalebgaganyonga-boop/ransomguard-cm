"""Authentication endpoints: login, refresh, logout."""

import asyncio
from datetime import UTC, datetime

from fastapi import APIRouter, Depends, HTTPException
from sqlalchemy.ext.asyncio import AsyncSession

from ransomguard_grid.api.v1.dependencies.jwt_auth import get_current_user, get_jwt_service, oauth2_scheme
from ransomguard_grid.api.v1.schemas.auth import LoginRequest, LoginResponse, RefreshRequest
from ransomguard_grid.core.config import get_settings
from ransomguard_grid.core.jwt_service import JwtService, blacklist_token
from ransomguard_grid.core.security import verify_password
from ransomguard_grid.db.models.tenant_user import User
from ransomguard_grid.db.repositories.role_repository import UserRoleRepository
from ransomguard_grid.db.repositories.tenant_repository import TenantRepository
from ransomguard_grid.db.repositories.user_repository import UserRepository
from ransomguard_grid.db.session import get_db

router = APIRouter(prefix="/auth", tags=["auth"])
_settings = get_settings()


@router.post("/login", response_model=LoginResponse)
async def login(
    body: LoginRequest,
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
    await db.flush()

    return LoginResponse(
        access_token=access_token, refresh_token=refresh_token,
        expires_in=_settings.jwt_access_token_expire_minutes * 60,
        user_id=user.id, tenant_id=tenant.id, roles=role_names,
    )


@router.post("/refresh", response_model=LoginResponse)
async def refresh(
    body: RefreshRequest,
    db: AsyncSession = Depends(get_db),
    jwt_svc: JwtService = Depends(get_jwt_service),
) -> LoginResponse:
    """Use refresh token to get new access token."""
    from ransomguard_grid.core.exceptions import AuthenticationError

    try:
        payload = jwt_svc.decode(body.refresh_token)
    except AuthenticationError:
        raise HTTPException(401, "Invalid refresh token") from None

    if payload.token_type != "refresh":
        raise HTTPException(401, "Not a refresh token")

    user_repo = UserRepository(db, tenant_id=payload.tenant_id)
    user = await user_repo.get_by_id(payload.sub)
    if not user or not user.is_active:
        raise HTTPException(401, "User inactive")

    role_repo = UserRoleRepository(db)
    role_names = await role_repo.get_user_role_names(user.id)

    access_token = jwt_svc.create_access_token(
        user_id=user.id, tenant_id=payload.tenant_id, roles=role_names,
        expires_minutes=_settings.jwt_access_token_expire_minutes,
    )
    new_refresh = jwt_svc.create_refresh_token(user_id=user.id, tenant_id=payload.tenant_id)

    return LoginResponse(
        access_token=access_token, refresh_token=new_refresh,
        expires_in=_settings.jwt_access_token_expire_minutes * 60,
        user_id=user.id, tenant_id=payload.tenant_id, roles=role_names,
    )


@router.post("/logout")
async def logout(
    user: User = Depends(get_current_user),
    token: str = Depends(oauth2_scheme),
    jwt_svc: JwtService = Depends(get_jwt_service),
) -> dict[str, str]:
    """Revoke current access token."""
    payload = jwt_svc.decode(token)
    blacklist_token(payload.jti)
    return {"detail": "Logged out successfully"}
