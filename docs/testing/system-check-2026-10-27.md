# System check - 2026-10-27

Run at 2026-10-27 07:11:03 on KELYFA-PC by `scripts/system-check.ps1`.

A line says PASS only if it was checked and worked. NOT BUILT and NOT TESTABLE HERE are
the honest state of the system, not failures, and do not change the exit code.

| | Count |
|---|---|
| PASS | 28 |
| FAIL | 0 |
| NOT BUILT | 4 |
| NOT TESTABLE HERE | 6 |

## Platform

| Status | Check | Detail |
|---|---|---|
| PASS | Container grid-nginx | Up 10 hours |
| PASS | Container grid-api | Up 10 hours (healthy) |
| PASS | Container grid-mysql | Up 10 hours (healthy) |
| PASS | Container grid-redis | Up 10 hours (healthy) |
| PASS | Container grid-notifier | Up About an hour (healthy) |

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
| PASS | One customer sees nothing of another | test-clinic: 0 workstations, 0 alerts; the hospital: 2 workstation(s) |
| PASS | The auditor can read but not act | reads alerts (200), refused creating a user (403) |

## The workstations and what each module reports

| Status | Check | Detail |
|---|---|---|
| PASS | Workstations as the GRID reports them | 2 enrolled (decommissioned ones hidden) |
| PASS |   PC-RECEPTION: active, protection partial | reasons: EXFIL_WATCH:degraded:learning_phase, audit_write_failed |
| PASS |     PC-RECEPTION / SENTINEL: active |  |
| PASS |     PC-RECEPTION / ENTROPY: active |  |
| PASS |     PC-RECEPTION / GENEALOGY: active |  |
| PASS |     PC-RECEPTION / USB_GUARD: active |  |
| NOT TESTABLE HERE |     PC-RECEPTION / EXFIL_WATCH: degraded (learning_phase) | it learns the normal traffic before it can judge any |
| PASS |   PC-RECEPTION: no alert was refused |  |
| PASS |   PC-LAB: active, protection partial | reasons: EXFIL_WATCH:degraded:learning_phase |
| PASS |     PC-LAB / SENTINEL: active |  |
| PASS |     PC-LAB / ENTROPY: active |  |
| PASS |     PC-LAB / GENEALOGY: active |  |
| PASS |     PC-LAB / USB_GUARD: active |  |
| NOT TESTABLE HERE |     PC-LAB / EXFIL_WATCH: degraded (learning_phase) | it learns the normal traffic before it can judge any |
| PASS |   PC-LAB: no alert was refused |  |

## The installer the console hands out

| Status | Check | Detail |
|---|---|---|
| PASS | An enrollment code is issued | valid 15 minutes, single use |
| PASS | The console can hand out an installer | RansomGuard-CM-Installer-192.168.56.1.exe, 94.3 MB, valid PE header, sha256 F43B824A584C... |

## Not built, and not testable on this bench

| Status | Check | Detail |
|---|---|---|
| NOT BUILT | Anti-tampering (watchdog) | the services are registered but never started. The agent stopped reporting it as a module on 2026-10-26: a permanently grey row saying a thing does not exist is noise in a console. It is listed here, and as planned work in SYSTEM_STATUS |
| NOT BUILT | A client certificate per agent | agents authenticate with an enrollment token, not a per-agent certificate |
| NOT BUILT | A signed installer | Windows shows 'Publisher: Unknown' on the UAC prompt |
| NOT BUILT | IRONCLAD against real hardware | the code exists and is tested against a simulated Arduino only |
| NOT TESTABLE HERE | USB GUARD against a real key | needs a USB device passed through to the VM; the module itself reports active |
| NOT TESTABLE HERE | EXFIL WATCH raising an alert | it learns the normal traffic first and reports learning_phase until it has a baseline |
| NOT TESTABLE HERE | The deployed server | OPS-001: the Tailscale server is not touched from here |

## Suites

| Status | Check | Detail |
|---|---|---|
| NOT TESTABLE HERE | Automated suites | skipped with -SkipSuites |


