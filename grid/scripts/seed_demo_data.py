"""Seed SYNTHETIC demo data into a local GRID database, for console screenshots.

    ============================================================================
    THE ROWS THIS SCRIPT WRITES ARE INVENTED. THEY ARE NOT DETECTIONS.
    ============================================================================

No agent produced any of this. Every hostname, PID, entropy figure, byte count
and IP address below was written by hand to give the console something to draw,
so the interface can be photographed and reviewed. Nothing here is evidence
that the product detected anything.

So that the invention is never mistaken for a measurement, every row carries its
provenance in the database itself: each alert has an ``alert_details`` row with
key ``data_provenance`` and value ``synthetic_demo_seed``, and each agent's
hostname is a demo name. To find or remove every seeded row:

    SELECT * FROM alert_details WHERE key = 'data_provenance';
    python scripts/seed_demo_data.py --purge

Use it for: screenshots of the interface, reviewing layout and translations,
exercising the console against populated lists.

Do NOT use it for: anything presented as a detection result, a benchmark, a
measurement, or evidence that a module works. The evidence that the modules
work is the test suites (CI) and a real agent run on a bench, neither of which
this script substitutes for.

────────────────────────────────────────────────────────────────────────────────
Where this departs from a naive demo fixture, and why

Three things were asked for that this build cannot honestly show. The script
follows the product instead of overriding it:

1.  No agent reads "protected" (green). The verdict in
    ``services/agent_posture.py`` requires all four detection modules active,
    and since B4 (commit 882599c) ENTROPY honestly reports
    ``degraded / attribution_unreliable``: it cannot name the process that
    wrote a file, so it does not claim to be fully working. A real agent on
    this build therefore reads "partial", and so do these three. Reporting
    ENTROPY as active would have produced a green console by deleting the
    limitation B4 exists to disclose.

2.  There is no IRONCLAD alert. IRONCLAD/backup is PROD-001, not implemented:
    it is not in ``MODULE_CODES``, it raises no alert type in
    ``shared/contracts/agent-alert-types.json``, and ``Severity`` has no
    ``info`` level. A backup-completed row would have been a picture of a
    feature that does not exist, so it is not seeded.

3.  The Tor-exit-node rule cannot fire. ``TorExitNodeRule`` is one of four
    EXFIL rule classes left unregistered because the threat-intelligence lists
    they read are not fed. The 450 MB outbound burst is therefore seeded as
    ``ExfilWatchVolumeAnomaly``, a registered rule that really does detect
    exactly that, with the destination kept in the details.

Every alert_type, module code, module state, reason code, severity and MITRE id
used below is a real value taken from the contract and the enums, never invented.
────────────────────────────────────────────────────────────────────────────────

Usage (the GRID's own venv, with GRID_DATABASE_URL pointing at the local db):

    cd grid
    PYTHONPATH=src .venv/Scripts/python.exe scripts/seed_demo_data.py
    PYTHONPATH=src .venv/Scripts/python.exe scripts/seed_demo_data.py --purge

It refuses to touch a database whose tenant is not the demo tenant, and it
refuses to run when GRID_ENVIRONMENT is not development.
"""

from __future__ import annotations

import argparse
import asyncio
import hashlib
import os
import sys
import uuid
from datetime import UTC, datetime, timedelta

from sqlalchemy import delete, select
from sqlalchemy.ext.asyncio import async_sessionmaker, create_async_engine

from ransomguard_grid.db.models.agent import Agent, AgentModuleStatus, AgentPosture
from ransomguard_grid.db.models.alerts import Alert, AlertDetail
from ransomguard_grid.db.models.enums import AgentStatus, AlertStatus, Severity
from ransomguard_grid.db.models.tenant_user import Tenant

# The marker that makes every seeded row identifiable as invented, in the database.
PROVENANCE_KEY = "data_provenance"
PROVENANCE_VALUE = "synthetic_demo_seed"

DEMO_TENANT_CODE = "hopital-yde"

NOW = datetime.now(UTC)


def _fingerprint(hostname: str) -> str:
    """A deterministic stand-in for a hardware fingerprint, derived from the demo name.

    Not a real machine measurement, and reproducible so re-seeding is idempotent.
    """
    return hashlib.sha256(f"synthetic-demo::{hostname}".encode()).hexdigest()


def _agent_id(hostname: str) -> str:
    """Stable UUID per demo hostname, so re-running replaces rather than duplicates."""
    return str(uuid.uuid5(uuid.NAMESPACE_DNS, f"ransomguard-demo-seed.{hostname}"))


