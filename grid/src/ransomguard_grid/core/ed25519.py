"""Ed25519 signature verification for audit log integrity (CWE-345)."""

import base64
import json

from cryptography.hazmat.primitives.asymmetric.ed25519 import Ed25519PublicKey
from cryptography.hazmat.primitives.serialization import load_pem_public_key


def verify_ed25519_signature(
    payload: dict[str, object],
    signature_base64: str,
    public_key_pem: str,
) -> bool:
    """Verify Ed25519 signature over canonical JSON of payload.

    Canonical JSON: sort_keys=True, separators=(',', ':'), ensure_ascii=False.
    This MUST match the agent's C# JsonSerializer(WriteIndented=false) output.
    """
    try:
        key = load_pem_public_key(public_key_pem.encode())
        if not isinstance(key, Ed25519PublicKey):
            return False

        canonical_json = json.dumps(payload, sort_keys=True, separators=(",", ":"), ensure_ascii=False)
        message_bytes = canonical_json.encode("utf-8")
        signature_bytes = base64.b64decode(signature_base64)

        key.verify(signature_bytes, message_bytes)
        return True
    except Exception:
        return False
