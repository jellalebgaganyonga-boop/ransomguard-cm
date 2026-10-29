# EtwFileIoProbe — what a kernel FileIO session costs and carries

## What this is

A **measurement instrument** for the ETW design gate (`SPRINT_8_ARBITRAGE_LOT3.md`, B.2). It
answers with numbers, before any ETW attribution code is written:

1. the volume of FileIO events per second, at rest and during a file copy;
2. the CPU of the process that reads the session, and the CPU of the agent as it is today on
   the same copy;
3. which events carry a PID and a file name, per keyword set (`FileIOInit`, `+DiskFileIO`,
   `+FileIO`);
4. whether that PID is the process that did the operation (a wrong PID is worse than none),
   and whether the name of a file opened **before** the session is resolved;
5. without administrator rights: the kernel session is refused, and the `FileSystemWatcher`
   SENTINEL already uses still fires.

Plus the ETW real-time delivery delay (event timestamp to callback) and the cost of a canary
filter per event, on the file names actually seen.

It is **outside `RansomGuard.Agent.sln`**: the agent's CI neither builds nor runs it, and no
product code depends on it. It only touches files it creates under `%TEMP%\rg-etw-probe` (or
`RG_PROBE_WORK`): 20 canaries, 500 files of 64 KB to copy, and a copy destination. Its
"encryption" is an XOR over its own canaries, to reproduce the I/O shape of pattern 3.

## How to run it

The measurement needs administrator rights (a kernel session):

```
dotnet build agent/tools/EtwFileIoProbe -c Release
EtwFileIoProbe measure --seconds 60 [--agent-pid <PID of a running agent>] [--report report.md]
EtwFileIoProbe no-admin
```

`measure` runs a baseline without any session, then one session per keyword set; each phase
is 60 s at rest, then 60 s of copy. The copy is done by a child process (`writer`): it copies
the 500 files in a loop and, on each pass, rewrites one canary XOR-ed, renames it to
`.locked`, deletes it and puts it back. Its PID is the one attribution must return.

On a CI runner: `.github/workflows/etw-probe.yml` (review branch `review/adr-025-etw` only),
with the agent of the same commit running and watching the probe's directory. The report is
the job summary and the `etw-probe` artifact.

## Limits, said up front

- A CI runner is not a hospital workstation: no user, no third-party antivirus, a fast disk,
  4 vCPUs. The volume at rest is a floor, not an estimate for a busy workstation.
- The copy is synthetic and deliberately heavy (unthrottled): it gives an upper bound of the
  event rate a copy produces, not a typical one.
- CPU is measured on the probe's own process. TraceEvent's parsing is what an agent would pay;
  the probe does nothing else with the events but count them.