# ── The three demo endpoints ────────────────────────────────────────────────
#
# modules: the states a real agent on this build reports. ENTROPY degraded is
# B4's honest posture, GENEALOGY is an enricher degraded by design until ETW
# attribution lands, ANTI_TAMPERING is not implemented (no Watchdog yet).

_BASE_MODULES = {
    "SENTINEL": ("active", None),
    "ENTROPY": ("degraded", "attribution_unreliable"),
    "USB_GUARD": ("active", None),
    "EXFIL_WATCH": ("active", None),
    "GENEALOGY": ("degraded", "attribution_unreliable"),
    "ANTI_TAMPERING": ("inactive", "not_implemented"),
}

AGENTS = [
    {
        "hostname": "PC-URGENCES-01",
        "os_version": "Windows 11 Pro 23H2",
        "agent_version": "8.0.0-sprint8",
        "heartbeat_ago": timedelta(seconds=30),
        "upload_age_seconds": 30,
        "modules": dict(_BASE_MODULES),
    },
    {
        "hostname": "PC-LABO-02",
        "os_version": "Windows 10 Enterprise 22H2",
        "agent_version": "8.0.0-sprint8",
        "heartbeat_ago": timedelta(seconds=45),
        "upload_age_seconds": 45,
        "modules": dict(_BASE_MODULES),
    },
    {
        "hostname": "PC-ADMIN-03",
        "os_version": "Windows 11 Pro 23H2",
        "agent_version": "8.0.0-sprint8",
        "heartbeat_ago": timedelta(minutes=2),
        "upload_age_seconds": 120,
        # EXFIL WATCH still learning its baselines: VolumeAnomaly is mute, the
        # other rules fire (EXFIL-REG-001). One more real reason than the others.
        "modules": {**_BASE_MODULES, "EXFIL_WATCH": ("degraded", "learning_phase")},
    },
]

ENROLLED_AGO = timedelta(days=3)


# ── The six demo alerts ─────────────────────────────────────────────────────
#
# alert_type values come from shared/contracts/agent-alert-types.json; MITRE ids
# from the agent's AlertMapper. details are alert_details key/value rows; the
# attribution_* and process_* keys are the ones _attribution_for() reads (ADR-025).

