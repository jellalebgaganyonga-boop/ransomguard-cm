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
| Double-click installer (ADR-027) | **works** | system-check §5: the console hands out a signed-payload installer (94.3 MB on 2026-10-26) with a valid PE header. Installed on PC-LAB on 2026-10-26 from HEAD `0f0db14`, output entirely in English, 1/6 to 6/6, ending "Workstation enrolled". The UAC prompt takes up to 30 s to appear while the single file unpacks |
| Enrollment | **works** | Bench test 1. The code lasts 15 minutes and is single-use |
| Fresh install raises no alert | **works** | Bench test 1: 0 alerts, `rejected_alerts = 0`, `pending_alerts = 0`. This took three attempts to get right — see the bug list in BENCH_TEST_RESULTS.md |
| Re-enrolling a rebuilt workstation | **works** | A decommissioned agent no longer holds its machine's fingerprint; PC-LAB was reinstalled several times on 2026-10-25 |
| Installer signing | **not built** | Windows shows "Publisher: Unknown" on the UAC prompt. system-check §8 |
| A client certificate per agent (mTLS) | **not built** | Agents authenticate with an enrollment token. system-check §8. The GRID's own TLS *is* validated by every client, including this check |

## Detection and response

| Feature | Status | Proof |
|---|---|---|
| SENTINEL (decoy files) | **works, with a known defect** | Bench test 3: writing to a decoy raises a Critical alert within seconds. **One write raises the alert twice**, a second apart, and only the first carries attribution and the process tree — see BENCH_TEST_RESULTS, "What is still not right" |
| Attribution — who wrote the file | **works** | Bench test 3: `attribution: resolved`, powershell PID 7744, with its image path and start time |
| GENEALOGY — what started it | **works** | `explorer -> cmd -> powershell` on the alert page, PID 7744 matching the trigger output. Screenshot: `docs/screenshots/real/round4-english/alert_genealogy_section.png`. 8 GRID tests, 8 console tests |
| Suspension (ADR-026) | **works** | Bench test 3: `SentinelProcessSuspended`, the writer frozen |
| Resume from the console | **works** | Bench test 4: `SentinelProcessResumed` reaches the GRID, the PID leaves *Suspended processes*, and the process really continues on the workstation |
| ENTROPY | **works** | Reports `active` on PC-LAB with a named writer and rule 3 evaluated (AGT-EN-002 closed) |
| USB GUARD | **partial** | The module reports `active`, but nothing here exercised it: a real USB key must be passed into the VM. system-check §8 |
| EXFIL WATCH | **partial** | Reports `degraded / learning_phase` honestly. It cannot raise an alert until it has learned the normal traffic, so no detection has been observed |
| IRONCLAD (offline backup) | **partial** | The code exists and is tested against a *simulated* Arduino. Never connected to real hardware. system-check §8 |
| Anti-tampering | **not built** | The services are registered but never started; the agent reports `inactive / not_implemented`. system-check §8 |

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
   and nothing notices. The console reports this honestly rather than hiding it.
2. **The installer is unsigned.** Every install shows "Publisher: Unknown", which is exactly what
   a user is told to be suspicious of.
3. **EXFIL WATCH has never raised an alert here.** It is honest about learning, but that means
   the module is unproven on this bench.
4. **USB GUARD is unproven.** It reports active; no USB key has been passed through to test it.
5. **IRONCLAD has never seen real hardware.**
6. **One write on a decoy raises two alerts**, and the second carries neither the writer nor the
   process tree. A reader who opens the wrong one of the two concludes that attribution and
   GENEALOGY failed when they did not.
7. **`ip_address` is not recorded in the user-actions log**, so "who did it" is answered by
   account but not by origin.
8. **A workstation's name is recorded once, at enrollment.** Rename the machine and the console
   keeps the old name until it enrolls again — the heartbeat does not carry the hostname. This
   caught the bench itself: PC-RECEPTION's VirtualBox entry had been renamed to English while
   Windows still called the machine `PC-ACCUEIL`, so it would have enrolled under the French name. <!-- french-ok: the old machine name, recorded here only because it was renamed away -->
   
   Renamed and restarted on 2026-10-26.
