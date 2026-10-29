# The demo fixes, proven on the bench — 2026-10-29

Four screenshots of the running system, taken on PC-LAB (agent `1.0.0+dd94f9a`) and the console
at http://localhost:5175.

## USB GUARD, with the owner's own key

| | |
|---|---|
| `01-usb-guard-scanned-the-key-no-findings.png` | The agent's own log: `USB SCAN: Starting scan of E: (max size: 100 MB, timeout: 120s)` then `USB SCAN: Completed E: — 2 files scanned, 2 skipped, 0 findings, severity: None, duration: 634ms`. Two files are the .docx and the .pub; the two skipped are ISO images over the 100 MB limit. |
| `02-the-key-is-unchanged-and-still-writable.png` | `E: label='JEFF RAOUL' NTFS 14.452 GB, 9.47 GB free`, `disk 1: read-only = False`, `files: 4, bytes: 5007467131`. |

The key was byte-identical before it was attached and after it was returned — 773 files,
5300256010 bytes, 9.47 GB free. **Nothing written, nothing removed, and the drive was never made
read-only** — which is what the shipped Permissive mode does to a drive it calls Critical.

> **Two corrections to this picture, found the same day.** The screenshot reads `files: 4`: the
> bench script counted without `-Force`, so it counted only what was not hidden; the key holds 773.
> And `2 files scanned, 0 findings` was not a clean key, it was a scan that never looked. 769 of
> those files sat in a Hidden + System folder called `RECYCLER.BIN`, disguised as the recycle bin,
> holding 766 of the owner's documents under base64 names and a script that collects the machine's
> details onto the key. The scan skipped them because of `AttributesToSkip = FileAttributes.System`.
> The script and the scanner are both fixed; see `docs/testing/BENCH_TEST_RESULTS.md`, test 17.

Before `70b5c2a` the content scan forgave formats by extension, and `.docx` was not among them: a
Word file is a zip, a zip reads near 8 bits per byte, and the key would have crossed the line that
makes it read-only.

## The decision on the alert the owner actually opens

| | |
|---|---|
| `03-resume-on-the-suspension-alert.png` | "Process suspended after writing a canary: powershell (PID 3620)" now shows **who did it** (powershell, PID 3620, with its image path), **what started it** (`explorer -> cmd -> powershell`), and **Response: Process suspended — waiting for your decision, nothing resumes by itself**, with the Resume button under it. |
| `04-resume-requested-from-the-alert.png` | After the click: `Command "Resume": sent, waiting for the agent`. |

On the workstation, the trigger's own output: `05:23:46 write 1/5 done`, then nothing for two
minutes and twenty-one seconds, then `05:26:07 write 2/5 done`. The process was frozen until an
administrator decided, from the alert page, to let it go.

## Two hospitals on one server

PC-RECEPTION was wiped, then its agent was downloaded from the **clinic's** console with the
clinic's own enrolment code. The installer says which organization it belongs to before it
installs anything: `Organization : test-clinic`.

| | |
|---|---|
| `05-pc-reception-reset-to-before-the-agent.png` | the wipe: service deleted, data and binaries removed, 9 decoys and the test zone gone |
| `06-hospital-dashboard.png`, `06-hospital-agents.png` | `yde-hospital`: 1/1 active, **PC-LAB** only, in English |
| `07-clinic-dashboard.png`, `07-clinic-agents.png` | `test-clinic`: 1/1 active, **PC-RECEPTION** only, in French -- the language belongs to the account |
| `08-clinic-alert-with-resume.png` | the clinic's own detection, with the writer named (`powershell PID 2664`), the chain `cmd -> powershell`, and *Reprendre le processus* |

Neither console shows the other's workstation or the other's alerts, and asking for the other
tenant's agent by its id answers **404**, not 403: a customer must not learn even that the thing
exists.

The clock on PC-RECEPTION was 45 min 58 s behind and was corrected **while the agent was
running** -- the exact condition that used to blind attribution. The detection that followed still
named `powershell (PID 2664)` and suspended it, and the trigger's own output shows the five writes
and the four pauses: 07:03:28, 07:06:43, 07:07:33, 07:08:33, 07:09:32.
