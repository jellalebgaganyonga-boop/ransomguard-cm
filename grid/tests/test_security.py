"""Tests for security utilities."""

from ransomguard_grid.core.security import hash_password, verify_password


def test_password_hash_and_verify() -> None:
    """Password hashing and verification should work correctly."""
    plain = "SecureP@ssw0rd!"
    hashed = hash_password(plain)
    assert hashed != plain
    assert verify_password(plain, hashed)
    assert not verify_password("wrong", hashed)
