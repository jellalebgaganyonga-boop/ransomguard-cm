# Sprint 3 Report — Detection Engine Operational

**Version:** v0.5.0-detection-engine
**Date:** 2026-05-18

## Summary

Sprint 3 transitioned the product from canary tripwire to real ransomware detection engine. ENTROPY (Shannon entropy with 4 detection rules and per-file baseline tracking) and GENEALOGY (process tree forensics with 7 MITRE ATT&CK patterns) are operational and enrich all alerts.

## Modules Status

| # | Module | Status |
|---|--------|--------|
| 1 | SENTINEL | Operational (Sprint 2.x) |
| 2 | ENTROPY | Operational (Sprint 3) |
| 3 | GENEALOGY | Operational (Sprint 3) |
| 4 | USB GUARD | Pending (Sprint 4) |
| 5 | EXFIL WATCH | Pending (Sprint 4) |

## Files Created

### Detection/Entropy/
- `IEntropyCalculator.cs` — interface (file + buffer overloads)
- `EntropyCalculator.cs` — streaming IO, sampling, stackalloc
- `EntropyCalculatorOptions.cs` — configurable buffer/sampling sizes
- `EntropyDetector.cs` — monolithic 4-rule engine
- `EntropyBaselineService.cs` — rate-limited baseline build
- `IEntropyBaselineService.cs` — baseline interface
- `EntropyEvent.cs` — channel event record
- `DetectionRules/IEntropyRule.cs` — rule interface + context
- `DetectionRules/AbsoluteThresholdRule.cs` — Rule 1
- `DetectionRules/DeltaThresholdRule.cs` — Rule 2
- `DetectionRules/DirectoryShiftRule.cs` — Rule 3
- `DetectionRules/ExtensionWhitelistRule.cs` — Rule 4

### Detection/Genealogy/
- `ProcessSnapshot.cs` — process forensic record
- `ProcessTree.cs` — ancestor chain + summary
- `IProcessSnapshotService.cs` — capture interface
- `ProcessSnapshotService.cs` — WMI + System.Diagnostics
- `SuspiciousPatternDetector.cs` — 5 inline patterns
- `IGenealogyEnricher.cs` — enricher interface
- `GenealogyEnricher.cs` — RestartManager + tree + patterns
- `Patterns/IPatternRule.cs` — modular rule interface
- `Patterns/EmailAttachmentRule.cs` — T1566
- `Patterns/SignedBinaryProxyRule.cs` — T1218

### Persistence/
- `Entities/EntropyBaseline.cs`
- `Entities/EntropyAlert.cs`
- `Entities/GenealogyRecord.cs`
- `Repositories/IEntropyBaselineRepository.cs`
- `Repositories/EntropyBaselineRepository.cs`

### Service/
- `EntropyMonitor.cs` — BackgroundService with channel pipeline

### Migrations
- `AddEntropyEntities` — EntropyBaseline + EntropyAlert tables
- `AddGenealogyRecord` — GenealogyRecord table

## Test Coverage

| Category | Tests |
|----------|-------|
| EntropyCalculator | 12 |
| EntropyDetector (monolithic) | 12 |
| DetectionRules (separate) | 14 |
| EntropyBaseline | 8 |
| EntropyDetectionE2E | 3 |
| ProcessTree | 5 |
| SuspiciousPattern (original 5) | 7 |
| AdditionalPatterns (T1566, T1218) | 6 |
| **Sprint 3 New** | **67** |
| **Grand Total** | **259** |

## MITRE ATT&CK Coverage

| Pattern | Technique ID | Severity | Status |
|---------|-------------|----------|--------|
| Office Macro Abuse | T1059.001 | High | Implemented |
| Phishing Attachment | T1566 | High | Implemented |
| Signed Binary Proxy | T1218 | Medium | Implemented |
| Vssadmin Delete Shadows | T1490 | CRITICAL | Implemented |
| Bcdedit Recovery Disable | T1490 | CRITICAL | Implemented |
| PowerShell EncodedCommand | T1027 | High | Implemented |
| Unsigned Exe in Temp | T1059 | High | Implemented |

**7/7 mandatory patterns implemented.**

## STRIDE Threat Models
- `docs/security/entropy-stride.md` — 11 threats analyzed
- `docs/security/genealogy-stride.md` — 11 threats analyzed

## Documentation
- `docs/modules/entropy.md` — complete module documentation
- `docs/modules/genealogy.md` — complete module documentation
- `docs/testing/sprint-3-manual-tests.md` — 5 manual test procedures
- `scripts/sprint-3-performance-validation.ps1` — SLO validation script

## E2E Scenarios Validated

1. Single file encryption: French text -> AES bytes -> Rule 1 fires (Critical)
2. Legitimate edit: text appended -> No alert
3. Mass encryption: 10 CSV files encrypted -> Rule 3 directory shift (Critical)

## Known Limitations

1. **PID reuse race window**: Small window where PID reuse could misattribute
2. **Extension spoofing**: Whitelist bypass via renaming (magic bytes planned)
3. **Slow ransomware**: Encryption over days may cause baseline drift
4. **WinTrust not yet integrated**: Signature verification uses process name only

## Sprint 4 Readiness: YES

All deliverables complete. 259 tests. 0 warnings. 0 errors. 7/7 MITRE patterns. STRIDE models documented. Module documentation written.

Next: Sprint 4 (USB GUARD + EXFIL WATCH — modules 4 and 5 of 5).
