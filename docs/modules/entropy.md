# ENTROPY Module — Shannon Entropy Detection

**Module:** 2/5 innovation modules
**Version:** v0.5.0
**Status:** Operational

## What ENTROPY Does

ENTROPY monitors file modifications in real-time and computes Shannon entropy (information density) of changed files. When a plaintext medical document (entropy ~4.5 bits/byte) is suddenly replaced with encrypted content (entropy ~7.9 bits/byte), the entropy jump is detected and an alert fires. This is the standard academic approach to ransomware detection, validated by Scaife et al. 2016 (CryptoDrop) and Continella et al. 2016 (ShieldFS).

## Why Entropy Detection Works

Shannon entropy measures the average information content per byte. Text files, spreadsheets, and documents have structured, repetitive content with entropy in the 3.5-5.5 range. When ransomware encrypts these files using AES-256, the output is cryptographically random — entropy approaches the theoretical maximum of 8.0 bits/byte. This jump from ~4.5 to ~7.9 is mathematically unmistakable and cannot be hidden by the ransomware without weakening its encryption.

Formula: H = -sum(p(x) * log2(p(x))) for each byte value x with probability p(x).

## The Four Detection Rules

### Rule 1 — AbsoluteHighEntropy (Critical)
Fires when a text-like file (.docx, .pdf, .xlsx, .csv, .txt) has entropy > 7.5 AND its baseline was below 6.0. This means the file was readable and is now encrypted.

### Rule 2 — SuddenEntropyDelta (High)
Fires when any file's entropy increases by more than 2.5 bits/byte from baseline AND current entropy > 7.0. Catches encryption regardless of file type.

### Rule 3 — DirectoryWideEntropyShift (Critical) — **not evaluated (AGT-EN-002)**
It would fire when 5+ files in the same directory show an entropy increase > 1.5 within 60 seconds:
the strongest ransomware indicator, mass encryption of a directory. **`DirectoryShiftRule` and
`EntropyDetector.AnalyzeDirectoryShift` have no caller in the agent** — only the tests call them.
`EntropyMonitor` evaluates rules 1, 2 and 4. `DirectoryShiftThreshold`,
`DirectoryShiftWindowSeconds` and `DirectoryShiftMinFiles` below are read from the configuration
and change nothing: an administrator who tunes them tunes nothing. Mass encryption is therefore
seen as a series of single-file alerts (rules 1 and 2), not as one directory-wide signal.

### Rule 4 — ExtensionWhitelist (Suppression)
Prevents false positives on legitimately high-entropy files: .zip, .7z, .gz, .rar, .jpg, .jpeg, .png, .mp3, .mp4, .avi, .mkv. Evaluated first — if triggered, no other rules run.

## Configuration Reference

| Setting | Default | Description |
|---------|---------|-------------|
| Enabled | true | Enable/disable entropy detection |
| AbsoluteThreshold | 7.5 | Bits/byte threshold for Rule 1 |
| DeltaThreshold | 2.5 | Delta threshold for Rule 2 |
| DirectoryShiftThreshold | 1.5 | Directory average shift for Rule 3 |
| DirectoryShiftWindowSeconds | 60 | Time window for Rule 3 |
| DirectoryShiftMinFiles | 5 | Minimum files for Rule 3 |
| WhitelistedExtensions | .zip,.jpg,... | Extensions suppressed by Rule 4 |
| SusceptibleExtensions | .txt,.docx,... | Extensions eligible for Rule 1 |
| MaxFileSizeMB | 100 | Skip files larger than this |

## Baseline Build Process

At startup, ENTROPY scans all files in configured watch directories:
- Computes Shannon entropy for each file
- Stores as EntropyBaseline in SQLite (encrypted via SQLCipher)
- Rate-limited at 50 files/sec to prevent CPU spike
- Batched in groups of 100 with progress logging
- Idempotent: running twice does not create duplicates
- Skips SENTINEL canary files (0001_ prefix) and files > 100 MB
- Rebuilds after 7 days (configurable)

## Performance

- Entropy computation: < 10 ms for 1 MB file, < 100 ms for 100 MB (via sampling)
- Sampling: files > 1 MB read first 64KB + middle 64KB + last 64KB = 192KB
- Detection latency: < 100 ms from file modification to alert
- RAM overhead: < 10 MB for baseline tracking of 10,000 files

## Module posture

| State | When | Why |
|---|---|---|
| `disabled_by_config` | `Entropy.Enabled` false | Not asked to run. |
| `inactive / no_watch_directory` | none of the configured folders is watchable | It would see no write at all. |
| `degraded / attribution_unreliable` | **running (since 2026-10-22)** | It watches, computes Shannon entropy and fires rules 1, 2 and 4 — that part is real. Two gaps keep it from reading `active`: every signal carries **PID 0**, so no process is named (AGT-ATTR-001), and **rule 3 is not evaluated** (AGT-EN-002, above). |

It reads `active` when a signal names its writer and rule 3 is evaluated. Until then the console
shows "ENTROPY degraded: attribution unreliable", and the agent logs both gaps by their debt
identifiers at every start.

## Known Limitations

1. **Encrypted backups**: Legitimate encrypted backup files may trigger if extension is susceptible
2. **Extension spoofing**: Attacker renaming .docx to .zip bypasses Rule 4 (magic byte detection planned)
3. **Slow ransomware**: Encryption over days may cause baseline drift
4. **Pre-compressed files**: Already-compressed files show high baseline, delta detection still works
5. **One de-duplication filter per watcher** (V5, 2026-10-21): the 500 ms filter that folds a burst of
   events on one file is ENTROPY's own. Until then one instance was shared with the Worker (and SENTINEL,
   USB GUARD): within the window, the first watcher to handle a write takes the slot and the others do not
   get it. Measured honestly: the identity test is red on the old code (one instance); the loss itself was
   not observed in CI (`FileEventDeduplicationPerConsumerTests`: 20 writes with both watchers on the
   folder, 20/20 analysed on the old code, run 39652508279). Possible by construction, not seen.

## Academic References

- Scaife, N. et al. (2016). "CryptoDrop: Defending Against Ransomware Attacks." IEEE ICDCS.
- Continella, A. et al. (2016). "ShieldFS: A Self-healing, Ransomware-aware Filesystem." ACSAC.
- MITRE ATT&CK T1486: Data Encrypted for Impact.
