"""JWT authentication dependency for dashboard endpoints."""

from fastapi import Depends, HTTPException, Request, status
from fastapi.security import OAuth2PasswordBearer
from sqlalchemy.ext.asyncio import AsyncSession

from ransomguard_grid.core.config import get_settings
from ransomguard_grid.core.jwt_service import JwtService, is_token_blacklisted
from ransomguard_grid.db.models.tenant_user import User
from ransomguard_grid.db.repositories.role_repository import UserRoleRepository
from ransomguard_grid.db.repositories.user_repository import UserRepository
from ransomguard_grid.db.session import get_db

oauth2_scheme = OAuth2PasswordBearer(tokenUrl="/api/v1/auth/login")

_settings = get_settings()
_jwt_service = JwtService(_settings.jwt_secret_key, _settings.jwt_algorithm)


def get_jwt_service() -> JwtService:
    """FastAPI dependency for JWT service."""
    return _jwt_service


async def get_current_user(
    request: Request,
    token: str = Depends(oauth2_scheme),
    db: AsyncSession = Depends(get_db),
    jwt_svc: JwtService = Depends(get_jwt_service),
) -> User:
    """Decode JWT, validate, return current user. Sets tenant_id on request.state."""
    from ransomguard_grid.core.exceptions import AuthenticationError

    try:
        payload = jwt_svc.decode(token)
    except AuthenticationError:
        raise HTTPException(
            status_code=status.HTTP_401_UNAUTHORIZED,
            detail="Invalid authentication credentials",
            headers={"WWW-Authenticate": "Bearer"},
        ) from None

    if payload.token_type != "access":
        raise HTTPException(status.HTTP_401_UNAUTHORIZED, "Refresh token cannot be used for API access")

    if is_token_blacklisted(payload.jti):
        raise HTTPException(status.HTTP_401_UNAUTHORIZED, "Token revoked")

    user_repo = UserRepository(db, tenant_id=payload.tenant_id)
    user = await user_repo.get_by_id(payload.sub)
    if not user or not user.is_active:
        raise HTTPException(status.HTTP_401_UNAUTHORIZED, "User inactive or not found")

    request.state.tenant_id = payload.tenant_id
    request.state.user_roles = payload.roles
    return user


def require_roles(*required_roles: str):  # type: ignore[no-untyped-def]
    """Dependency factory enforcing user has at least one of the required roles."""

    async def check_roles(
        user: User = Depends(get_current_user),
        db: AsyncSession = Depends(get_db),
    ) -> User:
        role_repo = UserRoleRepository(db)
        user_roles = await role_repo.get_user_role_names(user.id)

        if not any(role in user_roles for role in required_roles):
            raise HTTPException(
                status_code=status.HTTP_403_FORBIDDEN,
                detail=f"Insufficient permissions. Required: {list(required_roles)}",
            )
        return user

    return check_roles
