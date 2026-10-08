"""JWT security tests, written before the python-jose -> PyJWT migration.

They must pass unchanged before and after it. To stay library-agnostic,
every hostile token is built by hand here (base64url + stdlib HMAC, and
`cryptography` for genuine RSA/EC signatures): nothing below imports
python-jose or PyJWT.

Cross-tenant access (a token of tenant A on a resource of tenant B -> 404)
is covered by tests/api/test_tenant_isolation.py
(test_user_a_gets_404_for_tenant_b_alert, test_user_a_gets_404_for_tenant_b_agent).
"""

import base64
import hashlib
import hmac
import json
import time
from collections.abc import Callable
from functools import cache

import pytest
from cryptography.hazmat.primitives import hashes, serialization
from cryptography.hazmat.primitives.asymmetric import ec, padding, rsa
from cryptography.hazmat.primitives.asymmetric.utils import decode_dss_signature
from httpx import AsyncClient

from ransomguard_grid.core.exceptions import AuthenticationError
from ransomguard_grid.core.jwt_service import JwtService
from ransomguard_grid.core.security import create_access_token, decode_access_token
from tests.api.test_tenant_isolation import _create_two_tenants, _login

_SECRET = "jwt-security-test-secret-at-least-32-chars"
# Must match tests/conftest.py (GRID_JWT_SECRET_KEY), used by the running app.
_APP_SECRET = "test-secret-key-minimum-32-characters-long!"

# Issued by python-jose 3.5.0 (HS256) before the migration, with the same claim
# shape as JwtService.create_access_token. It proves that tokens already in
# circulation stay valid: same secret, same algorithm, same claims.
_GOLDEN_SECRET = "golden-secret-key-for-jwt-compat-test-0001"
_GOLDEN_TOKEN_FROM_PYTHON_JOSE = (
    "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9."
    "eyJzdWIiOiJ1c2VyLWdvbGRlbi0wMDEiLCJ0ZW5hbnRfaWQiOiJ0ZW5hbnQtZ29sZGVuLUEiLCJyb2xlcyI6WyJ0ZW5hbnRfYWRtaW4iXSwidG9rZW5fdHlwZSI6"
    "ImFjY2VzcyIsImlhdCI6MTc1OTkwMDAwMCwiZXhwIjo0MTAyNDQ0ODAwLCJqdGkiOiI2ZjFjMmI5ZS0wZDRhLTRjMWUtOWE3Ny0zYjJmNWU4ZDFhMDEifQ."
    "HFABi8Z5knbWdjbw6Ar_-NigNrqEStJz1pa48e2j03o"
)


# ── Hand-made tokens ────────────────────────────────────────────


def _b64url(data: bytes) -> str:
    return base64.urlsafe_b64encode(data).rstrip(b"=").decode()


def _claims(tenant_id: str = "tenant-A", exp_offset: int = 3600) -> dict[str, object]:
    now = int(time.time())
    return {
        "sub": "user-123", "tenant_id": tenant_id, "roles": ["tenant_admin"], "token_type": "access",
        "iat": now - 10, "exp": now + exp_offset, "jti": "11111111-2222-3333-4444-555555555555",
    }


def _signing_input(alg: str, claims: dict[str, object]) -> bytes:
    header = _b64url(json.dumps({"alg": alg, "typ": "JWT"}, separators=(",", ":")).encode())
    payload = _b64url(json.dumps(claims, separators=(",", ":")).encode())
    return f"{header}.{payload}".encode()


def _hmac_token(alg: str, key: bytes, claims: dict[str, object]) -> str:
    """Token whose header says `alg`, signed with HMAC-SHA* under `key`."""
    digest = {"HS256": hashlib.sha256, "HS384": hashlib.sha384, "HS512": hashlib.sha512}.get(alg, hashlib.sha256)
    signing_input = _signing_input(alg, claims)
    return f"{signing_input.decode()}.{_b64url(hmac.new(key, signing_input, digest).digest())}"


def _unsigned_token(alg: str, claims: dict[str, object]) -> str:
    return f"{_signing_input(alg, claims).decode()}."


def _rsa_keys() -> tuple[rsa.RSAPrivateKey, bytes, bytes]:
    private = rsa.generate_private_key(public_exponent=65537, key_size=2048)
    public = private.public_key()
    pem = public.public_bytes(serialization.Encoding.PEM, serialization.PublicFormat.SubjectPublicKeyInfo)
    der = public.public_bytes(serialization.Encoding.DER, serialization.PublicFormat.SubjectPublicKeyInfo)
    return private, pem, der


