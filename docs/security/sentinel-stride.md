# SENTINEL Module — STRIDE Threat Model

**Date:** 2026-05-18
**Author:** Claude Opus 4.6 (audit), supervised by Jella Lebga
**Standard References:** CWE Top 25 (2024), OWASP ASVS 4.0.3, NIST SP 800-53 Rev.5

## Components Under Analysis

1. CanaryFileService (creates files)
2. SentinelDeploymentService (orchestrates deployment)
3. SentinelMonitor (detects tampering)
4. CanaryContentGenerator (synthesizes content)
5. RestartManagerHelper (process attribution)
6. AuditLog persistence layer

## Threats Matrix

### Spoofing

| ID | Component | Threat | Severity | Mitigation | Status | CWE | NIST |
|----|-----------|--------|----------|------------|--------|-----|------|
| S1 | CanaryFileService | Attacker creates fake canary to mask real attack | Medium | Canaries tracked in DB with hash — unknown files ignored | Mitigated | CWE-290 | IA-3 |
| S2 | AuditLog | Attacker spoofs audit entries | High | Hash chain + Ed25519 signing (planned) prevents insertion | Partial | CWE-345 | AU-10 |

### Tampering

| ID | Component | Threat | Severity | Mitigation | Status | CWE | NIST |
|----|-----------|--------|----------|------------|--------|-----|------|
| T1 | CanaryFileService | Path traversal in canary destination | High | PathValidator with canonical resolution | Mitigated | CWE-22 | SI-10 |
| T2 | CanaryFileService | Symlink attack on canary creation | Medium | Directory ownership check at startup | Accepted Risk | CWE-59 | AC-3 |
| T3 | SentinelMonitor | Attacker modifies database to mark canaries as Deleted | High | SQLCipher encryption + DACL on db file | Mitigated | CWE-311 | SC-28 |
| T4 | CanaryContentGenerator | Attacker reverse-engineers canary patterns | Medium | Randomized content per generation, no fixed template | Mitigated | — | — |
| T5 | AuditLog | Tamper with audit entries | Critical | SHA-256 hash chain verified on read | Mitigated | CWE-354 | AU-10 |

### Repudiation

| ID | Component | Threat | Severity | Mitigation | Status | CWE | NIST |
|----|-----------|--------|----------|------------|--------|-----|------|
| R1 | AuditLog | Deny having tampered with canary | High | Hash-chained audit log with timestamps | Mitigated | CWE-778 | AU-3 |
| R2 | CanaryAlert | Deny process was responsible | Medium | Restart Manager API attribution | Partial | — | AU-3 |

### Information Disclosure

| ID | Component | Threat | Severity | Mitigation | Status | CWE | NIST |
|----|-----------|--------|----------|------------|--------|-----|------|
| I1 | Database | Patient-like data exposed from canaries | Medium | All content is synthetic, zero real PHI | Mitigated | CWE-200 | SC-28 |
| I2 | Logs | Sensitive paths/data in logs | Medium | LogRedactionEnricher applied to all sinks | Mitigated | CWE-532 | AU-9 |
| I3 | Database | SQLite file readable without encryption | High | SQLCipher AES-256 with DPAPI key | Mitigated | CWE-311 | SC-28 |

### Denial of Service

| ID | Component | Threat | Severity | Mitigation | Status | CWE | NIST |
|----|-----------|--------|----------|------------|--------|-----|------|
| D1 | SentinelMonitor | Flood canary events to exhaust resources | Medium | Bounded channel (10K), drop-oldest policy | Mitigated | CWE-400 | SC-5 |
| D2 | SentinelDeploymentService | Infinite regeneration loop | High | Anti-loop: max regen/hour, SustainedAttack alert | Mitigated | CWE-674 | SC-5 |
| D3 | RestartManagerHelper | Slow Restart Manager API blocks detection | Medium | Timeout on attribution, non-blocking | Mitigated | CWE-400 | SC-5 |

### Elevation of Privilege

| ID | Component | Threat | Severity | Mitigation | Status | CWE | NIST |
|----|-----------|--------|----------|------------|--------|-----|------|
| E1 | Service | Non-admin stops protection service | High | DACL security descriptor on service | Mitigated | CWE-269 | AC-6 |
| E2 | Database | Non-admin modifies database | High | File-level DACL + SQLCipher | Mitigated | CWE-732 | AC-3 |
| E3 | Keys | Non-admin reads encryption key | High | DPAPI LocalMachine scope + DACL on key file | Mitigated | CWE-522 | SC-12 |

## Coverage Summary

- Total threats identified: 18
- Mitigated: 15
- Partially mitigated: 2 (S2: Ed25519 planned, R2: attribution best-effort)
- Accepted residual risk: 1 (T2: symlink attack)
- Deferred: Ed25519 audit signing (Sprint 2.5 scope was too large for full implementation), Authenticode signing (Sprint 8)
