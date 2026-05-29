"""Builds Ed25519-signed ZIP packages for threat intel distribution to agents."""

import hashlib
import json
import zipfile
from pathlib import Path

from ransomguard_grid.services.grid_signing_key_service import GridSigningKeyService
from ransomguard_grid.services.threat_intel_aggregator import AggregatedThreatIntel


class ThreatIntelPackageBuilder:
    """Builds ZIP packages compatible with agent's ThreatIntelUpdateValidator."""

    STATIC_LOLBAS = [
        "certutil.exe", "bitsadmin.exe", "wmic.exe", "regsvr32.exe", "mshta.exe",
        "rundll32.exe", "msbuild.exe", "installutil.exe", "regasm.exe", "regsvcs.exe",
        "csc.exe", "msxsl.exe", "xwizard.exe", "esentutl.exe", "extexport.exe",
        "extrac32.exe", "findstr.exe", "hh.exe", "ieexec.exe", "imewdbld.exe",
        "makecab.exe", "msconfig.exe", "msdt.exe", "odbcconf.exe", "pcalua.exe",
        "presentationhost.exe", "print.exe", "psr.exe", "scriptrunner.exe",
        "syncappvpublishingserver.exe",
    ]

    def __init__(self, signing_service: GridSigningKeyService, output_dir: Path) -> None:
        self.signing_service = signing_service
        self.output_dir = output_dir

    def build(self, aggregated: AggregatedThreatIntel, version_string: str) -> Path:
        """Build a signed ZIP package from aggregated threat intel."""
        tor_ips = sorted({
            e.indicator for e in aggregated.entries
            if "tor_exit_node" in e.tags and e.indicator_type == "ipv4"
        })
        c2_ips = sorted({
            e.indicator for e in aggregated.entries
            if any(t in e.tags for t in ["botnet_cc", "c2", "malware"])
            and e.indicator_type in ("ipv4", "ipv6")
        })
        cloud_cidrs = self._group_cloud_cidrs(aggregated)

        generated_at = aggregated.generated_at.isoformat()

        files_content: dict[str, object] = {
            "manifest.json": {
                "package_version": version_string,
                "generated_at": generated_at,
                "files": {
                    "tor-exit-nodes.json": {"entry_count": len(tor_ips)},
                    "known-c2-servers.json": {"entry_count": len(c2_ips)},
                    "cloud-providers.json": {"provider_count": len(cloud_cidrs)},
                    "lolbas-binaries.json": {"entry_count": len(self.STATIC_LOLBAS)},
                },
                "signing_algorithm": "Ed25519",
                "source_status": aggregated.source_status,
            },
            "tor-exit-nodes.json": {
                "metadata": {"version": version_string, "generated_at": generated_at, "entry_count": len(tor_ips)},
                "entries": tor_ips,
            },
            "known-c2-servers.json": {
                "metadata": {"version": version_string, "generated_at": generated_at, "entry_count": len(c2_ips)},
                "entries": c2_ips,
            },
            "cloud-providers.json": {
                "metadata": {"version": version_string, "generated_at": generated_at},
                "cidrs": cloud_cidrs,
            },
            "lolbas-binaries.json": {
                "metadata": {"version": "static-1.0", "binaries": len(self.STATIC_LOLBAS)},
                "binaries": self.STATIC_LOLBAS,
            },
        }

        # Canonical JSON bytes per file
        canonical: dict[str, bytes] = {}
        for name, content in files_content.items():
            canonical[name] = json.dumps(content, sort_keys=True, separators=(",", ":"), ensure_ascii=False).encode()

        # Build signature payload: sorted filenames with content
        sig_payload = b""
        for name in sorted(canonical):
            sig_payload += name.encode() + b":" + canonical[name] + b"\n"

        signature = self.signing_service.sign(sig_payload)

        # Write ZIP
        self.output_dir.mkdir(parents=True, exist_ok=True)
        zip_path = self.output_dir / f"threat-intel-{version_string}.zip"
        with zipfile.ZipFile(zip_path, "w", compression=zipfile.ZIP_DEFLATED) as zf:
            for name in sorted(canonical):
                zf.writestr(name, canonical[name])
            zf.writestr("signature.bin", signature)

        return zip_path

    @staticmethod
    def compute_sha256(path: Path) -> str:
        """Compute SHA-256 hash of a file."""
        h = hashlib.sha256()
        with open(path, "rb") as f:
            for chunk in iter(lambda: f.read(8192), b""):
                h.update(chunk)
        return h.hexdigest()

    @staticmethod
    def _group_cloud_cidrs(aggregated: AggregatedThreatIntel) -> dict[str, list[str]]:
        """Group cloud CIDRs by provider name."""
        groups: dict[str, set[str]] = {}
        for e in aggregated.entries:
            if e.indicator_type in ("cidr", "ipv6_cidr"):
                provider = e.source.split("+")[0].replace("_ip_ranges", "")
                groups.setdefault(provider, set()).add(e.indicator)
        return {k: sorted(v) for k, v in groups.items()}
