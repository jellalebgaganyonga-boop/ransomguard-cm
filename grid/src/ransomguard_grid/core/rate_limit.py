"""In-memory rate limiter for Section C. Redis migration in Section F."""

import asyncio
import time


class InMemoryRateLimiter:
    """Sliding window rate limiter using in-memory counters."""

    def __init__(self) -> None:
        self._counters: dict[str, list[float]] = {}
        self._lock = asyncio.Lock()

    async def check_and_increment(self, key: str, limit: int, window_seconds: int) -> bool:
        """Return True if request allowed, False if rate limit exceeded."""
        async with self._lock:
            now = time.time()
            window_start = now - window_seconds

            if key not in self._counters:
                self._counters[key] = []

            self._counters[key] = [t for t in self._counters[key] if t > window_start]

            if len(self._counters[key]) >= limit:
                return False

            self._counters[key].append(now)
            return True


_rate_limiter = InMemoryRateLimiter()


def get_rate_limiter() -> InMemoryRateLimiter:
    """FastAPI dependency for rate limiter."""
    return _rate_limiter
