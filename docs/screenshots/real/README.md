# Real screens — compose GRID, VM agent, honest state

Two capture rounds. All at 1440x900. Nothing from `seed_demo_data.py`: that script is excluded
from the GRID image and refuses to run outside `GRID_ENVIRONMENT=development` on SQLite.

## Round 1 — console before enrollment (2026-10-24, GRID `fb41ac6`)

Captured at `http://localhost:5175` → nginx `192.168.56.1:8443`, Vite proxy validating the GRID
cert via `NODE_EXTRA_CA_CERTS`.

| File | What it shows |
|---|---|
| `01_agents_before_enrollment.png` | 0 registered endpoints on the real stack |
| `02_add_agent_modal.png` | Real enrollment code from the GRID + "Download the installer" button |
| `03_installer_downloaded.png` | After clicking: no "No installer is published" error |

## Round 2 — real agent, real detection, real suspension (2026-10-25)

VM: `win` (Windows 10 Pro 22H2, VirtualBox, 4 vCPU, host-only + NAT adapters), signed in as
`vboxuser`. GRID at `192.168.56.1`. Agent at `0a99aa0...` (compiled from source then).

| File | What it shows |
|---|---|
| `04_installer_progress.png` | Installer running in-VM, steps 1-3/6 (verify, service check, extract) |
| `05_installer_service_1053.png` | Step 6/6 **failed**: `sc.exe start RansomGuardAgent (1053) : service did not respond in time`. ROOT CAUSE: missing `vcruntime140.dll` / `msvcp140.dll` (Visual C++ 2015-2022 Redistributable). libsodium.dll depends on them, NSec cryptography can't initialize, `AuditLogSigner` crashes in its type initializer. See `[12:57:01 FTL]` block in the admin shell. **Fix committed to the publish script**: VC runtime now bundled in the agent zip, so new installers don't need this on the endpoint. |
| `06_dashboard_real_agent.png` | After the fix: dashboard with 1 real endpoint, 6 real CRITICAL `SentinelCanaryModified` alerts, MITRE T1486 shown |
| `07_agents_list_real.png` | WIN endpoint, protection **PARTIAL** (ENTROPY degraded/attribution unreliable, honest since B4), active, 18s ago — see the note below, this is the pre-fix posture |
| `08_agent_detail_real.png` | Modules table: SENTINEL active, ENTROPY/GENEALOGY/EXFIL_WATCH degraded with reasons, ANTI_TAMPERING not implemented. Zero alerts not transmitted. |
| `09_alerts_list_real.png` | 6 alerts, all CRITICAL `SentinelCanaryModified` on WIN |
| `10_alert_detail_real.png` | Canary file path, **Who did it?** = "Process not identified — kernel monitoring unavailable on this endpoint" (ADR-025 attribution honest when ETW is not available), **Response** = "No response possible: the process is not identified, the agent never acts on a guess" |
| `11_agent_with_suspension.png` | After manual trigger: agent detail page with Suspended processes block below modules |
| `12_alerts_with_suspension.png` | Alerts list including `SentinelProcessSuspended` and the new `SentinelCanaryModified` from the trigger |
| `13_alert_suspension_detail.png` | Suspension alert: "Process suspended after writing a canary: powershell (PID 3792)" |
| `14_suspended_processes_block.png` | Focused block: `powershell (PID 3792)`, decoy file written, Resume button |
| `15_after_resume_click.png` | Confirmation posted (showing the posture state during the resume dispatch) |
| `16_vm_shell_resumed.png` | **Resume worked**: inside the VM, the suspended PowerShell (PID 3792) processed its queued keystrokes and printed `my PID is 3792 at 10/25/2026 13:22:41` — matching the GRID's command_queue `Completed` timestamp. NtResumeProcess returned success and the frozen process ran again. |

