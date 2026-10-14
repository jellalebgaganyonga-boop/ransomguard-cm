"""The installer this GRID serves, and its fingerprint (GRID-SEC-006).

nginx serves the agent packages directory at /agent/; grid-api mounts the same directory
read-only. The console's install command downloads /agent/install.ps1 with TLS validation and
refuses to run it unless its SHA-256 equals the one computed here, on the file actually served.
"""

import hashlib
from pathlib import Path

INSTALLER_NAME = "install.ps1"


def installer_sha256(packages_dir: str) -> str | None:
    """Upper-case hex SHA-256 of the served install.ps1 (Get-FileHash's format), or None if absent."""
    path = Path(packages_dir) / INSTALLER_NAME
    try:
        data = path.read_bytes()
    except OSError:
        return None
    return hashlib.sha256(data).hexdigest().upper()
