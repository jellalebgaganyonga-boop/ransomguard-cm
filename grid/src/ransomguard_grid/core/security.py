"""Security utilities: JWT creation/verification, password hashing."""

import calendar
from datetime import UTC, datetime, timedelta

import bcrypt
from jose import JWTError, jwt  # type: ignore[import-untyped]

from ransomguard_grid.core.config import get_settings

_settings = get_settings()


def hash_password(password: str) -> str:
    """Hash a password using bcrypt."""
    return bcrypt.hashpw(password.encode("utf-8"), bcrypt.gensalt()).decode("utf-8")


def verify_password(plain: str, hashed: str) -> bool:
    """Verify a password against its bcrypt hash."""
    return bcrypt.checkpw(plain.encode("utf-8"), hashed.encode("utf-8"))


def create_access_token(
    data: dict[str, str | list[str]],
    expires_delta: timedelta | None = None,
) -> str:
    """Create a JWT access token with integer exp claim."""
    to_encode: dict[str, object] = dict(data)
    expire = datetime.now(UTC) + (
        expires_delta or timedelta(minutes=_settings.jwt_access_token_expire_minutes)
    )
    to_encode["exp"] = calendar.timegm(expire.utctimetuple())
    encoded: str = jwt.encode(to_encode, _settings.jwt_secret_key, algorithm=_settings.jwt_algorithm)
    return encoded


def decode_access_token(token: str) -> dict[str, str | list[str]]:
    """Decode and verify a JWT access token. Raises JWTError on failure."""
    try:
        payload: dict[str, str | list[str]] = jwt.decode(
            token, _settings.jwt_secret_key, algorithms=[_settings.jwt_algorithm]
        )
        return payload
    except JWTError:
        raise