ALERTS = [
    {
        "cmid": "demo-seed-001",
        "hostname": "PC-URGENCES-01",
        "alert_type": "SentinelCanaryModified",
        "mitre": "T1486",
        "severity": Severity.Critical,
        "status": AlertStatus.Investigating,
        "detected_ago": timedelta(hours=2),
        # The summary states only what the alert itself carries. No response alert is
        # seeded (ADR-026 responses are sent by a running agent), so claiming a
        # suspension here would contradict the Response section of the alert page.
        "summary": "Canary file modified: dossier_patient_042.docx, written by suspicious.exe.",
        "details": {
            "canary_path": r"C:\SentinelCanaries\dossier_patient_042.docx",
            "attribution_state": "resolved",
            "process_id": "4521",
            "process_name": "suspicious.exe",
            "process_path": r"C:\Users\Public\Downloads\suspicious.exe",
            "process_created_at": (NOW - timedelta(hours=2, minutes=4)).isoformat(),
            "suggested_response": "suspend_process",
        },
    },
    {
        "cmid": "demo-seed-002",
        "hostname": "PC-LABO-02",
        "alert_type": "EntropyAbsoluteHighEntropy",
        "mitre": "T1486",
        "severity": Severity.High,
        "status": AlertStatus.Investigating,
        "detected_ago": timedelta(hours=5),
        "summary": "High Shannon entropy 7.92 bits/byte on rapport_labo_2026-10.xlsx, written by cryptolocker.exe.",
        "details": {
            "file_path": r"D:\Analyses\rapport_labo_2026-10.xlsx",
            "shannon_entropy_bits_per_byte": "7.92",
            "entropy_threshold": "7.50",
            # ENTROPY is degraded and names no writer (B4): the process below came from
            # SENTINEL's ETW attribution on the same file, not from ENTROPY.
            "attribution_state": "resolved",
            "attribution_reason": "attributed_by_sentinel_etw",
            "process_id": "7823",
            "process_name": "cryptolocker.exe",
            "process_path": r"C:\ProgramData\cryptolocker.exe",
            "process_created_at": (NOW - timedelta(hours=5, minutes=11)).isoformat(),
            "suggested_response": "suspend_process",
        },
    },
    {
        "cmid": "demo-seed-003",
        "hostname": "PC-ADMIN-03",
        "alert_type": "ExfilWatchVolumeAnomaly",
        "mitre": "T1041",
        "severity": Severity.Medium,
        "status": AlertStatus.New,
        "detected_ago": timedelta(days=1),
        "summary": "Outbound volume anomaly: 450 MB in 15 min to 185.220.101.42. Writer not identified.",
        "details": {
            "bytes_out": "471859200",
            "window_minutes": "15",
            "destination_ip": "185.220.101.42",
            "destination_port": "443",
            # Ambiguous: several processes held the socket, none can be named with
            # confidence. The console shows "ambiguous", not a guess.
            "attribution_state": "ambiguous",
            "attribution_reason": "multiple_candidate_processes",
            "candidate_process_names": "svchost.exe, backup-agent.exe",
            "response": "alert_raised",
        },
    },
    {
        "cmid": "demo-seed-004",
        "hostname": "PC-URGENCES-01",
        "alert_type": "UsbGuard",
        "mitre": "T1091",
        "severity": Severity.Low,
        "status": AlertStatus.Resolved,
        "detected_ago": timedelta(days=1, hours=3),
        "summary": "USB mass storage connected: SanDisk Ultra 32GB. Allowed by policy, logged.",
        "details": {
            "device_description": "SanDisk Ultra 32GB",
            "usb_vid": "0781",
            "usb_pid": "5567",
            "policy_decision": "allowed",
            "response": "logged",
        },
    },
    {
        "cmid": "demo-seed-005",
        "hostname": "PC-LABO-02",
        "alert_type": "UsbGuard",
        "mitre": "T1091",
        "severity": Severity.High,
        "status": AlertStatus.Resolved,
        "detected_ago": timedelta(days=2),
        "summary": "Unknown USB device blocked: Generic Flash (VID 1234, PID 5678). Access denied.",
        "details": {
            "device_description": "Generic Flash",
            "usb_vid": "1234",
            "usb_pid": "5678",
            "policy_decision": "blocked",
            "response": "device_access_denied",
        },
    },
    {
        "cmid": "demo-seed-006",
        "hostname": "PC-ADMIN-03",
        "alert_type": "SentinelCanaryModified",
        "mitre": "T1486",
        "severity": Severity.Critical,
        "status": AlertStatus.New,
        "detected_ago": timedelta(days=2, hours=4),
        "summary": "6 canary files modified in 12 s by ransom_note_gen.exe. Escalated for a human decision.",
        "details": {
            "canary_path": r"C:\SentinelCanaries\bilan_sanguin_118.docx",
            "canaries_modified_count": "6",
            "elapsed_seconds": "12",
            "attribution_state": "resolved",
            "process_id": "9012",
            "process_name": "ransom_note_gen.exe",
            "process_path": r"C:\Users\admin\AppData\Local\Temp\ransom_note_gen.exe",
            "process_created_at": (NOW - timedelta(days=2, hours=4, minutes=1)).isoformat(),
            "suggested_response": "suspend_process",
            "escalated": "true",
        },
    },
]


async def _resolve_tenant(session) -> Tenant:
    tenant = (
        await session.execute(select(Tenant).where(Tenant.code == DEMO_TENANT_CODE))
    ).scalar_one_or_none()
    if tenant is None:
        raise SystemExit(
            f"No tenant with code {DEMO_TENANT_CODE!r} in this database. "
            "This script only seeds the local demo tenant; run seed_initial_data.py first."
        )
    return tenant


async def purge(session, tenant_id: str) -> None:
    """Remove every row this script wrote, identified by the provenance marker."""
    seeded_alert_ids = [
        row[0]
        for row in (
            await session.execute(
                select(AlertDetail.alert_id).where(
                    AlertDetail.tenant_id == tenant_id,
                    AlertDetail.key == PROVENANCE_KEY,
                    AlertDetail.value == PROVENANCE_VALUE,
                )
            )
        ).all()
    ]
    if seeded_alert_ids:
        await session.execute(delete(AlertDetail).where(AlertDetail.alert_id.in_(seeded_alert_ids)))
        await session.execute(delete(Alert).where(Alert.id.in_(seeded_alert_ids)))

    agent_ids = [_agent_id(a["hostname"]) for a in AGENTS]
    await session.execute(delete(AgentModuleStatus).where(AgentModuleStatus.agent_id.in_(agent_ids)))
    await session.execute(delete(AgentPosture).where(AgentPosture.agent_id.in_(agent_ids)))
    await session.execute(delete(Alert).where(Alert.agent_id.in_(agent_ids)))
    await session.execute(delete(Agent).where(Agent.id.in_(agent_ids)))
    print(f"purged {len(seeded_alert_ids)} seeded alerts and {len(agent_ids)} demo agents")