> **These screenshots predate the ENTROPY attribution work.** They were taken while ENTROPY
> reported `degraded / attribution_unreliable` for the two gaps B4 disclosed: alerts carried PID 0
> (AGT-ATTR-001) and rule 3 was never evaluated (AGT-EN-002). Both are closed as of `4ccb71e`:
> an ENTROPY alert now names the process that wrote the file, rule 3 evaluates, and the module
> reads `active` whenever the kernel FileIO session is up. The agent running in the `win` VM was
> installed before that and still reports the old posture; it picks up the new behaviour when it
> is reinstalled from a freshly published installer. The screenshots are kept as they are because
> they are an honest record of what the console showed that day.

## What this proves

End-to-end, on a real Windows VM against the real compose GRID (no mocks, no seed, nothing
faked):

1. **Install** — ADR-027 double-click installer unpacks, writes config, adds the GRID CA to the
   VM's `LocalMachine\Root`, registers the Windows service (LocalSystem, auto-start).
2. **Enrollment** — agent POSTs `/api/v1/agents/enroll`, GRID returns an `agent_id`, agent
   begins heartbeats and alert transmission.
3. **Detection** — SENTINEL fires `SentinelCanaryModified` alerts as the canary files are
   modified (6 on first startup, 1 from the manual trigger).
4. **Attribution (ADR-025)** — when ETW is unavailable the console says so plainly ("kernel
   monitoring unavailable"), does not guess, does not act. When the writer is a process the
   kernel can observe (the trigger script's PowerShell), attribution resolves and names it:
   "powershell (PID 3792)".
5. **Suspension (ADR-026)** — resolved attribution + `Sentinel.Response.SuspendProcess = true`
   → the agent calls `NtSuspendProcess`. The PowerShell that wrote to the canary was frozen
   in-session; typed input stopped being processed.
6. **Resume (ADR-026 Phase 2)** — the console `Resume` button queues a command. The agent polls
   `/commands`, picks it up, calls `NtResumeProcess`.

## What found (and fixed) along the way

Two real bugs the test caught:

- **The agent couldn't start on a clean Windows 10** because `vcruntime140.dll`,
  `msvcp140.dll`, `vcruntime140_1.dll` were not bundled (NSec→libsodium dependency chain).
  Fixed in `deployment/grid/publish-local-installer.ps1` step [2b] — the zip now includes them
  (grew from 56.00 → 56.26 MB). The CI publish job (`.github/workflows/cd.yml`) needs the same
  addition before shipping (follow-up).
- **The agent couldn't fetch its command queue** because
  `deployment/grid/nginx/conf.d/agent.conf` only routed `(alerts|audit-logs|heartbeat|
  threat-intel-version)` — `/commands` returned 404 from the static file handler. Fixed by
  adding `commands` to the alternation.
- **`trigger-sentinel.ps1`** missed canaries because they are hidden+system; added `-Force` to
  `Get-ChildItem`.

## What's open (not hidden)

- **Resume succeeded on the agent side but the console doesn't learn about it.** Three
  separate Resume commands against PID 3792 all moved to `command_queue.status=Completed`.
  The VM shell confirmed it was resumed: queued keystrokes to the frozen window got processed
  as soon as NtResumeProcess returned (shot 16). But no `SentinelProcessResumed` /
  `SentinelSuspensionEnded` alert was POSTed to `/api/v1/agents/{id}/alerts` and the
  Suspended Processes list in the console still shows PID 3792 as frozen. The server-side
  rendering of that list depends on a closure alert from the agent, so this is almost certainly
  an agent-side gap in the ADR-026 Phase 2 reporting path: the resume command executes but the
  closure alert is not emitted. Needs an issue to track and a fix in SentinelMonitor's resume
  handler.
- The 6 "startup" alerts were fired by SENTINEL writing the canary files for the first time,
  because the file-watcher sees the write before the newly-created canary is marked as owned.
  Not a false positive in the clinical sense — the detector did react to a real write — but on
  a hospital workstation the admin would see six alerts on install. Worth suppressing at the
  start-up window or marking the canary before it exists on disk (follow-up).
- The CI pipeline doesn't bundle VC runtime yet. Any installer downloaded from a CD-built GRID
  will fail on a clean Windows 10. The local `publish-local-installer.ps1` fixes it; `cd.yml`
  needs the same addition.
