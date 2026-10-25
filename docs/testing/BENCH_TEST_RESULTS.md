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
| Each workstation has | a non-administrator account `infirmier`, and 200 invented hospital documents in `C:\Users\Public\Documents\Hospital-FICTIONAL\` (50 each in Patients, Laboratory, Pharmacy, Imaging) |
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
| 2 | Posture and module table, honest per agent | each module's real state | `SENTINEL active`, `ENTROPY active`, `GENEALOGY active`, `USB_GUARD active`, `EXFIL_WATCH degraded/learning_phase`, `ANTI_TAMPERING inactive/not_implemented`. Verdict `partial`, and the only reason given is the one that is true: EXFIL WATCH is still learning. | **PASS** |
| 3 | SENTINEL: run `trigger-sentinel.cmd` | Critical alert, "Who did it?" names the process, process suspended | `SentinelCanaryModified` (Critical), then `SentinelProcessSuspended` — *"Process suspended after writing a canary: powershell (PID 2560)"*. "Who did it?" reads `state: resolved, process_id: 2560, process_name: powershell` — the writer is named, not guessed. | **PASS** |
| 4 | Resume from the console | the process continues on the VM **and** the console updates | `SentinelProcessResumed` — *"Process resumed on request"* — reached the GRID and the suspension left *Suspended processes*. The process really did resume: still inside its five-write loop, it wrote the canary again and was suspended again, each time. Resuming until the script finished left **0 suspensions listed** and the trigger reporting *"finished. A pause above was the suspension; the resume let the script continue."* | **PASS** |
| 6 | Tenant isolation | `admin@test-clinic.cm` sees nothing of `yde-hospital` | `test-clinic`: 0 agents, 0 alerts, 1 user (its own admin). `yde-hospital`, at the same moment: 1 agent, 10 alerts, 3 users. | **PASS** |
| 5 | Roles: auditor cannot act, analyst can triage, admin can do all | — | **not yet run** | — |
| 7 | Audit log and user-actions log record the actions above | — | partly observed: decommissioning an agent through the console wrote a `agent_decommission` user action. Not yet checked end to end. | **not yet run** |
| 8 | Agent offline then back, queued alerts delivered | — | **not yet run** | — |
| 9 | FR/EN switch on the pages used | — | **not yet run** | — |
| 10 | USB GUARD and EXFIL WATCH | — | **not tested on the bench.** USB GUARD needs a USB device passed through to the VM; EXFIL WATCH is in its learning phase and reports `degraded/learning_phase` honestly until it has a baseline. Neither can be exercised without tooling the directive rules out. | **not tested** |

**5 of 5 runnable tests attempted so far pass.** Tests 5, 7, 8 and 9 have not been run yet; test 10
cannot be run on this bench.

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

- **The installer speaks French only.** Its output is "Vérification du paquet", "Poste enrôlé", and
  so on, with no English. The console has both languages; the installer does not.
- **EXFIL WATCH cannot reach `active` on a bench.** It needs a learning period before it has a
  baseline, so a freshly installed agent is `partial` for that reason alone, and will be until the
  learning phase ends. That is honest, but it means `protected` is not reachable on a new
  workstation on day one.
- **ANTI_TAMPERING is not implemented.** It reports so.
- **Decommissioned agents stay in the list.** After four reinstalls, PC-LAB appears four times in
  the console, three of them decommissioned. Nothing hides or groups them.
