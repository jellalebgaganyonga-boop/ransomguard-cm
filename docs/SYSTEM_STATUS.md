# RansomGuard-CM — what works, what is partial, what is not built

**As of 2026-10-26**, branch `sprint8/recovery`.

Every row says how it was checked. Nothing here is a plan or an intention: a row says **works**
only if something was run and observed. Where a thing cannot be exercised on this bench, the row
says so and gives the reason rather than claiming a pass.

How to check it yourself, in one command:

```powershell
pwsh scripts/system-check.ps1
```

It runs to the end, prints PASS / FAIL / NOT BUILT / NOT TESTABLE HERE for each line, writes
`docs/testing/system-check-<date>.md`, and exits non-zero only if something failed.

## The platform

| Feature | Status | Proof |
|---|---|---|
| GRID server (compose: nginx, API, MySQL, Redis, notifier) | **works** | system-check §1 and §2: five containers healthy, `/api/v1/health` answers over TLS validated against the demo CA |
| Console (React) | **works** | system-check §2; 212 unit tests, 81 end-to-end tests |
| Multi-tenant isolation | **works** | system-check "One customer sees nothing of another": `test-clinic` sees 0 workstations and 0 alerts while the hospital has one. Bench test 6 |
| Roles (admin / analyst / auditor) | **works** | system-check "The auditor can read but not act". Bench test 5: every write is 403 for the auditor; analyst refused users, resume and decommission |
| Audit log and user actions | **partial** | Bench test 7: `login`, `user_create`, `agent_decommission`, `user_disable` are all recorded with `actor_email` and a timestamp. **`ip_address` is null on every row** |
| Console in English only | **works** | `check-no-french.py` over the built bundle, the GRID, the installer, the agent, the bench scripts and the owner-facing documents, in CI. `e2e/english-only.spec.ts` in a French-locale browser. Bench test 9. The check's word list was dead until 2026-10-26 — it compiled to a backspace byte instead of `\b` — and repairing it found four French error messages in the GRID, now English. It self-tests before every run |

## Installing and enrolling a workstation

| Feature | Status | Proof |
|---|---|---|
| Double-click installer (ADR-027) | **works** | system-check §5: the console hands out a signed-payload installer (94.3 MB on 2026-10-26) with a valid PE header. Installed on PC-LAB on 2026-10-26 from HEAD `8cd80bb`, output entirely in English, 1/6 to 6/6, ending "Workstation enrolled", first attempt. The UAC prompt takes up to 30 s to appear while the single file unpacks |
| The service starts inside Windows' limit | **partial** | A warm start is **13.0 s**, measured on PC-RECEPTION. The **first** start after an install still took longer than 120 s — the schema is created then, Entity Framework is JIT-ed, and the scanner reads 94 MB of new files — so `sc start` returns 1053 and the installer's retry is what carries the installation through to "Workstation enrolled". `EnsureDatabase()` still runs the migration before `host.Run()`, which is the next thing to move. Fixed on 2026-10-26. `sc start` had returned **1053**, "the service did not respond in a timely fashion", leaving the agent installed but unenrolled: the host was opening the ETW session, planting the decoys, walking the user profiles, waiting on the backup device and enrolling with the GRID *before* it told Windows the service was running. All of that now happens after. The installer also waits 120 s rather than 60, judges by the service's own state rather than sc.exe's exit code, and starts it once more before giving up. `AgentStartupTests` |
| Enrollment | **works** | Bench test 1. The code lasts 15 minutes and is single-use |
| Fresh install raises no alert | **works** | Bench test 1: 0 alerts, `rejected_alerts = 0`, `pending_alerts = 0`. This took three attempts to get right — see the bug list in BENCH_TEST_RESULTS.md |
| Re-enrolling a rebuilt workstation | **works** | A decommissioned agent no longer holds its machine's fingerprint; PC-LAB was reinstalled several times on 2026-10-25 |
| Installer signing | **not built** | Windows shows "Publisher: Unknown" on the UAC prompt. system-check §8 |
| A client certificate per agent (mTLS) | **not built** | Agents authenticate with an enrollment token. system-check §8. The GRID's own TLS *is* validated by every client, including this check |

## Detection and response

