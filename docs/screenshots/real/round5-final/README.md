# Round 5 — the final bench run, 2026-10-26

Everything here was captured from a real run on HEAD `8cd80bb` (agent `1.0.0+8cd80bb`). Nothing is
staged or redrawn.

## `pc-lab-vm/` — the live detection inside PC-LAB

Taken while `scripts/system-check.ps1 -WithDetection PC-LAB` drove the workstation: a Command
Prompt opened, `D:\trigger-sentinel.cmd` typed into it, and the window frozen mid-write by the
suspension. That run finished **33 PASS, 0 FAIL**, and the chain it read back from the GRID was
`explorer -> cmd -> powershell`, writer `powershell` PID 7504.

| | |
|---|---|
| `20-vm-cmd-open.png` | the Command Prompt, before the trigger |
| `21-vm-trigger-typed.png` | `D:\trigger-sentinel.cmd`, read back before Enter |
| `22-vm-trigger-suspended.png` | the trigger stopped after its first write: the suspension |
| `23-vm-after-check.png` | the workstation once the check had resumed it |

## `reception-rehearsal/` — the owner's guide, walked on PC-RECEPTION

`docs/OWNER_TEST_GUIDE.md` followed step by step on a workstation that had never had the agent,
to meet whatever the owner will meet. What it found is in the guide's own history; the pictures
are the evidence.

| | |
|---|---|
| `07-uac-publisher-unknown.png` | step 7: "Publisher: Unknown", about 30 s after the double-click |
| `08-installer-enrolled.png` | step 7: 1/6 to 6/6, the 1053 retry, then "Workstation enrolled" |
| `09-trigger-typed.png` | step 9: the trigger, started from Explorer as the guide says |
| `09-trigger-suspended.png` | step 9: the first write, then the freeze |
| `11-trigger-finished.png` | step 11: all five writes, "finished", after five Resume presses |
| `12-reception-clean-and-running.png` | the workstation left as the owner will find it: no agent, no decoys, no test zone, documents intact |

The 1053 in `08` is worth looking at rather than skipping. It is the Service Control Manager
giving up at its own 30 s limit on a first-ever install, and the installer saying so, waiting, and
starting the service a second time — which is why the line below it reads "Service started" and
then "Workstation enrolled" instead of the install being lost.
