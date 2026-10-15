"""The protection verdict, row by row and limit by limit (services/agent_posture.py)."""

from datetime import UTC, datetime, timedelta

import pytest

from ransomguard_grid.services.agent_posture import (
    PARTIAL,
    PROTECTED,
    UNKNOWN,
    UNPROTECTED,
    ModuleStatus,
    PostureSnapshot,
    ProtectionVerdict,
    compute_protection_state,
    is_restart,
    last_upload_age_seconds,
)

NOW = datetime(2026, 10, 11, 12, 0, tzinfo=UTC)
ACTIVE = ModuleStatus("active")


def _modules(**overrides: ModuleStatus) -> dict[str, ModuleStatus]:
    modules = {
        "SENTINEL": ACTIVE, "ENTROPY": ACTIVE, "USB_GUARD": ACTIVE, "EXFIL_WATCH": ACTIVE,
        # Displayed, never in the verdict.
        "GENEALOGY": ModuleStatus("degraded", "attribution_unreliable"),
        "ANTI_TAMPERING": ModuleStatus("inactive", "not_implemented"),
    }
    modules.update(overrides)
    return modules


def _posture(
    modules: dict[str, ModuleStatus] | None = None,
    *,
    uptime: int = 3600,
    received_ago: timedelta = timedelta(seconds=30),
    pending: int | None = 0,
    last_ok_ago: timedelta | None = timedelta(seconds=40),
    last_ok_age: int | None = None,
    rejected: int | None = None,
    audit_failures: int | None = None,
    schema_version: int = 2,
) -> PostureSnapshot:
    return PostureSnapshot(
        schema_version=schema_version,
        received_at=NOW - received_ago,
        agent_uptime_seconds=uptime,
        modules=_modules() if modules is None else modules,
        pending_alerts=pending,
        last_upload_ok_at=None if last_ok_ago is None else NOW - last_ok_ago,
        last_upload_ok_age_seconds=last_ok_age,
        rejected_alerts=rejected,
        audit_write_failures=audit_failures,
    )


def _verdict(posture: PostureSnapshot | None, signal_ago: timedelta | None = timedelta(seconds=30)) -> ProtectionVerdict:
    last = None if signal_ago is None else NOW - signal_ago
    return compute_protection_state(last, posture, NOW)


# ── Row 1: protected ──

def test_fresh_signal_four_detectors_active_healthy_transmission_is_protected() -> None:
    verdict = _verdict(_posture())
    assert (verdict.state, verdict.reason_codes, verdict.signal_age_seconds) == (PROTECTED, [], 30)


def test_genealogy_degraded_and_anti_tampering_inactive_do_not_lower_the_verdict() -> None:
    assert _verdict(_posture()).state == PROTECTED


def test_a_queue_draining_burst_is_still_protected() -> None:
    # Alerts pending, but the last upload succeeded 1 min ago: a burst being drained.
    assert _verdict(_posture(pending=40, last_ok_ago=timedelta(minutes=1))).state == PROTECTED


# ── Row 2: partial, a detector other than SENTINEL ──

@pytest.mark.parametrize(
    ("module", "status", "reason"),
    [
        ("EXFIL_WATCH", ModuleStatus("inactive", "admin_rights_missing"), "EXFIL_WATCH:inactive:admin_rights_missing"),
        ("ENTROPY", ModuleStatus("inactive", "stopped_unexpectedly"), "ENTROPY:inactive:stopped_unexpectedly"),
        ("ENTROPY", ModuleStatus("inactive", "no_watch_directory"), "ENTROPY:inactive:no_watch_directory"),
        ("USB_GUARD", ModuleStatus("degraded", "actions_not_implemented"), "USB_GUARD:degraded:actions_not_implemented"),
        # Another detector turned off in configuration is the truth: partially protected.
        ("EXFIL_WATCH", ModuleStatus("disabled_by_config", "disabled_by_config"), "EXFIL_WATCH:disabled_by_config"),
    ],
)
def test_a_detector_other_than_sentinel_not_active_is_partial(module: str, status: ModuleStatus, reason: str) -> None:
    verdict = _verdict(_posture(_modules(**{module: status})))
    assert verdict.state == PARTIAL
    assert verdict.reason_codes == [reason]


def test_sentinel_degraded_integrity_check_failing_is_partial_not_red() -> None:
    verdict = _verdict(_posture(_modules(SENTINEL=ModuleStatus("degraded", "integrity_check_failing"))))
    assert (verdict.state, verdict.reason_codes) == (PARTIAL, ["SENTINEL:degraded:integrity_check_failing"])


# ── Row 2: partial, transmission ──

