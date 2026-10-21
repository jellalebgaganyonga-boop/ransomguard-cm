# SENTINEL Module — Semantic Medical Canary Files

**Module:** SENTINEL (1/5 innovation modules)
**Version:** v0.4.0
**Status:** Operational

## What SENTINEL Does

SENTINEL deploys synthetic medical record files (canary files) in strategic locations across protected directories. These files:

- Look like legitimate hospital records (patient files, lab results, prescriptions)
- Sort alphabetically before real files (prefix `0001_`)
- Are monitored in real-time for any tampering
- Fire CRITICAL alerts when modified, deleted, or renamed
- Self-regenerate after deletion

When ransomware enumerates a directory and starts encrypting/renaming files, it hits canary files FIRST because of their alphabetical prominence. This gives SENTINEL sub-second detection before real patient data is touched.

## Configuration Reference

```json
{
  "Sentinel": {
    "Enabled": true,
    "CanariesPerDirectory": 3,
    "CheckIntervalMs": 1000,
    "WatchDirectories": [
      "%EACHUSERPROFILE%\\Desktop",
      "%EACHUSERPROFILE%\\Documents"
    ],
    "CanaryTemplates": [
      "dossier_patient",
      "analyses_laboratoire",
      "imagerie_medicale",
      "prescription_pharmacie",
      "rapport_consultation"
    ],
    "CanaryPrefix": "0001_"
  }
}
```

| Setting | Type | Range | Default | Description |
|---------|------|-------|---------|-------------|
| Enabled | bool | — | true | Enable/disable SENTINEL |
| CanariesPerDirectory | int | 1-10 | 3 | Canary files per directory |
| CheckIntervalMs | int | 100-10000 | 1000 | Periodic polling interval |
| WatchDirectories | string[] | min 1 | — | Directories to protect. `%EACHUSERPROFILE%` expands to one path per real user of the machine (see below) |
| CanaryTemplates | string[] | min 3 | — | Content templates |
| CanaryPrefix | string | — | "0001_" | Filename prefix |

### `%EACHUSERPROFILE%` (AGT-PATH-001)

The service runs as LocalSystem, where `%USERPROFILE%` is `C:\Windows\System32\config\systemprofile`: a path
written with it is **refused** (never watched, never created; the module reads `inactive / no_watch_directory`
if nothing else is left). `%EACHUSERPROFILE%` is the agent's own token: it expands to one path per real user
profile of the machine (`HKLM\...\ProfileList`, accounts `S-1-5-21-*` whose folder exists; LocalSystem,
LocalService, NetworkService and service SIDs are excluded). `Desktop` and `Documents` follow the user's
*User Shell Folders* (OneDrive or GPO redirections) when the user's hive is loaded; otherwise the default
folder under the profile is assumed and a Warning names the user and the folder. Profiles are read when the
module starts: a profile created afterwards is seen at the next agent restart (AGT-PATH-002).

## Response: suspending the writer (ADR-026)

```json
{
  "Sentinel": {
    "EtwEnabled": true,
    "Response": {
      "SuspendProcess": true,
      "EscalationMinutes": 60
    }
  }
}
```

When the attribution of a canary write is **resolved** (one writer seen by the kernel ETW session,
ADR-025), SENTINEL suspends that process with `NtSuspendProcess` -- every thread in one kernel call --
after re-reading its (PID, creation time) pair (50 ms tolerance). Never PID 0 or 4, never the agent
itself; never on an `unavailable` or `ambiguous` attribution: nobody is suspended on a guess. The
suspension is an alert the console shows (`SentinelProcessSuspended`, linked to the canary alert by
`response_to`), an audit entry before the call (`ProcessSuspendIntent`) and one after
(`ProcessSuspended` / `ProcessSuspendFailed`), and a row in `SuspendedProcesses`.

**Nothing resumes by itself.** Every `EscalationMinutes` without a decision, the alert
`SentinelProcessSuspendedNoDecision` repeats; a process that is gone closes its row as `Ended`. The
resume is a request (console command, J1 § 1.3): three attempts, then `ResumeFailed` and the process
stays suspended until the machine restarts (AGT-RESP-001).

Without a `Response` section the write is alerted and never answered, and the log says so at start;
with `SuspendProcess` true and no engine wired, SENTINEL does not start (`inactive / init_failed`).
Three distinct canaries written within 60 s with no process named raise `SentinelIsolationRecommended`
once per minute: the isolation itself does not exist (AGT-ISO-001).

**Honesty.** The suspension works when, and only when, ETW saw the write and one process made it.
Without ETW (no administrator rights, no session slot), for a deferred write carried by PID 4, or
when two processes write in the same 2 s window, the agent alerts and does **not** act. It is never
presented as guaranteed. A sync client or a restore that rewrites a canary would be suspended; the
resume exists for that, and no exclusion list is planned (an exclusion by image path is a bypass for
an attacker who names itself like the sync client).

## Templates

5 templates generate realistic French-language medical content:

1. **dossier_patient** — Patient medical record with history, treatment, follow-up notes
2. **analyses_laboratoire** — Complete blood count, biochemistry, parasitology results
3. **imagerie_medicale** — Radiology report (chest X-ray)
4. **prescription_pharmacie** — Medical prescription with dosage instructions
5. **rapport_consultation** — Clinical examination and diagnostic hypothesis

All content uses synthetic Cameroonian names and realistic medical data. Zero real PHI.

## Alert Types and Severities

| Alert Type | Severity | Trigger |
|-----------|----------|---------|
| CanaryModified | Critical | Content hash mismatch detected |
| CanaryDeleted | Critical | Canary file no longer exists |
| CanaryRenamed | Critical | Canary file renamed (common ransomware behavior) |

All alerts are:
- Persisted to SQLite database (CanaryAlerts table)
- Written to immutable hash-chained audit log (ANTIC compliance)
- Logged at Critical level via Serilog

## Process Attribution

When a canary is tampered with, SENTINEL attempts to identify the offending process:
- Process ID (PID)
- Process name
- Executable path

**Limitation:** Full file handle attribution requires kernel-level APIs (ETW, NtQuerySystemInformation). Current implementation uses best-effort process enumeration. Production will use ETW tracing for accurate attribution.

## Performance

- RAM overhead: < 5 MB for canary tracking
- CPU overhead: < 0.1% for periodic polling at 1000ms interval
- Detection latency: < 100ms (real-time via FileSystemWatcher)
- File size per canary: ~1.5-3 KB

## Known Limitations

1. **Text format only**: Canaries use .txt format. Real .docx/.pdf generation planned for future sprint.
2. **Process attribution**: Best-effort, not guaranteed. ETW integration needed for production accuracy.
3. **No canary rotation**: Canaries are static after deployment. Periodic content refresh planned.
4. **Directory scope**: Monitors configured directories only, not entire volume.