def _ec_keys() -> tuple[ec.EllipticCurvePrivateKey, bytes, bytes]:
    private = ec.generate_private_key(ec.SECP256R1())
    public = private.public_key()
    pem = public.public_bytes(serialization.Encoding.PEM, serialization.PublicFormat.SubjectPublicKeyInfo)
    der = public.public_bytes(serialization.Encoding.DER, serialization.PublicFormat.SubjectPublicKeyInfo)
    return private, pem, der


def _genuine_rs256(private: rsa.RSAPrivateKey, claims: dict[str, object]) -> str:
    signing_input = _signing_input("RS256", claims)
    signature = private.sign(signing_input, padding.PKCS1v15(), hashes.SHA256())
    return f"{signing_input.decode()}.{_b64url(signature)}"


def _genuine_es256(private: ec.EllipticCurvePrivateKey, claims: dict[str, object]) -> str:
    signing_input = _signing_input("ES256", claims)
    r, s = decode_dss_signature(private.sign(signing_input, ec.ECDSA(hashes.SHA256())))
    return f"{signing_input.decode()}.{_b64url(r.to_bytes(32, 'big') + s.to_bytes(32, 'big'))}"


@cache
def _forged_tokens() -> dict[str, str]:
    """Every token here must be refused by a verifier that only trusts HS256 + _SECRET."""
    rsa_private, rsa_pem, rsa_der = _rsa_keys()
    ec_private, ec_pem, ec_der = _ec_keys()
    claims = _claims()
    return {
        # alg: none, in the spellings attackers try
        "alg_none": _unsigned_token("none", claims),
        "alg_None": _unsigned_token("None", claims),
        "alg_NONE": _unsigned_token("NONE", claims),
        # Algorithm confusion: asymmetric alg in the header, HMAC under the public key
        "RS256_header_hmac_with_rsa_public_pem": _hmac_token("RS256", rsa_pem, claims),
        "RS256_header_hmac_with_rsa_public_der": _hmac_token("RS256", rsa_der, claims),
        "ES256_header_hmac_with_ec_public_pem": _hmac_token("ES256", ec_pem, claims),
        "ES256_header_hmac_with_ec_public_der": _hmac_token("ES256", ec_der, claims),
        # The CVE-2026-85394 shape: HS256 keyed with a DER public key
        "HS256_hmac_with_rsa_public_der": _hmac_token("HS256", rsa_der, claims),
        "HS256_hmac_with_ec_public_der": _hmac_token("HS256", ec_der, claims),
        # Genuine asymmetric signatures: valid for their key, not for us
        "genuine_RS256": _genuine_rs256(rsa_private, claims),
        "genuine_ES256": _genuine_es256(ec_private, claims),
        # Right secret, wrong HMAC algorithm: the allowed list is fixed, never read from the token
        "HS384_with_right_secret": _hmac_token("HS384", _SECRET.encode(), claims),
        "HS512_with_right_secret": _hmac_token("HS512", _SECRET.encode(), claims),
    }


# ── Round trip ──────────────────────────────────────────────────


def test_valid_token_round_trips_with_its_claims() -> None:
    service = JwtService(_SECRET)
    before = int(time.time())

    token = service.create_access_token("user-123", "tenant-A", ["tenant_admin"], expires_minutes=60)
    payload = service.decode(token)

    assert payload.sub == "user-123"
    assert payload.tenant_id == "tenant-A"
    assert payload.roles == ["tenant_admin"]
    assert payload.token_type == "access"
    assert payload.exp - payload.iat == 3600
    assert before - 1 <= payload.iat <= int(time.time()) + 1
    assert payload.jti


def test_hand_made_hs256_token_with_right_secret_is_accepted() -> None:
    """Sanity check of the helpers: the refusals below are not an artefact of a broken builder."""
    payload = JwtService(_SECRET).decode(_hmac_token("HS256", _SECRET.encode(), _claims()))
    assert payload.tenant_id == "tenant-A"


def test_token_issued_by_python_jose_is_still_valid() -> None:
    payload = JwtService(_GOLDEN_SECRET).decode(_GOLDEN_TOKEN_FROM_PYTHON_JOSE)

    assert payload.sub == "user-golden-001"
    assert payload.tenant_id == "tenant-golden-A"
    assert payload.roles == ["tenant_admin"]
    assert payload.token_type == "access"
    assert payload.iat == 1759900000
    assert payload.exp == 4102444800
    assert payload.jti == "6f1c2b9e-0d4a-4c1e-9a77-3b2f5e8d1a01"


