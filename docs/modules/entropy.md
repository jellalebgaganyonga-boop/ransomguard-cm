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

### Rule 3 — DirectoryWideEntropyShift (Critical)
Fires when `DirectoryShiftMinFiles` (5) **distinct** files in the same directory show an average
entropy increase above `DirectoryShiftThreshold` (1.5) within `DirectoryShiftWindowSeconds` (60):
the strongest ransomware indicator, mass encryption of a directory.

`DirectoryEntropyWindow` keeps the count. Two properties of it matter:

- **Distinct files, not writes.** One document rewritten twenty times by a noisy application is one
  file and can never reach the threshold on its own. Mass encryption is about breadth.
- **One alert per burst.** After the rule fires for a directory it goes quiet for the length of the
  window, so fifty encrypted files raise one Critical naming the directory, not fifty that would
  bury it. A second wave after the window is reported again.

The directory-wide alert names the writing process when every attributed file in the window agrees
on one PID; files nobody could attribute do not contradict it, two different writers do, and then
nobody is named.

The three configuration values are read and act: an administrator who tunes them tunes the rule.

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
| `active` | running **with the kernel FileIO session up** | It watches, computes Shannon entropy, evaluates rules 1–4, and an alert names the process that wrote the file. |
| `degraded / attribution_unreliable` | running **without the kernel FileIO session** | Detection and all four rules still run; no alert can name its writer, so the console must not read green (AGT-ATTR-001). |

The state follows the session at runtime, not the build: if the session is stopped from outside
(`logman stop`, an anti-EDR tool) the module drops to `degraded` on the next alert, and returns to
`active` when the session's restart succeeds.

**How a writer is named.** The session is SENTINEL's — one per machine, shared through
`FileIoFanout` rather than opening a second one. `FileWriteAttributionCache` keeps the write
records of the watched folders for two minutes, so ENTROPY can still ask "who wrote this" after it
has read and hashed the file, by which time the writer has closed its handle. (The Restart Manager,
which GENEALOGY uses, only reports processes *currently holding* a file, so for the write-and-close
that ransomware does it reports nobody — that is why it was never enough.)

The refusals are reported as precisely as the answers, because a wrong PID is worse than no PID:
`system_pid` (the write was PID 0 or 4), `pid_reused`, `process_exited`, `no_write_event`,
`etw_unavailable`, or `ambiguous` when two processes wrote the same file in the window. The console
shows each of these differently rather than implying the file wrote itself.

**Needs administrator rights.** The kernel session needs them, and a free system-logger slot
(Windows allows eight). Without either, ENTROPY runs degraded and SENTINEL's log says which is
missing.

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
