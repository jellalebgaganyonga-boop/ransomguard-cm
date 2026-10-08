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

## Provenance

The original bench was lost with the development disk (commit `90dfb1b` never
reached GitHub). This version is rebuilt from the 2026-09-30 and 2026-10-01
reports: same pipeline, same six patterns, same arguments and output. The
2026-09-30 figures above were obtained with the original. The original also
compared the production query with a by-the-book Restart Manager call
(0 cases where only the latter found a holder) and probed the holder from the
writer's own thread; those two instrument checks are not rebuilt here.
