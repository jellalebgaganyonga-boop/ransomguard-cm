# Demo rehearsal — 2026-10-28, PC-RECEPTION

The owner's ten-step demo, rehearsed end to end on the real stack before the video: the GRID in
Docker on the host, the console on http://localhost:5175, and PC-RECEPTION as the hospital
reception computer, wiped beforehand so the agent was installed from nothing.

The ten steps are Steps 1 to 10 of `docs/OWNER_TEST_GUIDE.md` — start the platform, sign in,
install the agent on the hospital computer, see it in the console, simulate the attack on a decoy
file, see the detection — plus the resume that closes it (the guide's Step 11), which is the half
of the story that shows a person is in charge.

Every screenshot is of the running system. Nothing here is mocked, and no password appears in any
of them; the enrollment code visible in the installer output is masked by the installer itself and
expired the same evening.

| # | Step | Screenshot | Result |
|---|---|---|---|
| 1 | The GRID is up | `01-grid-stack-healthy.png` | PASS — `{"status":"ok","version":"0.8.0"}` |
| 2 | The console opens | `02-console-sign-in-page.png` | PASS — English, with the EN/FR switch |
| 3 | Sign in as the hospital administrator | `03-signed-in-as-the-hospital-administrator.png` | PASS |
| 4 | The dashboard counts the fleet | `04-dashboard-counts-one-fleet.png` | PASS — 2/2, the same two the Agents page lists |
| 5 | The workstation is ready | `05-workstation-clock-corrected.png` | PASS — its clock was 20 h 07 m behind and was set |
| 6 | Install the agent from the disc | `06-agent-installed-on-pc-reception.png` | PASS — enrolled in one run, 1.0.0+dd94f9a |
| 7 | The workstation appears in the console | `07-pc-reception-active-in-the-console.png` | PASS — ACTIVE, heartbeat 54 s |
| 8 | Simulate the attack on a decoy | `08-attack-simulated-on-a-decoy.png`, `08b-the-writer-stopped-after-one-write.png` | PASS — one write, then nothing: the writer was stopped |
| 9 | See the detection | `09a-the-detection-in-the-alert-list.png`, `09b-the-writer-named-and-suspended.png`, `09c-the-suspension-on-the-workstation-page.png` | PASS — `powershell (PID 3220)`, Critical, suspended |
| 10 | Resume from the console | `10a-resume-requested-from-the-console.png`, `10b-the-process-resumed-on-the-workstation.png`, `10c-the-workstation-page-after-the-resume.png` | PASS — frozen 21:29:55 → 21:36:33, resumed on request |

## What the rehearsal found

- **/login blanked the console.** Not a route (`/auth/login` is), and the 404 page crashed on
  render, so a mistyped address showed a white screen. Fixed in `b85e5ae`, with a test.
- **PC-RECEPTION's clock was 20 h 07 m behind.** Nothing keeps a VM's clock without Guest
  Additions. It was set before the rehearsal, and `demo-ready.ps1` now refuses a demo on a
  workstation more than two minutes from the GRID.
- **The alert page has no Resume button.** The response controls live on the workstation page
  (`09c`), which is where the owner's guide sends you, but somebody reading the alert would look
  there first.
- **Posture reads PARTIAL on a fresh install.** EXFIL WATCH is in its learning phase for the
  first hours, and the audit chain has not uploaded yet, so the console says "audit log
  incomplete". Both clear on their own; a demo that starts within minutes of installing will show
  PARTIAL.
