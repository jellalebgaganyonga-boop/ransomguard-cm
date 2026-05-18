# ENTROPY Module — STRIDE Threat Model

## Components Under Analysis
1. EntropyCalculator (file I/O)
2. EntropyBaselineService (persistence)
3. EntropyDetector (rule evaluation)
4. EntropyMonitor (FileSystemWatcher)

## Threat Matrix

### Component: EntropyCalculator

| Threat | STRIDE | Severity | Description | Mitigation | CWE | NIST |
|--------|--------|----------|-------------|------------|-----|------|
| Path traversal to read sensitive files | Tampering | High | Configured WatchPath manipulated to read system files | PathValidator with canonical resolution | CWE-22 | SI-10 |
| TOCTOU between PathValidator and FileStream open | TOCTOU | Medium | File replaced between check and use | Open with FileShare.ReadWrite, document residual | CWE-367 | SI-7 |
| Large file DoS via slow disk | DoS | Medium | Attacker creates 10 GB file | MaxFileSizeForBaselineMB skip + sampling | CWE-400 | SC-5 |
| Memory exhaustion via deeply recursive directory | DoS | Low | Recursive scan symlink loop | Max depth + sampling strategy | CWE-674 | SC-5 |

### Component: EntropyBaselineService

| Threat | STRIDE | Severity | Description | Mitigation | CWE | NIST |
|--------|--------|----------|-------------|------------|-----|------|
| Baseline manipulation to hide encryption | Tampering | Critical | Attacker writes high baseline value before encrypting | Baseline frozen after alert (forensic preservation) + SQLCipher encrypted DB | CWE-345 | SI-7 |
| Baseline rebuild flooding | DoS | Medium | Force rebuild every minute | 7-day rebuild policy + rate limit | CWE-400 | SC-5 |

### Component: EntropyDetector

| Threat | STRIDE | Severity | Description | Mitigation | CWE | NIST |
|--------|--------|----------|-------------|------------|-----|------|
| Whitelist bypass via extension spoofing | Spoofing | High | Attacker renames .docx to .zip to suppress alert | Document residual; magic byte detection planned Sprint 4 | CWE-345 | SI-3 |
| Rule logic subversion via crafted input | Elevation | Low | Crafted entropy values to crash detector | Input validation + [0.0, 8.0] bounds check | CWE-20 | SI-10 |

### Component: EntropyMonitor

| Threat | STRIDE | Severity | Description | Mitigation | CWE | NIST |
|--------|--------|----------|-------------|------------|-----|------|
| Event flooding to exhaust queue | DoS | High | Generate 1M file events to overflow channel | Bounded channel 10K + drop-oldest + deduplicator | CWE-400 | SC-5 |
| Audit log leak of file content | Info Disclosure | Medium | Alert evidence includes file path | LogRedactor enricher (Sprint 2.5) | CWE-532 | AU-9 |
| FileSystemWatcher buffer overflow | DoS | Medium | Rapid file events exceed FSW internal buffer | Bounded channel absorbs burst; periodic polling backup | CWE-400 | SC-5 |

## Coverage Summary
- Total threats identified: 11
- Mitigated: 9
- Residual risk: 2 (TOCTOU file replace, extension spoofing)

## Compliance Mapping
- CWE Top 25: CWE-22, CWE-345, CWE-367, CWE-400, CWE-532, CWE-674
- NIST SP 800-53 Rev. 5: SI-3, SI-7, SI-10, SC-5, AU-9
- ISO/IEC 27002:2022: 8.7 (malware protection), 8.15 (logging)
- MITRE ATT&CK: T1486 (Data Encrypted for Impact)
