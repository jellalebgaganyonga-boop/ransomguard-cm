# System check - 2026-10-26

Run at 2026-10-26 08:39:15 on KELYFA-PC by `scripts/system-check.ps1`.

A line says PASS only if it was checked and worked. NOT BUILT and NOT TESTABLE HERE are
the honest state of the system, not failures, and do not change the exit code.

| | Count |
|---|---|
| PASS | 27 |
| FAIL | 1 |
| NOT BUILT | 5 |
| NOT TESTABLE HERE | 4 |

## Platform

| Status | Check | Detail |
|---|---|---|
| PASS | Container grid-nginx | Up 37 hours |
| PASS | Container grid-api | Up 9 hours (healthy) |
| PASS | Container grid-mysql | Up 37 hours (healthy) |
| PASS | Container grid-redis | Up 37 hours (healthy) |
| PASS | Container grid-notifier | Up 37 hours (healthy) |

## The platform answers

| Status | Check | Detail |
|---|---|---|
| PASS | GRID answers over validated TLS | version 0.8.0 |
| PASS | Console answers on localhost:5175 |  |

## The bench accounts, isolation and roles

| Status | Check | Detail |
|---|---|---|
| PASS | Sign-in: admin@yde-hospital.cm (tenant_admin) | tenant yde-hospital |
| PASS | Sign-in: analyst@yde-hospital.cm (security_analyst) | tenant yde-hospital |
| PASS | Sign-in: auditor@yde-hospital.cm (read_only_auditor) | tenant yde-hospital |
| PASS | Sign-in: admin@test-clinic.cm (tenant_admin) | tenant test-clinic |
| PASS | One customer sees nothing of another | test-clinic: 0 workstations, 0 alerts; the hospital: 1 workstation(s) |
| PASS | The auditor can read but not act | reads alerts (200), refused creating a user (403) |

## The workstations and what each module reports

| Status | Check | Detail |
|---|---|---|
| PASS | Workstations as the GRID reports them | 1 enrolled (decommissioned ones hidden) |
| PASS |   PC-LAB: active, protection partial | reasons: EXFIL_WATCH:degraded:learning_phase |
| PASS |     PC-LAB / SENTINEL: active |  |
| PASS |     PC-LAB / ENTROPY: active |  |
| PASS |     PC-LAB / GENEALOGY: active |  |
| PASS |     PC-LAB / USB_GUARD: active |  |
| NOT TESTABLE HERE |     PC-LAB / EXFIL_WATCH: degraded (learning_phase) | it learns the normal traffic before it can judge any |
| NOT BUILT |     PC-LAB / ANTI_TAMPERING: inactive (not_implemented) | the watchdog has not been written |
| PASS |   PC-LAB: no alert was refused |  |

## The installer the console hands out

| Status | Check | Detail |
|---|---|---|
| PASS | An enrollment code is issued | valid 15 minutes, single use |
| PASS | The console can hand out an installer | RansomGuard-CM-Installer-192.168.56.1.exe, 94.3 MB, valid PE header, sha256 F43B824A584C... |

## Not built, and not testable on this bench

| Status | Check | Detail |
|---|---|---|
| NOT BUILT | Anti-tampering (watchdog) | the services are registered but never started; the agent reports it as not implemented |
| NOT BUILT | A client certificate per agent | agents authenticate with an enrollment token, not a per-agent certificate |
| NOT BUILT | A signed installer | Windows shows 'Publisher: Unknown' on the UAC prompt |
| NOT BUILT | IRONCLAD against real hardware | the code exists and is tested against a simulated Arduino only |
| NOT TESTABLE HERE | USB GUARD against a real key | needs a USB device passed through to the VM; the module itself reports active |
| NOT TESTABLE HERE | EXFIL WATCH raising an alert | it learns the normal traffic first and reports learning_phase until it has a baseline |
| NOT TESTABLE HERE | The deployed server | OPS-001: the Tailscale server is not touched from here |

## Suites

| Status | Check | Detail |
|---|---|---|
| PASS | GRID suite (pytest) | 429 passed, 1 skipped in 766.85s (0:12:46) |
| PASS | Console suite (vitest) | Tests  212 passed (212) |
| PASS | No French reaches a user | no French reaches a user |
| PASS | Agent suite (CI, cannot run here) | CI run 40283727509 green on 8cd80bb |
| PASS | Console end-to-end (Playwright) | 81 passed (4.1m) |

## Live detection

| Status | Check | Detail |
|---|---|---|
| FAIL | PC-LAB: the decoy raised an alert | no new SentinelCanaryModified arrived - **fix:** did the trigger run, and is the agent active? see the agent log on the workstation |