def test_queue_not_drained_for_more_than_5_min_is_partial() -> None:
    verdict = _verdict(_posture(pending=3, last_ok_ago=timedelta(minutes=5, seconds=1)))
    assert (verdict.state, verdict.reason_codes) == (PARTIAL, ["transmission_blocked"])


def test_last_upload_exactly_5_min_ago_is_not_yet_blocked() -> None:
    assert _verdict(_posture(pending=3, last_ok_ago=timedelta(minutes=5))).state == PROTECTED


def test_null_last_upload_with_pending_alerts_counts_as_blocked_once_up_5_min() -> None:
    verdict = _verdict(_posture(pending=3, last_ok_ago=None, uptime=301, received_ago=timedelta(0)))
    assert (verdict.state, verdict.reason_codes) == (PARTIAL, ["transmission_blocked"])


def test_null_last_upload_shortly_after_a_restart_is_not_blocked() -> None:
    # The timestamp lives in the agent's memory: lost on restart, with the queue still on disk.
    assert _verdict(_posture(pending=3, last_ok_ago=None, uptime=120, received_ago=timedelta(0))).state == PROTECTED


def test_more_than_100_pending_is_partial_even_with_a_recent_upload() -> None:
    verdict = _verdict(_posture(pending=101, last_ok_ago=timedelta(seconds=10)))
    assert (verdict.state, verdict.reason_codes) == (PARTIAL, ["transmission_blocked"])


# Decision 4: green is a promise, only given with proof.

def test_pending_unmeasured_during_the_first_299_s_has_no_effect() -> None:
    assert _verdict(_posture(pending=None, last_ok_ago=None, uptime=299, received_ago=timedelta(0))).state == PROTECTED


def test_pending_unmeasured_at_300_s_is_partial_transmission_unknown() -> None:
    verdict = _verdict(_posture(pending=None, last_ok_ago=None, uptime=300, received_ago=timedelta(0)))
    assert (verdict.state, verdict.reason_codes) == (PARTIAL, ["transmission_unknown"])


def test_pending_measured_follows_the_normal_rule() -> None:
    assert _verdict(_posture(pending=0, uptime=300, received_ago=timedelta(0))).state == PROTECTED
    assert _verdict(_posture(pending=3, last_ok_ago=timedelta(minutes=6), uptime=300)).reason_codes == [
        "transmission_blocked"
    ]


# Decision 5: the agent's own measure of the upload age wins over its timestamp.

def test_the_agent_measured_age_wins_over_a_fresh_looking_timestamp() -> None:
    # The timestamp says 1 min (agent clock behind), the agent measured 10 min: blocked.
    verdict = _verdict(_posture(pending=3, last_ok_ago=timedelta(minutes=1), last_ok_age=600))
    assert verdict.reason_codes == ["transmission_blocked"]


def test_the_agent_measured_age_wins_over_a_stale_looking_timestamp() -> None:
    # The timestamp says 10 min (agent clock ahead... or behind), the agent measured 20 s.
    assert _verdict(_posture(pending=3, last_ok_ago=timedelta(minutes=10), last_ok_age=20)).state == PROTECTED


def test_the_measured_age_is_projected_to_now() -> None:
    # 200 s when sent, sent 2 min ago: 320 s now, beyond 5 min.
    posture = _posture(pending=3, last_ok_age=200, received_ago=timedelta(minutes=2))
    assert last_upload_age_seconds(posture, NOW) == 320
    assert _verdict(posture, timedelta(minutes=2)).reason_codes == ["transmission_blocked"]


def test_a_future_timestamp_never_gives_a_negative_age() -> None:
    posture = _posture(pending=3, last_ok_ago=-timedelta(minutes=10))  # agent clock 10 min ahead
    assert last_upload_age_seconds(posture, NOW) == 0
    assert _verdict(posture).state == PROTECTED


def test_rejected_alerts_is_partial() -> None:
    verdict = _verdict(_posture(rejected=2))
    assert (verdict.state, verdict.reason_codes) == (PARTIAL, ["alerts_rejected"])


@pytest.mark.parametrize("rejected", [0, None])
def test_zero_or_unreported_rejected_alerts_is_protected(rejected: int | None) -> None:
    assert _verdict(_posture(rejected=rejected)).state == PROTECTED


def test_audit_entries_not_recorded_is_partial() -> None:
    # AGT-AUD-002: the agent kept detecting, but its signed trail has holes.
    verdict = _verdict(_posture(audit_failures=1))
    assert (verdict.state, verdict.reason_codes) == (PARTIAL, ["audit_write_failed"])


@pytest.mark.parametrize("failures", [0, None])
def test_zero_or_unreported_audit_failures_is_protected(failures: int | None) -> None:
    assert _verdict(_posture(audit_failures=failures)).state == PROTECTED


# ── Row 3: partial, signal lost 5-60 min ──

