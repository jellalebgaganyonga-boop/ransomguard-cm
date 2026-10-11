"""Agent posture: the closed vocabulary and the global protection verdict.

The verdict answers the first question of the agent page -- "is this endpoint protected, right
now?" -- and lives here only: the agent list, the agent page, the Sprint 9 "endpoint not
protected" notification and the reports all read it from the API, none recomputes it. It is
computed *at read time*, never stored: the age of the signal changes without any heartbeat
arriving, so a stored verdict would be wrong a minute later. Ages are measured on the server
clock (``agents.last_heartbeat_at`` is the server's reception time).

The table (LOT2_DECISIONS_ET_CORRECTIFS.md, decision 1, with the CORRECTIF2_REVUE.md rules):

==============  ======  ================================================================
state           colour  condition
==============  ======  ================================================================
protected       green   signal <= 5 min, the 4 detectors active, transmission healthy
partial         orange  signal <= 5 min and (a detector other than SENTINEL not active,
                        SENTINEL degraded, transmission blocked or unknown, or alerts
                        rejected)
partial         orange  signal lost for more than 5 min and at most 60 min
unprotected     red     SENTINEL not active, whatever the reason (disabled_by_config too)
unprotected     red     no detector active
unprotected     red     no signal for more than 60 min
unknown         grey    never connected
unknown         grey    v1 agent: no posture reported
unknown         grey    a detector starting, agent up for less than 300 s
==============  ======  ================================================================

Several rows can hold at once; the worst wins and every contributing reason is reported.
"""

from collections.abc import Mapping
from dataclasses import dataclass, field
from datetime import UTC, datetime, timedelta

# ── Closed vocabulary (the agent's ModuleCode / ModuleState / ModuleReasonCode) ──

MODULE_CODES: frozenset[str] = frozenset(
    {"SENTINEL", "ENTROPY", "GENEALOGY", "USB_GUARD", "EXFIL_WATCH", "ANTI_TAMPERING"}
)
MODULE_STATES: frozenset[str] = frozenset({"active", "inactive", "disabled_by_config", "degraded"})

# Reason codes the console translates (FR/EN). An unlisted code is stored and shown as is.
REASON_CODES: frozenset[str] = frozenset({
    "admin_rights_missing",
    "disabled_by_config",
    "init_failed",
    "stopped_unexpectedly",
    "not_implemented",
    "attribution_unreliable",
    "actions_not_implemented",
    "starting",
    "no_watch_directory",
    "integrity_check_failing",
    "not_reported",  # server side: a v2 heartbeat without this detector
})

# Bound of last_upload_ok_age_seconds on the wire: ten years, far beyond any real age.
MAX_UPLOAD_AGE_SECONDS = 10 * 365 * 24 * 3600

# "Is this endpoint protected?" is a question about detection: the verdict reads these four.
# GENEALOGY is an enricher (degraded by design until ETW attribution), ANTI_TAMPERING is not
# implemented yet; both are displayed, neither enters the verdict (debt: re-enter
# ANTI_TAMPERING once the Watchdog exists).
DETECTION_MODULES: tuple[str, ...] = ("SENTINEL", "ENTROPY", "USB_GUARD", "EXFIL_WATCH")

# ── Thresholds: the one definition (the console keeps colours only) ──

SIGNAL_FRESH_FOR = timedelta(minutes=5)
SIGNAL_RED_AFTER = timedelta(minutes=60)
STARTING_GRACE_SECONDS = 300
UPLOAD_STALE_AFTER = timedelta(minutes=5)
PENDING_ALERTS_GUARD = 100

# ── Verdicts and reason codes ──

PROTECTED = "protected"
PARTIAL = "partial"
UNPROTECTED = "unprotected"
UNKNOWN = "unknown"

# unknown sits below partial: "starting" must not hide a detector that is really down.
_RANK = {PROTECTED: 0, UNKNOWN: 1, PARTIAL: 2, UNPROTECTED: 3}

NEVER_CONNECTED = "never_connected"
AGENT_UPDATE_REQUIRED = "agent_update_required"
SIGNAL_LOST = "signal_lost"
STARTING = "starting"
NO_DETECTOR_ACTIVE = "no_detector_active"
TRANSMISSION_BLOCKED = "transmission_blocked"
TRANSMISSION_UNKNOWN = "transmission_unknown"
ALERTS_REJECTED = "alerts_rejected"


@dataclass(frozen=True)
class ModuleStatus:
    state: str
    reason_code: str | None = None


@dataclass(frozen=True)
class PostureSnapshot:
    """The last posture an agent reported, as stored."""

    schema_version: int
    received_at: datetime  # server clock
    agent_uptime_seconds: int
    modules: Mapping[str, ModuleStatus] = field(default_factory=dict)
    pending_alerts: int | None = None
    last_upload_ok_at: datetime | None = None  # agent clock
    # Measured by the agent on its own clock at send time: preferred over last_upload_ok_at,
    # which only compares with the server clock through the agent/server skew.
    last_upload_ok_age_seconds: int | None = None
    rejected_alerts: int | None = None


@dataclass(frozen=True)
class ProtectionVerdict:
    state: str
    # Worst first. Module reasons read "<MODULE>:<state>[:<reason_code>]", e.g.
    # "EXFIL_WATCH:inactive:admin_rights_missing", so the console can compose the full sentence.
    reason_codes: list[str]
    signal_age_seconds: int | None


def _as_utc(value: datetime) -> datetime:
    """A naive datetime (SQLite round-trips lose tzinfo) is UTC."""
    return value if value.tzinfo is not None else value.replace(tzinfo=UTC)


