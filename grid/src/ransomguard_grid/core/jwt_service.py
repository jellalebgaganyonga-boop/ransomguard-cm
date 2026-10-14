"""JWT token creation and decoding. Revocation lives in Redis (core/session_store.py)."""

import calendar
from datetime import UTC, datetime, timedelta
from uuid import uuid4

import jwt
from pydantic import BaseModel, ValidationError

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
    # Session family: one per login, shared by the access and refresh tokens it produces; revoked
    # as a whole on logout or when a refresh token is replayed.
    fam: str
    # Per-user session version at issue time; "log out every session" increments the current one.
    ver: int


class JwtService:
    """Creates and verifies JWT tokens. HS256 only, by design."""

    def __init__(self, secret_key: str) -> None:
        if len(secret_key) < 32:
            raise ValueError("JWT secret key must be at least 32 characters")
        self._secret = secret_key

    def create_access_token(
        self, user_id: str, tenant_id: str, roles: list[str], family: str, version: int,
        expires_minutes: int = 60,
    ) -> str:
        """Create a short-lived access token."""
        now = datetime.now(UTC)
        payload = {
            "sub": user_id, "tenant_id": tenant_id, "roles": roles, "token_type": "access",
            "iat": calendar.timegm(now.utctimetuple()),
            "exp": calendar.timegm((now + timedelta(minutes=expires_minutes)).utctimetuple()),
            "jti": str(uuid4()), "fam": family, "ver": version,
        }
        encoded: str = jwt.encode(payload, self._secret, algorithm="HS256")
        return encoded

    def create_refresh_token(
        self, user_id: str, tenant_id: str, family: str, version: int, expires_days: int = 7,
    ) -> str:
        """Create a long-lived refresh token."""
        now = datetime.now(UTC)
        payload = {
            "sub": user_id, "tenant_id": tenant_id, "roles": [], "token_type": "refresh",
            "iat": calendar.timegm(now.utctimetuple()),
            "exp": calendar.timegm((now + timedelta(days=expires_days)).utctimetuple()),
            "jti": str(uuid4()), "fam": family, "ver": version,
        }
        encoded: str = jwt.encode(payload, self._secret, algorithm="HS256")
        return encoded

    def decode(self, token: str) -> JwtPayload:
        """Decode and validate a JWT. Raises AuthenticationError on failure."""
        try:
            # Explicit, fixed algorithm list: never taken from the token's header.
            # Rules out "alg: none" and algorithm confusion (a public key used as
            # an HMAC secret, CVE-2026-85394 in python-jose).
            raw: dict[str, object] = jwt.decode(token, self._secret, algorithms=["HS256"])
        except jwt.PyJWTError as e:
            raise AuthenticationError(f"Invalid token: {e}") from e
        try:
            # A token issued before session families and versions (fam, ver) is refused: it cannot
            # be revoked, so it cannot be trusted.
            return JwtPayload(**raw)  # type: ignore[arg-type]
        except ValidationError as e:
            raise AuthenticationError("Invalid token: missing or malformed claims") from e