async def seed(session, tenant_id: str) -> None:
    by_hostname: dict[str, str] = {}

    for spec in AGENTS:
        hostname = spec["hostname"]
        agent_id = _agent_id(hostname)
        by_hostname[hostname] = agent_id
        heartbeat = NOW - spec["heartbeat_ago"]
        enrolled = NOW - ENROLLED_AGO

        session.add(
            Agent(
                id=agent_id,
                tenant_id=tenant_id,
                hostname=hostname,
                fqdn=f"{hostname.lower()}.{DEMO_TENANT_CODE}.local",
                os_version=spec["os_version"],
                agent_version=spec["agent_version"],
                hardware_fingerprint=_fingerprint(hostname),
                status=AgentStatus.active,
                last_heartbeat_at=heartbeat,
                enrolled_at=enrolled,
                created_at=enrolled,
            )
        )

        # pending_alerts / rejected_alerts / audit_write_failures must be explicit 0,
        # not None: None means "the agent could not measure it" and the verdict then
        # reports transmission_unknown. Zero is a measurement, null is an absence.
        session.add(
            AgentPosture(
                agent_id=agent_id,
                tenant_id=tenant_id,
                schema_version=2,
                agent_uptime_seconds=int(ENROLLED_AGO.total_seconds()),
                threat_intel_version="2026.10.20-1",
                last_detection_at=heartbeat - timedelta(hours=2),
                system_free_bytes=84_298_104_832,
                db_volume_free_bytes=12_884_901_888,
                pending_alerts=0,
                last_upload_ok_at=heartbeat,
                last_upload_ok_age_seconds=spec["upload_age_seconds"],
                last_upload_error_code=None,
                rejected_alerts=0,
                audit_write_failures=0,
                received_at=heartbeat,
            )
        )

        for code, (state, reason) in spec["modules"].items():
            session.add(
                AgentModuleStatus(
                    id=str(uuid.uuid4()),
                    tenant_id=tenant_id,
                    agent_id=agent_id,
                    module_code=code,
                    state=state,
                    reason_code=reason,
                    changed_at=enrolled,
                    updated_at=heartbeat,
                )
            )

    for spec in ALERTS:
        alert_id = str(uuid.uuid4())
        detected = NOW - spec["detected_ago"]
        session.add(
            Alert(
                id=alert_id,
                tenant_id=tenant_id,
                agent_id=by_hostname[spec["hostname"]],
                client_message_id=spec["cmid"],
                alert_type=spec["alert_type"],
                mitre_technique_id=spec["mitre"],
                severity=spec["severity"],
                status=spec["status"],
                detected_at=detected,
                ingested_at=detected + timedelta(seconds=3),
                summary=spec["summary"],
                raw_payload_json=None,
            )
        )
        details = dict(spec["details"])
        details[PROVENANCE_KEY] = PROVENANCE_VALUE
        for key, value in details.items():
            session.add(
                AlertDetail(
                    id=str(uuid.uuid4()),
                    tenant_id=tenant_id,
                    alert_id=alert_id,
                    key=key,
                    value=value,
                )
            )

    print(f"seeded {len(AGENTS)} demo agents and {len(ALERTS)} demo alerts")


async def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--purge", action="store_true", help="remove seeded rows and exit")
    args = parser.parse_args()

    environment = os.environ.get("GRID_ENVIRONMENT", "")
    if environment != "development":
        raise SystemExit(
            f"GRID_ENVIRONMENT is {environment!r}; this script only runs with 'development'. "
            "It writes invented data and must never touch a real deployment."
        )

    url = os.environ.get("GRID_DATABASE_URL")
    if not url:
        raise SystemExit("GRID_DATABASE_URL is not set.")

    engine = create_async_engine(url, echo=False)
    maker = async_sessionmaker(engine, expire_on_commit=False)
    async with maker() as session:
        tenant = await _resolve_tenant(session)
        await purge(session, tenant.id)
        if not args.purge:
            await seed(session, tenant.id)
        await session.commit()
    await engine.dispose()
    return 0


if __name__ == "__main__":
    sys.exit(asyncio.run(main()))
