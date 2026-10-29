# The demo fixes, proven on the bench — 2026-10-29

Four screenshots of the running system, taken on PC-LAB (agent `1.0.0+dd94f9a`) and the console
at http://localhost:5175.

## USB GUARD, with the owner's own key

| | |
|---|---|
| `01-usb-guard-scanned-the-key-no-findings.png` | The agent's own log: `USB SCAN: Starting scan of E: (max size: 100 MB, timeout: 120s)` then `USB SCAN: Completed E: — 2 files scanned, 2 skipped, 0 findings, severity: None, duration: 634ms`. Two files are the .docx and the .pub; the two skipped are ISO images over the 100 MB limit. |
| `02-the-key-is-unchanged-and-still-writable.png` | `E: label='JEFF RAOUL' NTFS 14.452 GB, 9.47 GB free`, `disk 1: read-only = False`, `files: 4, bytes: 5007467131`. |

The host counted the same four files and the same 5007467131 bytes before the key was attached and
after it was returned. **Nothing written, nothing removed, and the drive was never made
read-only** — which is what the shipped Permissive mode does to a drive it calls Critical.

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
