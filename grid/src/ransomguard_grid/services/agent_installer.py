"""The installers this GRID serves, and their fingerprints.

nginx serves the agent packages directory at /agent/; grid-api mounts the same directory
read-only. Two installers live there:

* ``install.ps1`` (GRID-SEC-006): the console's scripted install command downloads it with TLS
  validation and refuses to run it unless its SHA-256 equals the one computed here, on the file
  actually served;
* the double-click installer (ADR-027): the CD publishes a *stub* (``RansomGuard-Installer-stub.exe``,
  a self-contained single-file .NET host) and the agent package; on every download from the
  console, this module appends to a copy of the stub the package, a small JSON configuration
  (GRID address, enrollment code, tenant, package SHA-256, GRID CA certificate) and a 56-byte
  trailer the stub reads back from its own file. Layout and trailer:

  ``[stub][zip][config JSON][trailer]`` with
  ``trailer = b"RGINST01" | zip length (int64 LE) | config length (int64 LE) | SHA-256(zip ‖ config)``.
"""

import hashlib
import json
import struct
from collections.abc import Iterator
from dataclasses import dataclass
from pathlib import Path
from typing import Any

INSTALLER_NAME = "install.ps1"
STUB_NAME = "RansomGuard-Installer-stub.exe"
PACKAGE_NAME = "RansomGuard-Agent-win-x64.zip"

TRAILER_MAGIC = b"RGINST01"
TRAILER = struct.Struct("<8sqq32s")  # 56 bytes, little-endian
assert TRAILER.size == 56

_CHUNK = 1024 * 1024


def installer_sha256(packages_dir: str) -> str | None:
    """Upper-case hex SHA-256 of the served install.ps1 (Get-FileHash's format), or None if absent."""
    path = Path(packages_dir) / INSTALLER_NAME
    try:
        data = path.read_bytes()
    except OSError:
        return None
    return hashlib.sha256(data).hexdigest().upper()


def _sha256_of_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(_CHUNK), b""):
            digest.update(chunk)
    return digest.hexdigest()


@dataclass(frozen=True)
class InstallerParts:
    """What a double-click installer is made of, located and measured before anything is sent."""

    stub: Path
    package: Path
    package_sha256: str  # lower-case hex, as the CD writes it next to the package
    ca_pem: str

    @property
    def stub_size(self) -> int:
        return self.stub.stat().st_size

    @property
    def package_size(self) -> int:
        return self.package.stat().st_size


def locate_installer_parts(packages_dir: str, ca_cert_path: str) -> InstallerParts | None:
    """The stub, the package and the CA certificate, or None when any of them is not published.

    The package's SHA-256 is read from the ``.sha256`` file the CD writes next to it when present
    (first token of its first line), otherwise computed on the file served.
    """
    base = Path(packages_dir)
    stub, package, ca = base / STUB_NAME, base / PACKAGE_NAME, Path(ca_cert_path)
    if not (stub.is_file() and package.is_file() and ca.is_file()):
        return None
    sha_file = base / f"{PACKAGE_NAME}.sha256"
    digest: str | None = None
    if sha_file.is_file():
        first = sha_file.read_text(encoding="ascii", errors="ignore").split()
        if first and len(first[0]) == 64:
            digest = first[0].lower()
    if digest is None:
        digest = _sha256_of_file(package)
    return InstallerParts(stub=stub, package=package, package_sha256=digest, ca_pem=ca.read_text(encoding="ascii"))


def installer_config_bytes(config: dict[str, Any]) -> bytes:
    """The JSON the stub parses (snake_case keys, compact)."""
    return json.dumps(config, separators=(",", ":"), ensure_ascii=False).encode("utf-8")


def installer_length(parts: InstallerParts, config: bytes) -> int:
    return parts.stub_size + parts.package_size + len(config) + TRAILER.size


def stream_installer(parts: InstallerParts, config: bytes) -> Iterator[bytes]:
    """Yields the installer: stub, package, config, trailer -- the SHA-256 computed on the way."""
    with parts.stub.open("rb") as stub:
        yield from iter(lambda: stub.read(_CHUNK), b"")
    digest = hashlib.sha256()
    with parts.package.open("rb") as package:
        for chunk in iter(lambda: package.read(_CHUNK), b""):
            digest.update(chunk)
            yield chunk
    digest.update(config)
    yield config
    yield TRAILER.pack(TRAILER_MAGIC, parts.package_size, len(config), digest.digest())