# ── Refusals: JwtService (production path) ──────────────────────


@pytest.mark.parametrize("name", sorted(_forged_tokens()))
def test_forged_token_is_refused(name: str) -> None:
    token = _forged_tokens()[name]
    with pytest.raises(AuthenticationError):
        JwtService(_SECRET).decode(token)


def test_modified_signature_is_refused() -> None:
    service = JwtService(_SECRET)
    header, payload, signature = service.create_access_token("user-123", "tenant-A", ["tenant_admin"]).split(".")
    # Change the FIRST signature character: the last one partly carries padding
    # bits, so changing it can leave the decoded bytes identical.
    tampered = f"{header}.{payload}.{'B' if signature[0] != 'B' else 'C'}{signature[1:]}"
    with pytest.raises(AuthenticationError):
        service.decode(tampered)


def test_modified_payload_with_original_signature_is_refused() -> None:
    service = JwtService(_SECRET)
    header, _, signature = service.create_access_token("user-123", "tenant-A", ["tenant_admin"]).split(".")
    other_payload = _b64url(json.dumps(_claims(tenant_id="tenant-B")).encode())
    with pytest.raises(AuthenticationError):
        service.decode(f"{header}.{other_payload}.{signature}")


def test_expired_token_is_refused() -> None:
    service = JwtService(_SECRET)
    with pytest.raises(AuthenticationError):
        service.decode(service.create_access_token("user-123", "tenant-A", ["tenant_admin"], expires_minutes=-5))
    with pytest.raises(AuthenticationError):
        service.decode(_hmac_token("HS256", _SECRET.encode(), _claims(exp_offset=-60)))


# ── Refusals: core/security.py helpers ──────────────────────────
# decode_access_token re-raises the library's own error (python-jose JWTError,
# PyJWT InvalidTokenError), so only "it raises" is asserted here.


def test_security_helpers_round_trip() -> None:
    payload = decode_access_token(create_access_token({"sub": "user-123", "tenant_id": "tenant-A", "roles": ["admin"]}))
    assert payload["sub"] == "user-123"
    assert payload["tenant_id"] == "tenant-A"


@pytest.mark.parametrize(
    "make_token",
    [
        pytest.param(lambda: _unsigned_token("none", _claims()), id="alg_none"),
        pytest.param(lambda: _hmac_token("HS256", _rsa_keys()[2], _claims()), id="HS256_hmac_with_rsa_public_der"),
        pytest.param(lambda: _hmac_token("RS256", _rsa_keys()[1], _claims()), id="RS256_header_hmac_with_rsa_public_pem"),
        pytest.param(lambda: _hmac_token("HS256", _APP_SECRET.encode(), _claims(exp_offset=-60)), id="expired"),
        pytest.param(lambda: _hmac_token("HS512", _APP_SECRET.encode(), _claims()), id="HS512_with_right_secret"),
    ],
)
def test_security_decode_refuses_hostile_tokens(make_token: Callable[[], str]) -> None:
    with pytest.raises(Exception):  # noqa: B017 -- library-specific error type, see above
        decode_access_token(make_token())


# ── Refusals through the API ────────────────────────────────────


@pytest.mark.asyncio
@pytest.mark.parametrize(
    "make_token",
    [
        pytest.param(lambda tenant_b: _unsigned_token("none", _claims(tenant_id=tenant_b)), id="alg_none"),
        pytest.param(lambda tenant_b: _hmac_token("HS256", _rsa_keys()[2], _claims(tenant_id=tenant_b)), id="algorithm_confusion"),
        pytest.param(lambda tenant_b: _hmac_token("HS256", _APP_SECRET.encode(), _claims(tenant_id=tenant_b, exp_offset=-60)), id="expired"),
    ],
)
async def test_api_rejects_hostile_tokens_with_401(client: AsyncClient, make_token: Callable[[str], str]) -> None:
    setup = await _create_two_tenants()
    # A real login first, so a 401 below cannot come from an empty database.
    token_a = await _login(client, setup["tenant_a_code"], setup["user_a_email"])
    ok = await client.get("/api/v1/dashboard/alerts", headers={"Authorization": f"Bearer {token_a}"})
    assert ok.status_code == 200

    resp = await client.get("/api/v1/dashboard/alerts", headers={"Authorization": f"Bearer {make_token(setup['tenant_b_id'])}"})
    assert resp.status_code == 401
