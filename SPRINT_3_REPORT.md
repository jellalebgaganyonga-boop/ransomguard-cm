# RansomGuard-CM -- Sprint 3 Report: Real Ransomware Detection Engine

**Date:** 2026-05-18
**Version:** v0.5.0-detection-engine
**Modules delivered:** ENTROPY + GENEALOGY (modules 2 and 3 of 5)

---

## Summary

Sprint 3 transforms RansomGuard-CM from a tripwire (SENTINEL canaries) into a real ransomware detection engine. The ENTROPY module computes Shannon entropy on every file modification and fires multi-signal alerts when encryption activity is detected. The GENEALOGY module reconstructs the process tree of the offending process and maps suspicious parent-child relationships to MITRE ATT&CK techniques. Together, these modules answer: "Was this file encrypted?" (ENTROPY) and "Who did it and how?" (GENEALOGY).

---

## Detection Capabilities (3 of 5 modules operational)

| Module | Function | Sprint |
|--------|----------|--------|
| SENTINEL | Canary tripwire (5 formats, self-healing) | Sprint 2.x |
| **ENTROPY** | Shannon entropy analysis (4 detection rules) | **Sprint 3** |
| **GENEALOGY** | Process tree forensics (5 MITRE patterns) | **Sprint 3** |
| USB GUARD | USB insertion monitoring | Sprint 4 |
| EXFIL WATCH | Data exfiltration detection | Sprint 4 |

---

## ENTROPY Module

### Detection Rules
1. **AbsoluteHighEntropy** (Critical): Susceptible file (.docx/.pdf/.csv) with entropy > 7.5 and baseline < 6.0
2. **SuddenEntropyDelta** (High): Any file with delta > 2.5 and current entropy > 7.0
3. **DirectoryWideEntropyShift** (Critical): 5+ files in directory shift > 1.5 in 60 seconds
4. **WhitelistSuppression**: .zip/.jpg/.png/.mp4 etc. excluded (legitimately high entropy)

### Architecture
- `EntropyCalculator`: streaming I/O, 4KB buffers, sampling for files > 1MB
- `EntropyBaseline`: per-file baseline built at startup, persisted to SQLite
- `EntropyDetector`: 4-rule engine with configurable thresholds
- `EntropyMonitor`: BackgroundService with channel-based event pipeline

### Validated Entropy Ranges
- French medical text: 4.0-5.5 bits/byte
- AES-256 encrypted: 7.8-8.0 bits/byte
- Empty file: 0.0 bits/byte
- Legitimate edit: < 0.5 delta (no alert)

---

## GENEALOGY Module

### MITRE ATT&CK Patterns Detected
| Technique | Description | Severity |
|-----------|-------------|----------|
| T1490 | vssadmin delete shadows | Critical |
| T1490 | bcdedit disable recovery | Critical |
| T1059.001 | Office spawning PowerShell/cmd | High |
| T1027 | PowerShell -EncodedCommand | High |
| T1059 | Exe from %TEMP% or %APPDATA% | Medium |

### Architecture
- `ProcessSnapshotService`: System.Diagnostics + WMI for PID, PPID, command line
- `ProcessTree`: ancestor chain (max 10 levels) with summary
- `SuspiciousPatternDetector`: pattern matching on process trees
- `GenealogyEnricher`: links alerts to process trees via RestartManager

---

## Test Count

| Sprint | Tests |
|--------|-------|
| Sprint 2.6 (baseline) | 192 |
| Sprint 3 Day 1 (entropy calculator) | 204 |
| Sprint 3 Day 2 (detector rules) | 216 |
| Sprint 3 Day 3 (monitor + E2E) | 219 |
| Sprint 3 Day 4 (genealogy) | 231 |
| Sprint 3 Day 5 (enricher) | **231** |

**39 new tests added in Sprint 3.**

---

## Migrations Applied
1. `AddEntropyEntities` — EntropyBaseline + EntropyAlert tables
2. `AddGenealogyRecord` — GenealogyRecord table

---

## E2E Scenarios Validated

1. **Single file encryption**: French text file replaced with AES bytes -> Rule 1 fires (Critical)
2. **Legitimate edit**: Text appended to text file -> No alert
3. **Mass encryption**: 10 CSV files encrypted -> 5+ individual alerts + Rule 3 directory shift (Critical)

---

## Known Limitations

1. **Baseline build is synchronous at startup** — large directories (10K+ files) cause slow start
2. **No cross-module correlation yet** — SENTINEL and ENTROPY alerts fire independently. The "3-converging-signals" rule from the Design Doc requires Sprint 4+ integration
3. **GENEALOGY enrichment is best-effort** — fast-exiting processes may escape attribution
4. **WMI dependency** — System.Management is Windows-only (acceptable for Windows agent)
5. **No entropy for network-mounted files** — UNC paths blocked by PathValidator

---

## Sprint 4 Readiness

The detection engine foundation is complete:
- SENTINEL provides immediate tripwire
- ENTROPY provides mathematical encryption detection
- GENEALOGY provides forensic attribution
- All three share the same audit log, alert pipeline, and database

Sprint 4 targets: USB GUARD (removable media monitoring) and EXFIL WATCH (data exfiltration detection via network traffic patterns).
