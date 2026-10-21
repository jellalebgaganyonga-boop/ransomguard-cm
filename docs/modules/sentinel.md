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