def test_signal_lost_just_over_5_min_is_partial() -> None:
    verdict = _verdict(_posture(received_ago=timedelta(minutes=5, seconds=1)), timedelta(minutes=5, seconds=1))
    assert (verdict.state, verdict.reason_codes, verdict.signal_age_seconds) == (PARTIAL, ["signal_lost"], 301)


def test_signal_exactly_5_min_old_is_still_fresh() -> None:
    assert _verdict(_posture(received_ago=timedelta(minutes=5)), timedelta(minutes=5)).state == PROTECTED


def test_signal_exactly_60_min_old_is_still_partial() -> None:
    verdict = _verdict(_posture(received_ago=timedelta(minutes=60)), timedelta(minutes=60))
    assert (verdict.state, verdict.reason_codes) == (PARTIAL, ["signal_lost"])


# ── Rows 4-6: unprotected ──

@pytest.mark.parametrize(
    ("status", "reason"),
    [
        (ModuleStatus("inactive", "init_failed"), "SENTINEL:inactive:init_failed"),
        (ModuleStatus("inactive", "stopped_unexpectedly"), "SENTINEL:inactive:stopped_unexpectedly"),
        (ModuleStatus("inactive", "no_watch_directory"), "SENTINEL:inactive:no_watch_directory"),
        # disabled_by_config included: SENTINEL is the first signal of the convergence rule.
        (ModuleStatus("disabled_by_config", "disabled_by_config"), "SENTINEL:disabled_by_config"),
    ],
)
def test_sentinel_not_active_is_unprotected_on_its_own(status: ModuleStatus, reason: str) -> None:
    verdict = _verdict(_posture(_modules(SENTINEL=status)))
    assert (verdict.state, verdict.reason_codes) == (UNPROTECTED, [reason])


def test_sentinel_missing_from_a_v2_heartbeat_is_unprotected() -> None:
    modules = _modules()
    del modules["SENTINEL"]
    verdict = _verdict(_posture(modules))
    assert (verdict.state, verdict.reason_codes) == (UNPROTECTED, ["SENTINEL:inactive:not_reported"])


def test_no_detector_active_is_unprotected() -> None:
    down = ModuleStatus("inactive", "stopped_unexpectedly")
    verdict = _verdict(_posture(_modules(SENTINEL=down, ENTROPY=down, USB_GUARD=down, EXFIL_WATCH=down)))
    assert verdict.state == UNPROTECTED
    assert "no_detector_active" in verdict.reason_codes


def test_no_signal_for_just_over_60_min_is_unprotected() -> None:
    verdict = _verdict(_posture(received_ago=timedelta(minutes=60, seconds=1)), timedelta(minutes=60, seconds=1))
    assert (verdict.state, verdict.reason_codes, verdict.signal_age_seconds) == (UNPROTECTED, ["signal_lost"], 3601)


# ── Rows 7-8: unknown ──

def test_never_connected_is_unknown() -> None:
    verdict = _verdict(None, signal_ago=None)
    assert (verdict.state, verdict.reason_codes, verdict.signal_age_seconds) == (UNKNOWN, ["never_connected"], None)


def test_v1_agent_with_a_fresh_signal_is_unknown_update_required() -> None:
    verdict = _verdict(_posture(schema_version=1, modules={}))
    assert (verdict.state, verdict.reason_codes) == (UNKNOWN, ["agent_update_required"])


def test_fresh_heartbeat_without_any_posture_is_unknown_update_required() -> None:
    assert _verdict(None).reason_codes == ["agent_update_required"]


def test_v1_agent_silent_for_more_than_60_min_is_still_red() -> None:
    verdict = _verdict(_posture(schema_version=1, modules={}), timedelta(hours=2))
    assert (verdict.state, verdict.reason_codes) == (UNPROTECTED, ["signal_lost"])


# ── starting (CORRECTIF2_REVUE.md, "pour le correctif 3") ──

STARTING = ModuleStatus("inactive", "starting")


def test_detectors_starting_under_300_s_is_unknown_starting() -> None:
    verdict = _verdict(_posture(_modules(SENTINEL=STARTING, ENTROPY=STARTING), uptime=60, received_ago=timedelta(0)))
    assert verdict.state == UNKNOWN
    assert verdict.reason_codes == ["SENTINEL:starting", "ENTROPY:starting", "starting"]


def test_sentinel_starting_at_299_s_is_still_starting() -> None:
    assert _verdict(_posture(_modules(SENTINEL=STARTING), uptime=299, received_ago=timedelta(0))).state == UNKNOWN


