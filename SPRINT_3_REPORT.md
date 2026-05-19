# Sprint 3 Report — Detection Engine Operational

**Version:** v0.5.1-zero-debt
**Date:** 2026-05-18
**Validation Date:** 2026-05-18
**Closure Date:** 2026-05-18

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

## Validation Gates

### Test Coverage

| Category | Tests |
|----------|-------|
| EntropyCalculator | 12 |
| EntropyDetector (monolithic) | 16 |
| DetectionRules (separate) | 14 |
| EntropyBaseline | 11 |
| EntropyDetectionE2E | 3 |
| ProcessTree / ProcessSnapshot | 8 |
| SuspiciousPattern (original 5 + case/edge) | 11 |
| AdditionalPatterns (T1566, T1218) | 6 |
| GenealogyEnricher Stress (handle leak) | 2 |
| RansomwareSimulationE2E | 3 |
| **Sprint 3 New** | **86** |
| **Grand Total** | **278** |

### Performance SLO (measured 2026-05-18)

| Metric | Measured | SLO Target | Result |
|--------|----------|------------|--------|
| Max RAM | 155.75 MB | 200 MB | PASS |
| Avg RAM | 153.12 MB | — | — |
| Avg CPU | 0% | 5% | PASS |
| Handle Variance | 17 | 50 | PASS |
| Handle Range | 745 — 762 | — | — |
| Avg Threads | 27 | — | — |

All SLOs: **PASS**

### E2E Ransomware Simulation

| Scenario | Result | Latency |
|----------|--------|---------|
| Full pipeline (10 files encrypted, alerts fire) | PASS | ~1s |
| Legitimate edits (no false positives) | PASS | ~91ms |
| Whitelisted format (no alert on .zip) | PASS | ~3s |

### STRIDE Threat Models

- `docs/security/entropy-stride.md` — **13 threats** analyzed
- `docs/security/genealogy-stride.md` — **13 threats** analyzed

### Validation Item Confirmations

| Item | Status | Evidence |
|------|--------|----------|
| 5a — PathValidator in GenealogyEnricher | CONFIRMED | `GenealogyEnricher.cs:40-46` |
| 5b — ConstantTimeComparison audit | CLEAN | No SequenceEqual in Entropy/Genealogy detection |
| 5c — Rate limiting wired | CONFIRMED | `EntropyBaselineService.cs:81` (50/sec), `EntropyMonitor.cs:224` (100/sec) |
| 5d — Handle leak test | PASS | Variance < 50 after 100 WMI operations |
| 5e — Audit log Ed25519 verification | CONFIRMED | `--verify-audit-log` CLI: hash chain intact, Ed25519 valid |

## Files Created / Modified

### New in Validation
- `agent/src/RansomGuard.Agent.Service/ServiceRegistration.cs` — shared DI setup
- `agent/src/RansomGuard.Agent.Tests/EndToEnd/RansomwareSimulationE2E.cs` — 3 E2E tests
- `agent/src/RansomGuard.Agent.Tests/Genealogy/GenealogyEnricherStressTests.cs` — handle leak stress tests
- `docs/benchmarks/sprint-3-perf-validation.md` — SLO report with real numbers
- `docs/benchmarks/sprint-3-perf-samples.csv` — 60 metric samples

### Detection/Entropy/
- `IEntropyCalculator.cs` — interface (file + buffer overloads)
- `EntropyCalculator.cs` — streaming IO, sampling, stackalloc
- `EntropyCalculatorOptions.cs` — configurable buffer/sampling sizes
- `EntropyDetector.cs` — monolithic 4-rule engine
- `EntropyBaselineService.cs` — rate-limited baseline build (50 files/sec)
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
- `GenealogyEnricher.cs` — RestartManager + tree + patterns + PathValidator
- `Patterns/IPatternRule.cs` — modular rule interface
- `Patterns/EmailAttachmentRule.cs` — T1566
- `Patterns/SignedBinaryProxyRule.cs` — T1218

### Service/
- `EntropyMonitor.cs` — BackgroundService with channel pipeline + rate limiting (100/sec)
- `ServiceRegistration.cs` — shared DI configuration for tests
- `Program.cs` — added `--verify-audit-log` CLI command

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

## Known Limitations

1. **PID reuse race window**: Small window where PID reuse could misattribute
2. **Extension spoofing**: Whitelist bypass via renaming (magic bytes planned Sprint 4)
3. **Slow ransomware**: Encryption over days may cause baseline drift
4. **WinTrust not yet integrated**: Signature verification uses process name only

## Verdict: Sprint 3 is COMPLETE

- 278 tests, 0 warnings, 0 errors
- All 3 SLOs PASS (RAM 155.75 MB, CPU 0%, handle variance 17)
- E2E ransomware simulation: PASS (< 5s latency)
- STRIDE: 13 threats per module (26 total)
- All 5 validation confirmations verified with file:line evidence
- 7/7 MITRE ATT&CK patterns implemented

## Sprint 3.6 Closure — Zero Technical Debt

8 gaps identified in the Sprint 3 evidence audit, all closed:

| Gap | Resolution | Evidence |
|-----|-----------|----------|
| 1. Unified cross-module E2E | UnifiedRansomwareAttackE2E.cs: 50 files + SENTINEL + ENTROPY + GENEALOGY in one test | PASS (2s) |
| 2. Formal TokenBucketRateLimiter | IOperationRateLimiter + TokenBucketRateLimiter wrapping BCL, 8 tests | 8 tests PASS |
| 3. 1000-iteration stress tests | Extended from 100 to 1000 with [Trait("Category", "Stress")] | 3 stress tests |
| 4. vssadmin live process test | MitreT1490LiveProcessTests: real snapshot + synthetic T1490 detection | 3 tests PASS |
| 5. Audit log real entries | AuditLogVerificationWithEntriesTests: 10 entries + tamper detection | 4 tests PASS |
| 6. Performance under load | 30 file encryptions during measurement: RAM 158.4MB, CPU 11.42%, handles var 17 | All SLOs PASS |
| 7. Channel saturation test | 4 tests verifying DropOldest behavior, concurrent writers, no crash | 4 tests PASS |
| 8. Migration rollback test | Apply, rollback to pre-Sprint 3, re-apply: all clean | 4 tests PASS |

### Final Test Count: 300 (non-stress) + 3 stress = 303

### Performance Under Load

| Metric | SLO | Idle | Under Load | Status |
|--------|-----|------|------------|--------|
| Max RAM | 200 MB | 155.75 MB | 158.4 MB | PASS |
| Avg CPU | 5% idle / 15% load | 0% | 11.42% | PASS |
| Handle Variance | 50 idle / 100 load | 17 | 17 | PASS |

Sprint 4 can start upon user approval.
