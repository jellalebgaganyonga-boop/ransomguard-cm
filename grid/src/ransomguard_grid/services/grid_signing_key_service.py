"""GRID Ed25519 signing key management for threat intel packages."""

import os
from pathlib import Path

from cryptography.hazmat.primitives.asymmetric.ed25519 import Ed25519PrivateKey
from cryptography.hazmat.primitives.serialization import (
    Encoding,
    NoEncryption,
    PrivateFormat,
    PublicFormat,
    load_pem_private_key,
)

from ransomguard_grid.core.logging import get_logger

logger = get_logger("grid_signing")


class GridSigningKeyService:
    """Manages GRID's Ed25519 signing keys for threat intel packages.

    Keys are persisted to PEM files. Production: use HSM or hardware secure storage.
    """

    def __init__(self, private_key_path: Path) -> None:
        self.private_key_path = private_key_path
        self.public_key_path = Path(str(private_key_path) + ".pub")
        self._private_key: Ed25519PrivateKey | None = None

    def initialize(self) -> None:
        """Load existing keys or generate new ones."""
        if self.private_key_path.exists():
            self._load_existing()
            logger.info("GRID signing key loaded", path=str(self.private_key_path))
        else:
            self._generate_new()
            logger.info("GRID signing key generated", path=str(self.private_key_path))

    def _generate_new(self) -> None:
        key = Ed25519PrivateKey.generate()
        self.private_key_path.parent.mkdir(parents=True, exist_ok=True)

        private_pem = key.private_bytes(Encoding.PEM, PrivateFormat.PKCS8, NoEncryption())
        self.private_key_path.write_bytes(private_pem)
        try:
            os.chmod(self.private_key_path, 0o600)
        except OSError:
            pass  # Windows doesn't support chmod the same way

        public_pem = key.public_key().public_bytes(Encoding.PEM, PublicFormat.SubjectPublicKeyInfo)
        self.public_key_path.write_bytes(public_pem)

        self._private_key = key

    def _load_existing(self) -> None:
        pem_data = self.private_key_path.read_bytes()
        key = load_pem_private_key(pem_data, password=None)
        if not isinstance(key, Ed25519PrivateKey):
            raise TypeError("Expected Ed25519 private key")
        self._private_key = key

    def sign(self, message: bytes) -> bytes:
        """Sign message bytes with GRID Ed25519 private key."""
        if self._private_key is None:
            raise RuntimeError("GridSigningKeyService not initialized")
        return self._private_key.sign(message)

    def get_public_key_pem(self) -> str:
        """Return public key in PEM format."""
        if self._private_key is None:
            raise RuntimeError("GridSigningKeyService not initialized")
        return self._private_key.public_key().public_bytes(
            Encoding.PEM, PublicFormat.SubjectPublicKeyInfo
        ).decode()
