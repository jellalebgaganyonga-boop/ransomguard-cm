# AttributionBench — SENTINEL process-attribution measurement

## What this is

A **measurement instrument for a defensive product**. It measures how often
SENTINEL's process attribution (the Windows Restart Manager, queried when the
canary watcher fires) names the process that touched a canary.

It reproduces the file-system *shape* of six behaviours, from benign to
ransomware-like, **only on files it creates itself, in its own isolated
temporary directory** (`%TEMP%\rg-attribution-bench`), which it deletes at the
end. It never reads, modifies or deletes any other file. Its "encryption" is an
AES pass with a throwaway key over its own synthetic text, to reproduce the I/O
pattern of an encrypting writer — nothing more.

It is deliberately **outside `RansomGuard.Agent.sln`**: CI neither builds nor
runs it. It is versioned so the exact same measurement can be replayed:

- after ETW attribution ships, to show the move from 0 % to the measured value;
- whenever someone touches the detection pipeline, to catch a regression.

## How it measures

The pipeline is SENTINEL's, with the production code where it matters:

1. a `FileSystemWatcher` with SENTINEL's filter
   (`NotifyFilters.FileName | LastWrite | Size`, `SentinelMonitor.cs`);
2. a bounded channel (10 000, `DropOldest`), as in `SentinelMonitor`;
3. a consumer delay standing in for SENTINEL's repository lookup between the
   event and the query (argument, default 10 ms);
4. the **production** `RestartManagerHelper.GetProcessesLockingFile`, and the
   production choice of the most recently started holder.

For each iteration it creates a fresh canary-like file, lets its creation
events go by, performs the pattern, and records whether the query named the
bench's own PID (`#`), another PID (`!`), nobody (`.`), or whether no event
arrived (`_`). It also measures when the event arrived relative to the moment
the pattern released the file.

## The six patterns

| # | Pattern | Event | What it reproduces |
|---|---|---|---|
| 1 | Open, write, hold the handle 5 s, close | Modified | A slow writer holding the file |
| 2 | Open, write, close immediately | Modified | An ordinary save |
| 3 | Read, encrypt in memory, rewrite, close (fast) | Modified | In-place encryption — **ransomware-shaped** |
| 4 | Rename to `.locked` | Renamed | Extension change |
| 5 | Delete the canary | Deleted | Deletion — **ransomware-shaped** |
| 6 | Write an encrypted copy, then delete the original | Deleted | The classic ransomware pattern — **ransomware-shaped** |

The decisive rate is patterns 3, 5 and 6. Thresholds, fixed before measuring
(`CLAUDE_CODE_A5_GO_ET_A1_MESURE.md`): ≥ 80 % ship the action engine on Restart
Manager; 50–80 % ship and start ETW immediately; < 50 % go to ETW directly.

## Running it

Windows only; no administrator rights needed. From the repository root:

```
dotnet run --project agent/tools/AttributionBench -c Release -- 20 10
```

Arguments: iterations per pattern (default 20), consumer delay in ms
(default 10). A full run takes about 3 minutes (pattern 1 holds the file 5 s
per iteration).

## The measurement of 2026-09-30: 0 / 120

20 iterations per pattern, consumer delay 10 ms:

| # | Pattern | Iter | Attributed | Rate | Correct PID | Wrong PID | >1 holder |
|---|---|---|---|---|---|---|---|
| 1 | Open, write, hold handle 5 s, close | 20 | 0 | 0 % | 0 | 0 | 0 |
| 2 | Open, write, close immediately | 20 | 0 | 0 % | 0 | 0 | 0 |
| 3 | Read, encrypt in memory, rewrite, close (fast) | 20 | 0 | 0 % | 0 | 0 | 0 |
| 4 | Rename to `.locked` | 20 | 0 | 0 % | 0 | 0 | 0 |
| 5 | Delete the canary | 20 | 0 | 0 % | 0 | 0 | 0 |
| 6 | Write encrypted copy, then delete the original | 20 | 0 | 0 % | 0 | 0 | 0 |

**Ransomware-shaped patterns (3, 5, 6): 0 / 60 = 0 %** → below 50 %: ETW FileIO.

Replayed on 2026-10-08 with this rebuilt bench (another machine, same
arguments): **0 / 120 again**, 0 / 60 on patterns 3, 5 and 6; event arrival
median 0 ms relative to the handle release on every pattern.

