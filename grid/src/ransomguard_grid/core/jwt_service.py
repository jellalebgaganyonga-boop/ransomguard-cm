"""JWT token creation, decoding, and blacklist management."""

import calendar
from datetime import UTC, datetime, timedelta
from uuid import uuid4

from jose import JWTError, jwt  # type: ignore[import-untyped]
from pydantic import BaseModel

from ransomguard_grid.core.exceptions import AuthenticationError


class JwtPayload(BaseModel):
    """Decoded JWT payload."""

    sub: str
    tenant_id: str
    roles: list[str]
    token_type: str
    iat: int
    exp: int
    jti: str


class JwtService:
    """Creates and verifies JWT tokens."""

    def __init__(self, secret_key: str, algorithm: str = "HS256") -> None:
        if len(secret_key) < 32:
            raise ValueError("JWT secret key must be at least 32 characters")
        self._secret = secret_key
        self._algorithm = algorithm

    def create_access_token(
        self, user_id: str, tenant_id: str, roles: list[str], expires_minutes: int = 60,
    ) -> str:
        """Create a short-lived access token."""
        now = datetime.now(UTC)
        payload = {
            "sub": user_id, "tenant_id": tenant_id, "roles": roles, "token_type": "access",
            "iat": calendar.timegm(now.utctimetuple()),
            "exp": calendar.timegm((now + timedelta(minutes=expires_minutes)).utctimetuple()),
            "jti": str(uuid4()),
        }
        encoded: str = jwt.encode(payload, self._secret, algorithm=self._algorithm)
        return encoded

    def create_refresh_token(self, user_id: str, tenant_id: str, expires_days: int = 7) -> str:
        """Create a long-lived refresh token."""
        now = datetime.now(UTC)
        payload = {
            "sub": user_id, "tenant_id": tenant_id, "roles": [], "token_type": "refresh",
            "iat": calendar.timegm(now.utctimetuple()),
            "exp": calendar.timegm((now + timedelta(days=expires_days)).utctimetuple()),
            "jti": str(uuid4()),
        }
        encoded: str = jwt.encode(payload, self._secret, algorithm=self._algorithm)
        return encoded

    def decode(self, token: str) -> JwtPayload:
        """Decode and validate a JWT. Raises AuthenticationError on failure."""
        try:
            raw: dict[str, object] = jwt.decode(token, self._secret, algorithms=[self._algorithm])
            return JwtPayload(**raw)  # type: ignore[arg-type]
        except JWTError as e:
            raise AuthenticationError(f"Invalid token: {e}") from e


# In-memory token blacklist (Redis migration in Section F)
_blacklisted_jtis: set[str] = set()


def blacklist_token(jti: str) -> None:
    """Add a JTI to the revocation blacklist."""
    _blacklisted_jtis.add(jti)


def is_token_blacklisted(jti: str) -> bool:
    """Check if a JTI has been revoked."""
    return jti in _blacklisted_jtis


def clear_blacklist() -> None:
    """Clear blacklist (for testing)."""
    _blacklisted_jtis.clear()
