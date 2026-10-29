# Offline, on today's build — 2026-10-29, PC-LAB

The question this answers: *does it only work while the server is up?*

Both of PC-LAB's adapters were cut from the host (`VBoxManage controlvm PC-LAB setlinkstate1 off`
and `setlinkstate2 off`), so the workstation could not reach the GRID at all. Then the harmless
decoy trigger was run anyway.

| | |
|---|---|
| `01-network-cut.png` | the workstation with both adapters down |
| `02-trigger-typed-with-no-network.png` | the command, photographed before Enter |
| `03-suspended-with-no-network.png` | `07:20:23 write 1/5 done` — and then nothing. The writer (`powershell`, PID 8584) was named and suspended by the agent, on the machine, with nobody to ask |
| `04-the-backlog-arrived-with-its-own-time.png` | the console a minute after the link came back |
| `05-the-alert-raised-while-offline.png` | the alert itself, with the writer, the chain and Resume |

While the link was down the hospital's console stayed at **261 alerts**: it cannot show what it
has not been told. A minute after the link returned it read **263**, and the two new rows carry
the times that matter:

| | detected | arrived |
|---|---|---|
| `SentinelCanaryModified` | **06:20:23** | 06:27:43 |
| `SentinelProcessSuspended` | **06:20:25** | 06:27:43 |

Seven minutes apart. The alert carries the time it **happened**, from the workstation's own clock,
not the time it was delivered — an incident report that said 06:27 would be wrong about when the
hospital was attacked.

Detection and response live on the workstation; the GRID is where the evidence goes, not where the
decision is made. The suspended process was then resumed from the console, as it would be in the
demonstration.

The earlier run of this test, on the agent that predates the clock and alert-page work, is in
`docs/screenshots/real/offline/`.