def test_sentinel_still_starting_at_300_s_is_a_start_failure_red() -> None:
    verdict = _verdict(_posture(_modules(SENTINEL=STARTING), uptime=300, received_ago=timedelta(0)))
    assert (verdict.state, verdict.reason_codes) == (UNPROTECTED, ["SENTINEL:inactive:init_failed"])


def test_another_detector_still_starting_after_300_s_is_partial() -> None:
    verdict = _verdict(_posture(_modules(ENTROPY=STARTING), uptime=400, received_ago=timedelta(0)))
    assert (verdict.state, verdict.reason_codes) == (PARTIAL, ["ENTROPY:inactive:init_failed"])


def test_uptime_is_projected_to_now_not_frozen_at_the_last_heartbeat() -> None:
    # Reported 250 s of uptime 2 min ago: the agent has been up 370 s now.
    verdict = _verdict(
        _posture(_modules(ENTROPY=STARTING), uptime=250, received_ago=timedelta(minutes=2)), timedelta(minutes=2)
    )
    assert verdict.reason_codes == ["ENTROPY:inactive:init_failed"]


def test_starting_never_hides_a_detector_that_is_really_down() -> None:
    verdict = _verdict(_posture(
        _modules(ENTROPY=STARTING, EXFIL_WATCH=ModuleStatus("inactive", "admin_rights_missing")),
        uptime=60, received_ago=timedelta(0),
    ))
    assert verdict.state == PARTIAL
    assert verdict.reason_codes[0] == "EXFIL_WATCH:inactive:admin_rights_missing"


# ── Combinations: the worst wins, every reason is reported ──

def test_signal_lost_never_makes_a_red_posture_look_better() -> None:
    verdict = _verdict(
        _posture(_modules(SENTINEL=ModuleStatus("inactive", "stopped_unexpectedly")), received_ago=timedelta(minutes=10)),
        timedelta(minutes=10),
    )
    assert verdict.state == UNPROTECTED
    assert verdict.reason_codes == ["SENTINEL:inactive:stopped_unexpectedly", "signal_lost"]


def test_every_contributing_reason_is_reported_worst_first() -> None:
    verdict = _verdict(_posture(
        _modules(SENTINEL=ModuleStatus("inactive", "init_failed"), EXFIL_WATCH=ModuleStatus("inactive", "admin_rights_missing")),
        rejected=1,
    ))
    assert verdict.state == UNPROTECTED
    assert verdict.reason_codes == [
        "SENTINEL:inactive:init_failed", "EXFIL_WATCH:inactive:admin_rights_missing", "alerts_rejected",
    ]


def test_age_is_measured_on_the_server_clock_even_for_a_naive_timestamp() -> None:
    naive = (NOW - timedelta(minutes=10)).replace(tzinfo=None)  # SQLite round-trip
    assert compute_protection_state(naive, _posture(), NOW).reason_codes == ["signal_lost"]


# ── Restart loop (R7 review): the start-up grace never lasts forever ──

def _sentinel_starting_at_100_s() -> PostureSnapshot:
    return _posture(_modules(SENTINEL=STARTING), uptime=100, received_ago=timedelta(0))


def test_three_restarts_in_the_hour_with_sentinel_starting_is_red_with_restart_loop() -> None:
    verdict = compute_protection_state(NOW - timedelta(seconds=5), _sentinel_starting_at_100_s(), NOW, restarts_last_hour=3)
    assert verdict.state == UNPROTECTED
    assert verdict.reason_codes == ["SENTINEL:inactive:init_failed", "restart_loop"]


def test_two_restarts_in_the_hour_keep_the_start_up_grace() -> None:
    verdict = compute_protection_state(NOW - timedelta(seconds=5), _sentinel_starting_at_100_s(), NOW, restarts_last_hour=2)
    assert (verdict.state, verdict.reason_codes) == (UNKNOWN, ["SENTINEL:starting", "starting"])


def test_a_restart_loop_alone_is_partial() -> None:
    verdict = compute_protection_state(NOW - timedelta(seconds=5), _posture(), NOW, restarts_last_hour=4)
    assert (verdict.state, verdict.reason_codes) == (PARTIAL, ["restart_loop"])


@pytest.mark.parametrize(
    ("previous", "received_ago", "uptime", "restart"),
    [
        (1000, timedelta(0), 950, False),  # jitter: 50 s back
        (1000, timedelta(0), 940, False),  # exactly the 60 s margin
        (1000, timedelta(0), 939, True),
        (1000, timedelta(minutes=2), 1060, False),  # projected 1120, 60 s back
        (1000, timedelta(minutes=2), 100, True),
        (3600, timedelta(hours=3), 30, True),  # down for hours, then back
    ],
)
def test_restart_detection(previous: int, received_ago: timedelta, uptime: int, restart: bool) -> None:
    assert is_restart(previous, NOW - received_ago, uptime, NOW) is restart