def compute_protection_state(
    last_heartbeat_at: datetime | None,
    posture: PostureSnapshot | None,
    now: datetime,
) -> ProtectionVerdict:
    """Decide whether an endpoint is protected right now. Pure: no I/O, no clock."""
    if last_heartbeat_at is None:
        return ProtectionVerdict(UNKNOWN, [NEVER_CONNECTED], None)

    now = _as_utc(now)
    age = now - _as_utc(last_heartbeat_at)
    age_seconds = max(0, int(age.total_seconds()))
    findings: list[tuple[str, str]] = []

    # Silence: an agent killed by ransomware is precisely an agent that stops talking.
    if age > SIGNAL_RED_AFTER:
        findings.append((UNPROTECTED, SIGNAL_LOST))
    elif age > SIGNAL_FRESH_FOR:
        findings.append((PARTIAL, SIGNAL_LOST))

    if posture is None or posture.schema_version < 2:
        if not findings:
            findings.append((UNKNOWN, AGENT_UPDATE_REQUIRED))
        return _verdict(findings, age_seconds)

    # The last known posture still counts when the signal is lost: losing contact never makes
    # an endpoint look better than what it last reported.
    received_at = _as_utc(posture.received_at)
    uptime_now = posture.agent_uptime_seconds + max(0, int((now - received_at).total_seconds()))
    findings.extend(_detector_findings(posture.modules, uptime_now))
    findings.extend(_transmission_findings(posture, uptime_now, now))

    return _verdict(findings, age_seconds)


def _detector_findings(modules: Mapping[str, ModuleStatus], uptime_now: int) -> list[tuple[str, str]]:
    findings: list[tuple[str, str]] = []
    active = starting = 0
    for code in DETECTION_MODULES:
        status = modules.get(code, ModuleStatus("inactive", "not_reported"))
        state, reason = status.state, status.reason_code

        if state == "active":
            active += 1
            continue

        if state == "inactive" and reason == STARTING:
            # "Starting" is grey strictly below 300 s; at 300 s exactly it is no longer start-up.
            if uptime_now < STARTING_GRACE_SECONDS:
                starting += 1
                findings.append((UNKNOWN, f"{code}:{STARTING}"))
                continue
            # Still starting after the grace period: a start-up failure, never "in progress" forever.
            reason = "init_failed"

        label = f"{code}:{state}" + (f":{reason}" if reason and reason != state else "")
        if code == "SENTINEL":
            # SENTINEL is the first signal of the convergence rule: without it the endpoint is
            # not protected -- except a failing periodic integrity check (degraded), which
            # leaves its real-time watchers running.
            findings.append((PARTIAL if state == "degraded" else UNPROTECTED, label))
        else:
            findings.append((PARTIAL, label))

    if active == 0 and starting == 0:
        findings.append((UNPROTECTED, NO_DETECTOR_ACTIVE))
    if starting:
        findings.append((UNKNOWN, STARTING))
    return findings


def last_upload_age_seconds(posture: PostureSnapshot, now: datetime) -> int | None:
    """Age of the last successful upload, now. Never negative.

    The agent's own measure (age at send time) is preferred, projected to now with the server
    clock. Without it, the agent timestamp is compared with the server clock; a timestamp in
    the future (skew) counts as "now", never as a negative age.
    """
    received_at = _as_utc(posture.received_at)
    if posture.last_upload_ok_age_seconds is not None:
        return posture.last_upload_ok_age_seconds + max(0, int((now - received_at).total_seconds()))
    if posture.last_upload_ok_at is not None:
        return max(0, int((now - _as_utc(posture.last_upload_ok_at)).total_seconds()))
    return None


def _transmission_findings(posture: PostureSnapshot, uptime_now: int, now: datetime) -> list[tuple[str, str]]:
    findings: list[tuple[str, str]] = []

    # Green is a promise, only given with proof. An unmeasured queue during the first 300 s is
    # start-up (the measure does not exist yet); beyond, for a v2 agent, it is "unknown".
    pending = posture.pending_alerts
    if pending is None:
        if uptime_now >= STARTING_GRACE_SECONDS:
            findings.append((PARTIAL, TRANSMISSION_UNKNOWN))

    # A queue that no longer drains, not a burst being drained. The last-upload timestamp lives
    # in the agent's memory and is lost on restart: none only counts once the agent has run 5 min.
    elif pending > 0:
        age = last_upload_age_seconds(posture, now)
        # With no successful upload known, the queue has been stuck as long as the agent has run.
        stuck_for = uptime_now if age is None else age
        if stuck_for > UPLOAD_STALE_AFTER.total_seconds() or pending > PENDING_ALERTS_GUARD:
            findings.append((PARTIAL, TRANSMISSION_BLOCKED))

    # Alerts the GRID refused (400/422) are never resent: "did everything arrive?" would be a
    # false yes. Null means "not reported", never 0.
    if posture.rejected_alerts is not None and posture.rejected_alerts > 0:
        findings.append((PARTIAL, ALERTS_REJECTED))

    return findings


def _verdict(findings: list[tuple[str, str]], age_seconds: int) -> ProtectionVerdict:
    if not findings:
        return ProtectionVerdict(PROTECTED, [], age_seconds)
    ordered = sorted(findings, key=lambda f: -_RANK[f[0]])  # stable: equal ranks keep their order
    return ProtectionVerdict(ordered[0][0], [reason for _, reason in ordered], age_seconds)
