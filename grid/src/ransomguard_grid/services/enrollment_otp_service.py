"""Enrollment OTP generation and one-time consumption, in Redis (GRID-ENR-001).

The OTPs used to live in a dictionary in the memory of one process: lost at every restart, and
with ``uvicorn --workers 2`` an OTP created on one worker was unknown to the other -- enrollment
failed about one time in two. They now live in the shared session store with their TTL, and are
consumed atomically (GETDEL): an OTP serves once, even with two simultaneous requests.
"""

import secrets
import string
from datetime import UTC, datetime

from ransomguard_grid.core.session_store import SessionStore

_ALPHABET = string.ascii_uppercase + string.digits


class EnrollmentOtpService:
    """Issues and consumes one-time enrollment tokens for agent onboarding."""

    def __init__(self, store: SessionStore) -> None:
        self._store = store

    async def generate(self, tenant_id: str, validity_minutes: int = 30) -> str:
        """Generate an OTP like 'RG-CAM-2026-X7K9P3', valid for ``validity_minutes``."""
        while True:
            random_part = "".join(secrets.choice(_ALPHABET) for _ in range(6))
            otp = f"RG-CAM-{datetime.now(UTC).year}-{random_part}"
            try:
                await self._store.store_otp(otp, tenant_id, validity_minutes * 60)
                return otp
            except ValueError:
                continue  # collision with a live OTP: draw again

    async def validate_and_consume(self, otp: str) -> str | None:
        """The tenant of a valid OTP, consumed (one use); None if unknown, expired or used."""
        return await self._store.consume_otp(otp)