| Feature | Status | Proof |
|---|---|---|
| SENTINEL (decoy files) | **works** | Bench test 3: writing to a decoy raises a Critical alert within seconds. One write raises **one** alert: the duplicate found on 2026-10-26 was fixed the same day and confirmed on the bench — two writes, one alert each, both with the writer and the chain |
| Attribution — who wrote the file | **works** | Bench test 3: `attribution: resolved`, powershell PID 7744, with its image path and start time |
| GENEALOGY — what started it | **works** | `explorer -> cmd -> powershell` on the alert page, PID 7744 matching the trigger output. Screenshot: `docs/screenshots/real/round4-english/alert_genealogy_section.png`. 8 GRID tests, 8 console tests |
| Suspension (ADR-026) | **works** | Bench test 3: `SentinelProcessSuspended`, the writer frozen |
| Resume from the console | **works** | Bench test 4: `SentinelProcessResumed` reaches the GRID, the PID leaves *Suspended processes*, and the process really continues on the workstation |
| ENTROPY | **works** | Reports `active` on PC-LAB with a named writer and rule 3 evaluated (AGT-EN-002 closed) |
| USB GUARD | **partial** | The module reports `active`, but nothing here exercised it: a real USB key must be passed into the VM. system-check §8 |
| EXFIL WATCH | **partial** | Reports `degraded / learning_phase` honestly. It cannot raise an alert until it has learned the normal traffic, so no detection has been observed |
| IRONCLAD (offline backup) | **partial** | The code exists and is tested against a *simulated* Arduino. Never connected to real hardware. system-check §8 |
| Anti-tampering | **not built — planned** | The services are registered but never started. As of 2026-10-26 the agent no longer reports it as a module and the console no longer shows the row: a line on every workstation reading "inactive — not implemented" is true and nothing anybody can act on. The GRID still *accepts* it, so an agent installed before that keeps its heartbeat. It is listed by system-check under what is not built, and it is the first thing in "what is weakest" below |

## The agent stays running

| Feature | Status | Proof |
|---|---|---|
| One service failing does not stop the agent | **works** | `AgentKeepsRunningTests`, `GridCommandServiceTests`. Found on 2026-10-26: PC-LAB went quiet fourteen minutes into a 65-minute idle proof and never came back, on a machine that was awake and had not slept. `sc query` said STOPPED with exit code 0 — a clean exit. The Event Log said why: *"BackgroundService failed … the configured HttpClient.Timeout of 10 seconds elapsing"* and *"BackgroundServiceExceptionBehavior is configured to StopHost … the IHost instance is stopping"*. The GRID had taken longer than ten seconds to answer a poll for commands; `GridCommandService` guarded its catches with `ex is not OperationCanceledException`, and an HttpClient timeout **is** one, so it escaped; .NET's default then stopped the host and every detector in it. Both halves are fixed: the filter now asks whether the agent's own token was cancelled, and the host is configured to keep running. The failure is still reported — the host logs the service that threw, and each module publishes its own failed state |

## Workstation offline and back

| Feature | Status | Proof |
|---|---|---|
| Disconnection is noticed | **works** | Bench test 8: `signal_lost`, then `disconnected` about six minutes after shutdown — the GRID waits five minutes on purpose so a brief hiccup is not an alarm |
| Reconnection | **works** | Bench test 8: `active` again about two minutes after restart |

## The deployed server

| Feature | Status | Proof |
|---|---|---|
| The Tailscale server | **not checked** | OPS-001: it is not touched from here. Everything above is the local compose GRID on `192.168.56.1` |

## Tests and gates

| | Count | Where |
|---|---|---|
| Agent | 1039 | CI, "Agent full". It cannot run on this machine: endpoint protection holds a read handle on the freshly built test DLL |
| GRID (pytest) | 429 + 1 skipped | locally and in CI |
| Console (vitest) | 212 | locally and in CI |
| Console end-to-end (Playwright) | 81 | locally and in CI |
| `check-no-french.py` | 369 files | CI, blocking |
| mypy on `grid/src` | 0 errors | CI, blocking since 2026-10-26 |

## What is weakest, in order

1. **Anti-tampering does not exist.** An attacker with administrator rights can stop the agent,
   and nothing notices. Removing its permanently grey row from the console on 2026-10-26 did not
   change that and was not meant to: the gap is here, in the list of what is weakest, and in what
   system-check prints as not built. A module table is for what a workstation is doing; a thing
   that has not been written belongs in a plan.
2. **The installer is unsigned.** Every install shows "Publisher: Unknown", which is exactly what
   a user is told to be suspicious of.
3. **EXFIL WATCH has never raised an alert here.** It is honest about learning, but that means
   the module is unproven on this bench.
4. **USB GUARD is unproven.** It reports active; no USB key has been passed through to test it.
5. **IRONCLAD has never seen real hardware.**
6. **A bench VM's clock free-runs.** There are no Guest Additions on PC-LAB or PC-RECEPTION, so
   nothing keeps their clocks with the host's after the VM starts. PC-LAB was **five hours and
   sixteen minutes behind** on 2026-10-27. The time an alert was detected is the workstation's
   own and travels with it, so a drifted workstation makes every alert look hours old — on the
   console and in front of an audience. `deployment/bench/set-clock.ps1` sets it; nothing checks
   it on its own.
7. **The first start after an install exceeds Windows' 30 s limit**, so `sc start` returns 1053
   and the installer's retry is what completes the installation. A warm start is 13 s.
8. **`ip_address` is not recorded in the user-actions log**, so "who did it" is answered by
   account but not by origin.
9. **A workstation's name is recorded once, at enrollment.** Rename the machine and the console
   keeps the old name until it enrolls again — the heartbeat does not carry the hostname. This
   caught the bench itself: PC-RECEPTION's VirtualBox entry had been renamed to English while
   Windows still called the machine `PC-ACCUEIL`, so it would have enrolled under the French name. <!-- french-ok: the old machine name, recorded here only because it was renamed away -->
   
   Renamed and restarted on 2026-10-26.
