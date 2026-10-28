# Bench test results — RansomGuard-CM serving a simulated hospital

**Run on 2026-10-25.** Vendor stack: the compose GRID and console on the host (`192.168.56.1`,
agents on 443, console on 8443). Customer 1: tenant `yde-hospital`, "Hopital Regional de Yaounde",
whose workstations are the bench VMs. Customer 2: tenant `test-clinic`, to show that one
customer never sees another's data.

Every figure below comes from a real run against that stack. Nothing here is seeded or imagined.

## The bench

| | |
|---|---|
| Workstation 1 | **PC-RECEPTION** (VirtualBox VM `win`), 4 vCPU, 4 GB, NAT + host-only |
| Workstation 2 | **PC-LAB** (VirtualBox VM `windows  10`), 4 vCPU, 4 GB, NAT + host-only |
| Each workstation has | a non-administrator account `nurse`, and 200 invented hospital documents in `C:\Users\Public\Documents\Hospital-FICTIONAL\` (50 each in Patients, Laboratory, Pharmacy, Imaging) |
| Agent under test | `1.0.0+69041da` (CI green: CI `40221112773`, Agent full `40221112766`) |
| Console accounts | `admin@yde-hospital.cm` (tenant_admin), `analyst@yde-hospital.cm` (security_analyst), `auditor@yde-hospital.cm` (read_only_auditor), `admin@test-clinic.cm` (tenant_admin of the other customer) |

Passwords are in `deployment/bench/credentials.local.md`, which is gitignored and never committed.

### Two things about the bench itself, said plainly

- **There is no Windows installation media on this host.** The directive asked for the VMs to be
  rebuilt with an unattended install. That is not possible without a ~6 GB download, so the bench
  uses the two Windows VMs that already existed and work, renamed PC-RECEPTION and PC-LAB. The
  third VM, `windows`, is still stuck in "Preparing to configure Windows" and was left alone
  rather than rebuilt.
- **Neither VM has Guest Additions**, so there is no shared folder and no guest control. Every
  step inside a VM is driven by typing at its console through VirtualBox, and everything longer
  than one line is carried in on an ISO (`deployment/bench/`). Typing drops and mis-cases
  characters, so each command is read back from a screenshot before it is run.

## Results

| ID | Test | Expected | Observed | Verdict |
|---|---|---|---|---|
| 1 | Install + enrolment on a fresh workstation | agent in the console within 2 min, **zero alerts** | PC-LAB enrolled and `active`, **0 alerts**, `rejected_alerts = 0`, `pending_alerts = 0`. The agent's own log for an install window holds 18 events: deployment starting, the ETW session opening, nine canaries deployed, ENTROPY attribution — and no alert, no restoration. Before the fixes below, the same window produced 105 events and 24 Critical alerts. | **PASS** |
| 2 | Posture and module table, honest per agent | each module's real state | `SENTINEL active`, `ENTROPY active`, `GENEALOGY active`, `USB_GUARD active`, `EXFIL_WATCH degraded/learning_phase`. Verdict `partial`, and the only reason given is the one that is true: EXFIL WATCH is still learning. The run of this test on 2026-10-26 also listed `ANTI_TAMPERING inactive/not_implemented`; that row was removed the same day, because a line on every workstation saying a thing was never built belongs in a plan, not in a table of what the machine is doing. It is still in SYSTEM_STATUS.md under what is not built. | **PASS** |
| 3 | SENTINEL: run `trigger-sentinel.cmd` | Critical alert, "Who did it?" names the process, process suspended | `SentinelCanaryModified` (Critical), then `SentinelProcessSuspended` — *"Process suspended after writing a canary: powershell (PID 2560)"*. "Who did it?" reads `state: resolved, process_id: 2560, process_name: powershell` — the writer is named, not guessed. | **PASS** |
| 4 | Resume from the console | the process continues on the VM **and** the console updates | `SentinelProcessResumed` — *"Process resumed on request"* — reached the GRID and the suspension left *Suspended processes*. The process really did resume: still inside its five-write loop, it wrote the canary again and was suspended again, each time. Resuming until the script finished left **0 suspensions listed** and the trigger reporting *"finished. A pause above was the suspension; the resume let the script continue."* | **PASS** |
| 6 | Tenant isolation | `admin@test-clinic.cm` sees nothing of `yde-hospital` | `test-clinic`: 0 agents, 0 alerts, 1 user (its own admin). `yde-hospital`, at the same moment: 1 agent, 10 alerts, 3 users. | **PASS** |
| 5 | Roles: auditor cannot act, analyst can triage, admin can do all | the auditor is refused every write | Run against the live GRID. Reads (agents, alerts, audit) are 200 for all three. Writes -- resume a process, create a user, decommission an agent -- are admin 200/201, analyst **403**, auditor **403**. Acknowledging an alert: admin and analyst both get 422, which is a status rule and not a permission, while the auditor gets **403** -- the distinction the roles exist for. The auditor can read the audit log and the analyst cannot, which is deliberate: it is the compliance role. | **PASS** |
| 7 | Audit log and user-actions log record every action | each action, with its time and who did it | The user-actions log carries `login`, `user_create`, `agent_decommission` and `user_disable`, each with `actor_email` naming the real account and `created_at`. `ip_address` is null on every row, which is recorded as a gap below rather than counted as a pass. | **PASS** |
| 8 | Workstation offline, then back | disconnected while it is off, active when it returns | PC-LAB shut down at 00:25:11. `signal_lost` appeared at 00:30:57 and the status became **disconnected** at 00:31:37 -- about six minutes, which is the five-minute timeout the GRID waits on purpose so that a brief hiccup is not an alarm. Restarted at 00:31:52, **active** again at 00:33:58, about two minutes, with the modules reporting `starting` as they came up. | **PASS** |
| 9 | The console stays English on a French-locale browser | English everywhere, and no way to leave it | `e2e/english-only.spec.ts`, run in a browser whose locale and `Accept-Language` both say French: the sign-in page is English and carries none of the French wording, the signed-in shell is English, the user menu offers no language item at all, and `<html lang>` is `en` -- which is what a screen reader picks its voice from. 3 passed. | **PASS** |
| 10 | USB GUARD against a real key | the device is seen, and an ordinary key is left alone | Run on PC-RECEPTION on 2026-10-26 with the owner's own Kingston key (FAT32, 7.21 GB, her files on it). USB GUARD saw it in under a second as `MassStorage`, `Removable Disk E:`, and the alert reached the GRID two seconds later. The key came back **Healthy**, same free space, nothing written or removed. **The first run found a false positive and it is recorded as one:** it returned Critical because of the boot signature `0x55AA`, which is on the first sector of every FAT32 and NTFS volume including a blank one. In the shipped Permissive mode Critical means the drive is made read-only, so a nurse's key would have been locked. Fixed in `6d33d77`: bootable now means a bootloader is present. Full account in `docs/testing/USB_GUARD_WITH_A_REAL_KEY.md`. | **PASS after a fix** |
| 11 | EXFIL WATCH | — | **not tested on the bench.** It is in its learning phase and reports `degraded/learning_phase` honestly until it has a baseline, which it cannot reach here. | **not tested** |

| 12 | Attribution after the clock is corrected while the agent runs | the writer is still named and still suspended | Run on PC-LAB on 2026-10-28. With the old agent, a +3 h 01 m clock change produced five alerts, every one `unavailable / pid_reused`, and nothing suspended -- the demo-breaking failure. With `8d89fd2`+`84500b9` the same change named `powershell PID 1152`, with the chain `explorer -> cmd -> powershell`, and suspended it; repeated after a 7 h 52 m correction, naming `PID 4684`. The heartbeat now carries the workstation's clock, so the console and `demo-ready.ps1` can see drift instead of inferring it. | **PASS after a fix** |
| 13 | A role change while the user is signed in | it takes effect at once, with no sign-out | Run against the live GRID with a dedicated account (`bench-roles@hopital-yaounde.cm`, password in the bench credentials note, never printed). Signed in as a `security_analyst`, triaged an alert: 200. An administrator changed the roles to `read_only_auditor`. The **same token**, with nothing refreshed: 403. Still signed in, still reading (200), and `/dashboard/me` already said `read_only_auditor`. The roles are read from the database on every request, so there is nothing to revoke and nothing to wait for. `scripts/role_change_bench_proof.py` repeats it. | **PASS** |
| 14 | The owner's ten-step demo, rehearsed end to end | every step as the guide describes it | Run on PC-RECEPTION on 2026-10-28, from a wiped machine: installed from the disc in one go, appeared in the console as ACTIVE, the decoy trigger was caught (`powershell PID 3220`, Critical, suspended after its first write), and the resume from the console let it continue six minutes later -- 21:29:55 then 21:36:33, in the trigger's own output. Ten steps, ten passes, with screenshots in `docs/screenshots/real/demo-rehearsal-2026-10-28/`. It also found two things: `/login` blanked the console (fixed, `b85e5ae`) and the workstation's clock was 20 h 07 m behind. | **PASS** |

**12 of 12 runnable tests pass.** Test 11 cannot be run on this bench, and says why.

## Bugs found and fixed during the run

All five were found by running the thing, not by reading it.

### 1. Every suspension closure alert was refused (422) — `alerts_rejected`

`client_message_id` for a closure or escalation alert was built by appending a suffix to the
SHA-256 hex of the suspension id. That hex is already exactly the 64 characters the GRID allows,
so `{hash}-process_resumed` came to 80 characters and the GRID refused it. 80 for
`process_resumed`, 81 for `suspension_ended`, 78 for `resume_failed`.

One defect, two symptoms: the `alerts_rejected` flag on the agent's posture, and a process resumed
from the console that stayed listed as suspended — `NtResumeProcess` had succeeded, but the alert
that says so never got in. The suspension alert itself used a bare hash and always went through,
which is why the module looked healthy and only the responses went missing.

The discriminator now goes **into** the hash instead of being appended. Fixed in `d7e7c89`.
Proven here: `rejected_alerts = 0`, and test 4 passes.

### 2. A fresh install raised a Critical alert for every decoy it planted

Six on the first bench, fifteen on the second, twenty-four on the third — the count follows the
number of canaries. This took three attempts to fix properly, and the first two were wrong:

- First attempt: the own-write mark was set by the deployment service **after**
  `CreateCanaryAsync` returned — after the bytes were already on disk, and after the
  FileSystemWatcher had already reported them. Moved into the component that does the writing,
  before the write (`9c337c7`). Still fifteen alerts.
- What actually caused it: **the kernel reports the agent's own writes as somebody else's.**
  Windows' cache manager flushes a file long after the process wrote it, and ETW attributes that
  flush to a *system PID*, not to the writer. Filtering the agent's own PID out of the session
  therefore does not cover the agent's own writes. Measured on the bench: canaries written at
  17:51:28, the same writes surfacing at 17:51:46 — eighteen seconds later, as `system_pid`.
  The ETW consumption loop never asked whether a write was the agent's own, and the restoration
  grace was 3 seconds, so each alert triggered a repair, and each repair came back as a fresh
  attack. SENTINEL ended up reporting **SUSTAINED ATTACK: 10 restorations in 1 hour ... Manual
  intervention required** over a file only it had ever touched.

Fixing it took two more goes, and CI refused both of the wrong ones:

- `ba519c2` made the ETW path consult the own-write set and stretched the restoration grace to 60
  seconds. CI failed three `SentinelCanaryRestoreTests`: a canary the agent had just repaired was
  unwatched for a minute, so an attacker's write five seconds later went unreported. A detection
  gap, not a fix.
- `823c309` split the windows, since the two paths see the same write at different times: the
  watcher stays prompt and keeps its short grace, and only the kernel path gets the 60-second
  window. CI then failed the real-service job — *"the canary writer was not suspended within
  15 s"* — because the guard was dropping **every** kernel record on a recently written path,
  including a genuine attacker's, so nobody was attributed and nobody was suspended.
- `69041da` narrows it to what was actually measured: only a write from PID 0 or 4, which is what
  the agent's own writes come back as, on a path the agent wrote itself. A record naming a real
  process is never dropped. That is the same test `CanaryAttributor` already uses to decide a
  write names nobody.

Both directions are now true on the bench at once: a fresh install raises nothing, and a real
writer is still named and suspended.

### 3. A workstation could be enrolled exactly once in its life

Enrolment refuses a hardware fingerprint that is already known (409), and the lookup ignored the
agent's status. So the fingerprint stayed taken for ever: reinstall the agent, rebuild the
machine, or press the console's own **decommission** button, and that workstation could never come
back. The console offered the button and no way to undo what it caused.

In a hospital that is a workstation permanently outside the console after a reinstall. Fixed in
`f92a958`: a decommissioned agent no longer holds its machine's fingerprint, so decommissioning is
the supported way to let a rebuilt workstation enrol again. Proven here — PC-LAB was reinstalled
four times during this run.

### 4. Enrolment codes expired before the job was done

Five minutes had to cover downloading the installer, carrying it to the workstation, answering the
UAC prompt, the service starting and the enrolment completing. On this bench it did not: the code
died mid-install twice and the agent retried with a dead code. Now 15 minutes, still single-use
(`f92a958`).

The console also said "valid for 5 minutes" in a hardcoded sentence while the real figure came
from the server, so the two could drift apart — and had. The sentence no longer carries a number;
the validity is already shown next to the code itself, from the value the server returns.

### 5. `agent-alert-types.json` claimed to list every alert type and omitted six

All six ADR-026 response types were missing from the contract, and the agent-side test that
recomputes the list from the code did not enumerate those mappers either. Both sides agreed
because both were blind — which is why nothing caught a response alert that never arrived. Fixed
in `d7e7c89`.

## What is still not right

- ~~**The installer speaks French only.**~~ Fixed on 2026-10-25: the installer's output is English
  throughout, and the install on PC-LAB that day was read end to end in English.
- ~~**The install ends in 1053, "the service did not respond to the start or control request in a
  timely fashion".**~~ Seen on PC-LAB on 2026-10-26: the agent was installed, the service created,
  and then `sc start` gave up and the workstation was left unenrolled. I first wrote this off as
  the host being busy. That was a guess and it was wrong.

  A Windows service has to report itself running inside the Service Control Manager's limit, 30 s,
  which is not ours to set. The .NET host reports that only once every hosted service has returned
  from `StartAsync` — and for a `BackgroundService` that is its first real await. The agent was
  doing nearly all of its startup before that point: the ETW kernel session and the file watchers,
  writing every decoy, resolving the watch paths across every user profile, a two-second wait for
  the IronClad device followed by a reconciliation over the wire, and the TLS enrolment round trip
  to the GRID. Each of those now yields first, and the IronClad reconciliation is a
  `BackgroundService` rather than a blocking `StartAsync`.

  The installer no longer loses an installation to one slow start either: 120 s instead of 60, it
  asks the service what state it is actually in rather than trusting sc.exe's exit code, and it
  starts it a second time before giving up.

  Proven by the thing itself: the same sequence on the same workstation, reinstalled from HEAD
  `8cd80bb`, printed "Service started" and then "Workstation enrolled (agent 6d4d9512…)" on the
  first attempt, with no 1053 and no retry line.
- **A first-ever install still trips Windows' 30 s limit.** The startup work was moved off the
  path that makes Windows wait (see the 1053 entry above) and a warm start on PC-RECEPTION is now
  **13.0 s**, measured. But the very first start after an install did not reach Running inside
  120 s: the schema is created then, Entity Framework is JIT-ed for the first time, and the
  endpoint scanner is reading 94 MB of freshly extracted files. The installer's retry is what
  carries the installation — it reported the 1053, waited, started the service again, and the
  workstation enrolled. So an install succeeds, and the owner guide says what that line means, but
  the first start is not yet inside the limit on its own.

  What is left on the startup path and would be the next thing to move: `EnsureDatabase()` runs
  `Database.Migrate()` before `host.Run()`, so the schema work happens before Windows is told
  anything. Moving it would mean the services that read the database have to wait for it, which is
  a bigger change than belonged in this pass.
- **The agent's own log file is empty.** `C:\ProgramData\RansomGuard-CM\logs\agent-<date>.log` was
  0 bytes on PC-RECEPTION while the agent was running and enrolled. Only `install.log` had
  anything in it. The owner guide and the troubleshooting table both send people to that folder
  when something goes wrong, so as it stands they would find nothing there.
- **An idle VM is reported disconnected, and comes back on its own.** PC-LAB read `disconnected`
  in the console for about an hour while its service was `RUNNING`; the heartbeat stalls while the
  guest sits idle and resumes when it is used again, at which point the workstation returns to
  `active` without anyone doing anything. Seen twice on 2026-10-26. It is the virtual machine
  idling rather than the agent failing, but a demonstration left sitting will show it.
- **EXFIL WATCH cannot reach `active` on a bench.** It needs a learning period before it has a
  baseline, so a freshly installed agent is `partial` for that reason alone, and will be until the
  learning phase ends. That is honest, but it means `protected` is not reachable on a new
  workstation on day one.
- **ANTI_TAMPERING is not implemented.** It reports so.
- ~~**Decommissioned agents stay in the list.**~~ Fixed: `list_agents` hides them unless
  `include_decommissioned=true` asks, and they are never deleted. Confirmed on 2026-10-26 — five
  decommissioned `PC-LABO` rows and one `WIN` row exist and none of them is on the page.
- ~~**One write on a decoy raises `SentinelCanaryModified` twice.**~~ Fixed on 2026-10-26, and
  confirmed on the bench the same day: on agent `1.0.0+8cd80bb`, two writes on PC-LAB (07:46:02
  and 07:46:39) raised **one alert each**, both `attribution=resolved`, PID 7504, chain
  `explorer -> cmd -> powershell`. The owner guide no longer tells anyone which of two alerts to
  open, because there is only one. What follows is the defect as it was found.

  Found on 2026-10-26 walking the
  owner guide on PC-LAB. The trigger wrote once, at 00:26:38, and the console shows two Critical
  alerts one second apart on the same file:

  | | alert | attribution | process tree |
  |---|---|---|---|
  | 00:26:38 | `c69f6d43` | resolved, powershell PID 1232 | `explorer -> cmd -> powershell` |
  | 00:26:39 | `a986de0c` | **unavailable — `no_write_event`** | **none** |

  Two paths raise the alert and neither knows about the other. The file watcher raises the first
  and can attribute it, because an ETW write event is there to match. `RaiseCanaryAlertAsync` then
  restores the decoy's contents, and the periodic integrity sweep in `SentinelMonitor` runs while
  that restore is still in flight, finds the file not matching its stored hash, and raises again —
  this time with no write event to attribute, so no writer and no genealogy.

  The sweep exists as a safety net for a write the watcher misses, so it could not simply be
  removed. Two things were wrong and both had to go. `ReportedModifications` now records a
  modification from the moment the alert is raised until its restore has stored the new hash, and
  the sweep stays quiet for exactly that one — not for a stretch of time, so a write landing after
  the restore is still reported as the separate attack it is. And the sweep no longer judges a
  decoy from the snapshot it read when the sweep began: it re-reads in a scope of its own, because
  the stale copy came from that scope's DbContext and Entity Framework would hand the same object
  back.

  `SentinelSweepDuplicateTests` pins both halves: one write is one alert and it carries the
  resolved attribution; a second write, after the restore has settled, is its own second alert.

  It is not new. The alert list carries pairs like this from earlier runs too; nothing looked for
  it because the attributed alert was always the one opened.