At 0 ms consumer delay, no attribution over 20 iterations of pattern 3.
(*À 0 ms de délai consommateur, aucune attribution sur 20 itérations du
patron 3.*) This is the sentence retained, not "there is no window at all".

### Why: the trigger arrives too late

SENTINEL's watcher filters on `FileName | LastWrite | Size`. NTFS updates the
size and last-write time in the directory entry **when the handle is
released**, and only then does the watcher fire. Pattern 1 shows it: the
handle is held 5 000 ms, yet the event arrives at the release (median 0 ms).
The Restart Manager is therefore queried at the exact moment the writer has
just let go of the file. A deleted canary (patterns 5 and 6) has no path left
to query at all, whatever the speed.

So Restart Manager attribution does not merely miss fast ransomware: it fails
structurally on every write, because the detection trigger always arrives too
late.

### Decision

ETW FileIO becomes SENTINEL's **trigger** for canaries — not a second query
after the `FileSystemWatcher`, which would reproduce the same delay
(`SPRINT_8_ARBITRAGE_LOT3.md`, A.1). This bench is the before/after measure of
that work. Until then, process attribution is reported as unreliable
(`attribution_unreliable`, `ModuleStateRegistry.cs`) and SENTINEL alerts still
go out without a process (debt AGT-ATTR-001 in `agent/README.md`).

## What the 0 / 120 rests on: the instrument checks

A 0 % is suspect by nature: it can be a property of the agent or a defect of
the bench. The run of 2026-09-30 rested on four independent checks of the
instrument (report of 2026-10-01, "validation de l'instrument"). **This rebuilt
bench carries checks 1, 2 and 4** (checks 1 and 2 rebuilt on 2026-10-15,
AGT-BENCH-001); check 3 is not rebuilt.

| # | Check | What it rules out | Result on 2026-09-30 | In this bench |
|---|---|---|---|---|
| 1 | **Reference call.** For every event, the production `RestartManagerHelper.GetProcessesLockingFile` compared with a by-the-book Restart Manager sequence written independently (`RmStartSession`, `RmRegisterResources`, `RmGetList` twice — `ERROR_MORE_DATA` to size the array, then fill — `RmEndSession`). | A defect in the production wrapper that would leave the agent blind where Windows is not. | 0 events out of 120 where only the reference call found a holder. | **Rebuilt** (`ReferenceRestartManager.cs`), run on every event |
| 2 | **Owner-thread probe.** The writer queries the Restart Manager about its own file from its own thread, while it still holds the handle. | That the Restart Manager cannot see the bench's process at all on this machine (rights, session, file location) — a 0 caused by the environment. | The holder is seen (1 holder) while the handle is held. | **Rebuilt**, 5 probes before the patterns, production and reference |
| 3 | **Restart Manager in isolation**, outside the watcher pipeline, queried 0, 250, 1 000 and 3 000 ms after the file is opened. | That the Restart Manager is unreliable on this machine whatever the timing. | 6 / 6 at every cadence. | **Not rebuilt** (AGT-BENCH-001) — a separate experiment of the original, not counted among the two checks named in `c57449c` |
| 4 | **Event arrival** relative to the moment the writer releases the handle. | Explains every failure by timing. | Median 0 ms on every pattern. | **Rebuilt**, printed at the end of each run |

The run of 2026-10-15 (development machine, `-- 20 10`), with checks 1 and 2:

- check 2, owner-thread probe: production saw the bench **5/5**, reference **5/5**;
- check 1, reference query on every event: **0** events where only the reference
  found a holder, 0 where only production did, **0** API errors;
- **0 / 120**, 0 / 60 on patterns 3, 5 and 6; event arrival median 0 ms.

So the "before" figure now rests on the instrument itself, not on the original
run: the production wrapper sees what Windows sees, the machine is not blind,
and the writer is still never named. The bench prints the verdict of both
checks and exits 4 when either fails: an "after" figure (ETW attribution) is
only comparable to a "before" figure produced by this same, checked instrument.
Check 3 (Restart Manager in isolation at fixed cadences) adds nothing once the
owner-thread probe passes, and is not rebuilt.

## Provenance

The original bench was lost with the development disk (commit `90dfb1b` never
reached GitHub). This version is rebuilt from the 2026-09-30 and 2026-10-01
reports: same pipeline, same six patterns, same arguments and output. The
2026-09-30 figures above were obtained with the original, checks 1–4 included.
