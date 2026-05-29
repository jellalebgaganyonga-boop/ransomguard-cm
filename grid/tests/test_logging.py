"""Tests for structured logging."""

from ransomguard_grid.core.logging import redact_sensitive_fields


def test_redact_sensitive_fields() -> None:
    """Sensitive fields should be replaced with [REDACTED]."""
    event = {
        "event": "login",
        "password": "secret123",
        "token": "eyJhbG...",
        "username": "admin",
    }
    result = redact_sensitive_fields(None, "", event)  # type: ignore[arg-type]
    assert result["password"] == "[REDACTED]"
    assert result["token"] == "[REDACTED]"
    assert result["username"] == "admin"  # not redacted


def test_redact_is_case_insensitive() -> None:
    """Redaction should be case-insensitive."""
    event = {"API_KEY": "abc123", "event": "test"}
    result = redact_sensitive_fields(None, "", event)  # type: ignore[arg-type]
    assert result["API_KEY"] == "[REDACTED]"
