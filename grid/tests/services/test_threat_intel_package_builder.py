"""Tests for threat intel package builder."""

import json
import zipfile
from datetime import UTC, datetime
from pathlib import Path
from tempfile import TemporaryDirectory

import pytest

from ransomguard_grid.services.grid_signing_key_service import GridSigningKeyService
from ransomguard_grid.services.threat_intel_aggregator import AggregatedThreatIntel
from ransomguard_grid.services.threat_intel_package_builder import ThreatIntelPackageBuilder
from ransomguard_grid.services.threat_intel_sources import ThreatIntelEntry


def _make_aggregated() -> AggregatedThreatIntel:
    """Create sample aggregated data."""
    return AggregatedThreatIntel(
        entries=[
            ThreatIntelEntry("1.2.3.4", "ipv4", "tor_project", 95, tags=["tor_exit_node"]),
            ThreatIntelEntry("5.6.7.8", "ipv4", "threatfox", 90, tags=["c2"]),
            ThreatIntelEntry("10.0.0.0/8", "cidr", "aws_ip_ranges", 100, tags=["aws", "amazon"]),
        ],
        source_status={"tor_project": "ok: 1", "threatfox": "ok: 1", "aws": "ok: 1"},
        total_entries=3,
        generated_at=datetime(2026, 5, 29, tzinfo=UTC),
    )


@pytest.fixture
def signing_service(tmp_path: Path) -> GridSigningKeyService:
    """Provide an initialized signing service."""
    svc = GridSigningKeyService(tmp_path / "test-key.pem")
    svc.initialize()
    return svc


def test_package_creates_zip_with_6_files(signing_service: GridSigningKeyService, tmp_path: Path) -> None:
    """Built package should contain manifest + 4 IOC files + signature.bin."""
    builder = ThreatIntelPackageBuilder(signing_service, tmp_path / "out")
    zip_path = builder.build(_make_aggregated(), "2026-05-29-001")

    assert zip_path.exists()
    with zipfile.ZipFile(zip_path) as zf:
        names = sorted(zf.namelist())
    assert names == sorted([
        "manifest.json", "tor-exit-nodes.json", "known-c2-servers.json",
        "cloud-providers.json", "lolbas-binaries.json", "signature.bin",
    ])


def test_package_signature_verifies_with_public_key(signing_service: GridSigningKeyService, tmp_path: Path) -> None:
    """The signature in the ZIP should verify against the GRID public key."""
    from cryptography.hazmat.primitives.serialization import load_pem_public_key

    builder = ThreatIntelPackageBuilder(signing_service, tmp_path / "out")
    zip_path = builder.build(_make_aggregated(), "2026-05-29-002")

    pub_pem = signing_service.get_public_key_pem().encode()
    pub_key = load_pem_public_key(pub_pem)

    with zipfile.ZipFile(zip_path) as zf:
        signature = zf.read("signature.bin")

        # Reconstruct signature payload (same as builder)
        file_names = sorted(n for n in zf.namelist() if n != "signature.bin")
        sig_payload = b""
        for name in file_names:
            sig_payload += name.encode() + b":" + zf.read(name) + b"\n"

    # Should not raise
    pub_key.verify(signature, sig_payload)  # type: ignore[union-attr]


def test_package_files_use_canonical_json(signing_service: GridSigningKeyService, tmp_path: Path) -> None:
    """All JSON files should use canonical format (sorted keys, no spaces)."""
    builder = ThreatIntelPackageBuilder(signing_service, tmp_path / "out")
    zip_path = builder.build(_make_aggregated(), "2026-05-29-003")

    with zipfile.ZipFile(zip_path) as zf:
        for name in zf.namelist():
            if name.endswith(".json"):
                content = zf.read(name).decode()
                parsed = json.loads(content)
                canonical = json.dumps(parsed, sort_keys=True, separators=(",", ":"), ensure_ascii=False)
                assert content == canonical, f"{name} is not canonical JSON"
