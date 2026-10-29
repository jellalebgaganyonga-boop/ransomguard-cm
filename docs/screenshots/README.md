# Console screenshots

Captured 2026-10-24 from `sprint8/recovery`, at a 1440x900 desktop viewport
(the console is desktop-only, so there is no phone width here).

Reproduce with:

```bash
cd grid      && PYTHONPATH=src .venv/Scripts/python.exe scripts/seed_demo_data.py
cd dashboard && node scripts/capture-screenshots.mjs
cd dashboard && node scripts/capture-evidence.mjs
cd dashboard && node scripts/capture-ci-card.mjs runs.json
```

## What the data behind these images is

**The agents and alerts in these screenshots are invented.** They were written
into a local database by `grid/scripts/seed_demo_data.py` so the interface would
have something to draw. No agent produced them; no file was encrypted; no
process was suspended; nothing was detected.

These images are evidence that **the console renders and behaves correctly**.
They are not evidence that any detection module works. That evidence is the test
suites (`13_test_results.png`, 1 641 tests green on CI) and, when it is run, a
real agent on a bench — which has not been done yet.

Every seeded alert carries its provenance in the database, so the invention can
always be traced:

```sql
SELECT * FROM alert_details WHERE key = 'data_provenance';  -- 'synthetic_demo_seed'
```

If you present any of these images, say the data is synthetic. Do not caption
them as detections.

## Where the screenshots depart from the brief, and why

Three things could not be shown honestly, so they are not shown:

1. **No endpoint reads "protected" (green).** The verdict in
   `services/agent_posture.py` requires all four detection modules active, and
   since B4 (`882599c`) ENTROPY honestly reports `degraded /
   attribution_unreliable` — it cannot name the process that wrote a file, so it
   does not claim otherwise. A real agent on this build therefore reads
   **partial**, and so do all three here. `04_agent_detail_protected.png` keeps
   its filename from the capture plan, but it shows PC-URGENCES-01's real
   partial posture with the module table that explains it.

2. **There is no IRONCLAD/backup alert.** IRONCLAD is PROD-001, not implemented:
   no module code, no alert type in `shared/contracts/agent-alert-types.json`,
   and `Severity` has no `info` level. A "backup completed" row would have been
   a picture of a feature that does not exist, so six alerts were seeded, not
   seven.

3. **The Tor-exit-node rule cannot fire.** `TorExitNodeRule` is one of four
   EXFIL rule classes left unregistered because the threat-intelligence lists
   they read are not fed. The 450 MB outbound burst is seeded as
   `ExfilWatchVolumeAnomaly` — a registered rule that really does detect that —
   with the destination kept in the alert details.

One bug was found and fixed while capturing: the executive dashboard's first KPI
card was labelled "Protected endpoints" but showed `active_agents`, which counts
enrolment status, not the protection verdict. With real rows present it read
"3 / 3 protected" while every agent was partial. The card is now labelled
**Active endpoints** / **Postes actifs** in both locales.

## The files

| File | What it shows |
|---|---|
| `01_login_page.png` | Sign-in, before credentials |
| `02_dashboard_overview.png` | Executive dashboard: KPIs and recent alerts |
| `03_agents_list.png` | The three demo endpoints, all **partial**, with heartbeat ages |
| `04_agent_detail_protected.png` | PC-URGENCES-01: posture verdict and the per-module table |
| `05_alerts_list.png` | All six alerts, with severity and module |
| `06_alert_detail_sentinel.png` | SENTINEL critical: ADR-025 attribution, ADR-026 suspend action |
| `07_alert_detail_entropy.png` | ENTROPY: Shannon entropy on the file |
| `08_alert_detail_exfil.png` | EXFIL WATCH volume anomaly, attribution **ambiguous** |
| `09_alert_detail_usb_blocked.png` | USB GUARD: unknown device blocked |
| `10_settings_tenant.png` | Settings → Profile (there is no separate tenant page; the tenant is carried here) |
| `11_console_french.png` | The same dashboard in French |
| `12_grid_swagger.png` | The GRID's OpenAPI docs, served because `GRID_DEBUG=true` on this local run |
| `13_test_results.png` | Test counts per suite, with the CI run ids they come from |
| `14_project_tree.txt` | Project structure, depth 3 |
| `15_ci_badge.png` | Recent GitHub Actions runs — **rendered from the GitHub API** via `gh run list`, not a capture of github.com (the browser here has no route to it). The run ids are real and resolve on github.com. |

## One thing that changes between runs

The posture verdict is computed from the clock, not stored: an agent whose last
heartbeat is over five minutes old reads "signal lost". The seed writes
heartbeats relative to the moment it runs, so **re-seed immediately before
capturing**, or the endpoints will show as disconnected.
