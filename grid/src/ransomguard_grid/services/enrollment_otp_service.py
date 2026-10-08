"""Enrollment OTP generation and validation. In-memory for Section C, Redis in Section F."""

import secrets
import string
from dataclasses import dataclass
from datetime import UTC, datetime, timedelta


@dataclass
class EnrollmentOtp:
    """Enrollment OTP with tenant association and expiration."""

    tenant_id: str
    expires_at: datetime
    consumed: bool = False


class EnrollmentOtpService:
    """Manages one-time enrollment tokens for agent onboarding.

    Section C: in-memory dictionary.
    Section F: Redis-backed with TTL.
    """

    def __init__(self) -> None:
        self._otps: dict[str, EnrollmentOtp] = {}

    def generate(self, tenant_id: str, validity_minutes: int = 30) -> str:
        """Generate a new OTP string like 'RG-CAM-2026-X7K9P3'."""
        random_part = "".join(secrets.choice(string.ascii_uppercase + string.digits) for _ in range(6))
        year = datetime.now(UTC).year
        otp_string = f"RG-CAM-{year}-{random_part}"

        self._otps[otp_string] = EnrollmentOtp(
            tenant_id=tenant_id,
            expires_at=datetime.now(UTC) + timedelta(minutes=validity_minutes),
        )
        return otp_string

    def validate_and_consume(self, otp_string: str) -> EnrollmentOtp | None:
        """Validate OTP and consume it (one-time use). Returns None if invalid/expired."""
        otp = self._otps.get(otp_string)
        if otp is None:
            return None
        if otp.consumed:
            return None
        if datetime.now(UTC) > otp.expires_at:
            del self._otps[otp_string]
            return None

        otp.consumed = True
        del self._otps[otp_string]
        return otp

    def cleanup_expired(self) -> int:
        """Remove expired OTPs. Returns count removed."""
        now = datetime.now(UTC)
        expired = [k for k, v in self._otps.items() if now > v.expires_at]
        for k in expired:
            del self._otps[k]
        return len(expired)


# Module-level singleton — shared by enrollment and dashboard routes.
_shared_otp_service = EnrollmentOtpService()


def get_shared_otp_service() -> EnrollmentOtpService:
    """Return the process-wide OTP service singleton."""
    return _shared_otp_service
