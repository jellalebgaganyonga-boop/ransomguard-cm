"""Session and enrollment state, shared by every worker through Redis.

GRID-SEC-004, GRID-SEC-005 and GRID-ENR-001 had one cause: security state kept in the memory of
one process (a Python set of revoked JTIs, a dict of enrollment OTPs) while production runs
``uvicorn --workers 2``. A token revoked on one worker stayed valid on the other; an OTP created
on one worker was unknown to the other. Everything that decides whether a session or an OTP is
still valid now lives here, in Redis, and every worker reads the same answer.

Keys (all with a TTL, nothing is kept forever):

* ``rg:revoked:{jti}``          an access token revoked before its expiry (logout);
* ``rg:refresh:{jti}``          a refresh token still unused -> its family. Consumed with GETDEL:
                                a token comes back only once;
* ``rg:family-revoked:{fam}``   a session family (one login) revoked: logout, or a refresh token
                                replayed -- the sign of a theft;
* ``rg:user-version:{user}``    per-user session version; "log out every session" increments it,
                                and tokens carrying an older version are refused;
* ``rg:otp:{otp}``              an enrollment OTP -> its tenant. Consumed with GETDEL: one use,
                                even with two simultaneous requests.

Fail closed: if Redis cannot be reached, :class:`SessionStoreUnavailableError` is raised and the
caller answers 503. A possibly revoked token is never accepted because the store was silent.
"""

from collections.abc import Awaitable
from typing import TypeVar

from redis.asyncio import Redis
from redis.exceptions import RedisError

from ransomguard_grid.core.config import get_settings

_REVOKED = "rg:revoked:"
_REFRESH = "rg:refresh:"
_FAMILY_REVOKED = "rg:family-revoked:"
_USER_VERSION = "rg:user-version:"
_OTP = "rg:otp:"

# A revoked family must outlive every refresh token of the family.
_FAMILY_TTL_SECONDS = 8 * 24 * 3600

T = TypeVar("T")


class SessionStoreUnavailableError(Exception):
    """Redis could not be reached: authentication must refuse (503), never guess."""


class SessionStore:
    def __init__(self, redis: Redis) -> None:
        self._redis = redis

    # ── Access tokens ──

    async def revoke_access(self, jti: str, ttl_seconds: int) -> None:
        """Revoke an access token for the rest of its life (at least one second)."""
        await self._call(self._redis.set(_REVOKED + jti, "1", ex=max(1, ttl_seconds)))

    async def is_access_revoked(self, jti: str) -> bool:
        return bool(await self._call(self._redis.exists(_REVOKED + jti)))

    # ── Refresh tokens and their family ──

    async def register_refresh(self, jti: str, family: str, ttl_seconds: int) -> None:
        await self._call(self._redis.set(_REFRESH + jti, family, ex=max(1, ttl_seconds)))

    async def consume_refresh(self, jti: str) -> str | None:
        """The family of a refresh token used for the first time, or None if it was already used
        (or never issued). Atomic: of two simultaneous uses, only one gets the family."""
        value = await self._call(self._redis.getdel(_REFRESH + jti))
        return None if value is None else _text(value)

    async def revoke_family(self, family: str) -> None:
        await self._call(self._redis.set(_FAMILY_REVOKED + family, "1", ex=_FAMILY_TTL_SECONDS))

    async def is_family_revoked(self, family: str) -> bool:
        return bool(await self._call(self._redis.exists(_FAMILY_REVOKED + family)))

    # ── "Log out every session" ──

    async def user_version(self, user_id: str) -> int:
        value = await self._call(self._redis.get(_USER_VERSION + user_id))
        return int(_text(value)) if value is not None else 0

    async def bump_user_version(self, user_id: str) -> int:
        """Invalidate every token issued so far to this user; returns the new version."""
        return int(await self._call(self._redis.incr(_USER_VERSION + user_id)))

    # ── Enrollment OTPs ──

    async def store_otp(self, otp: str, tenant_id: str, ttl_seconds: int) -> None:
        stored = await self._call(self._redis.set(_OTP + otp, tenant_id, ex=ttl_seconds, nx=True))
        if not stored:
            raise ValueError("OTP collision")

    async def consume_otp(self, otp: str) -> str | None:
        """The tenant of a valid OTP, consumed atomically (one use), or None."""
        value = await self._call(self._redis.getdel(_OTP + otp))
        return None if value is None else _text(value)

    @staticmethod
    async def _call(awaitable: Awaitable[T]) -> T:
        try:
            return await awaitable
        except (RedisError, OSError) as exc:
            raise SessionStoreUnavailableError(str(exc)) from exc


def _text(value: bytes | str) -> str:
    return value.decode() if isinstance(value, bytes) else value


_store: SessionStore | None = None


def get_session_store() -> SessionStore:
    """FastAPI dependency: one Redis client per process, from settings.redis_url.

    Short timeouts: an unreachable Redis must turn into a quick 503, not a hung login.
    """
    global _store
    if _store is None:
        redis = Redis.from_url(
            get_settings().redis_url, socket_connect_timeout=2, socket_timeout=2, health_check_interval=30,
        )
        _store = SessionStore(redis)
    return _store
