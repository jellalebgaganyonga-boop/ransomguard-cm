"""Tests for security utilities."""

from ransomguard_grid.core.security import (
    create_access_token,
    decode_access_token,
    hash_password,
    verify_password,
)


def test_password_hash_and_verify() -> None:
    """Password hashing and verification should work correctly."""
    plain = "SecureP@ssw0rd!"
    hashed = hash_password(plain)
    assert hashed != plain
    assert verify_password(plain, hashed)
    assert not verify_password("wrong", hashed)


def test_jwt_create_and_decode() -> None:
    """JWT creation and decoding should preserve claims."""
    token = create_access_token({"sub": "user-123", "tenant_id": "tenant-A", "roles": ["admin"]})
    payload = decode_access_token(token)
    assert payload["sub"] == "user-123"
    assert payload["tenant_id"] == "tenant-A"
    assert "admin" in payload["roles"]
