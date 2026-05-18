# GENEALOGY Module — STRIDE Threat Model

## Components Under Analysis
1. ProcessSnapshotService (WMI, System.Diagnostics)
2. ProcessTreeBuilder
3. SuspiciousPatternDetector (7 MITRE patterns)
4. GenealogyEnricher

## Threat Matrix

### Component: ProcessSnapshotService

| Threat | STRIDE | Severity | Description | Mitigation | CWE | NIST |
|--------|--------|----------|-------------|------------|-----|------|
| PID reuse: report wrong process | Spoofing | High | Windows reuses PIDs after exit | Capture timestamp + start time comparison | CWE-672 | SI-7 |
| Process name spoofing | Spoofing | High | Malware renamed to winword.exe | ExecutablePath check + signature verification | CWE-345 | SI-7 |
| WMI handle leak | DoS | Medium | ManagementObjectSearcher not disposed | All in using blocks + stress test | CWE-404 | SC-5 |
| WMI query timeout | DoS | Medium | Slow WMI service blocks detection | 5-second timeout on enrichment | CWE-400 | SC-5 |
| Access denied on system processes | DoS | Low | Cannot capture system PIDs (PID 4, csrss) | Catch AccessDeniedException, log and continue with partial tree | CWE-280 | AC-6 |

### Component: ProcessTreeBuilder

| Threat | STRIDE | Severity | Description | Mitigation | CWE | NIST |
|--------|--------|----------|-------------|------------|-----|------|
| Infinite ancestor loop via PID reuse | DoS | High | Parent chain cycles | Max depth 10 + cycle detection | CWE-835 | SC-5 |
| Memory exhaustion via fan-out | DoS | Medium | Process with 10K children | 1-level child capture only | CWE-400 | SC-5 |

### Component: SuspiciousPatternDetector

| Threat | STRIDE | Severity | Description | Mitigation | CWE | NIST |
|--------|--------|----------|-------------|------------|-----|------|
| Pattern bypass via command line obfuscation | Spoofing | High | "vss admin delete shadows" with spaces | String.Contains matching (residual risk documented) | CWE-176 | SI-3 |
| False negative on case variation | Spoofing | Medium | "VSSADMIN" vs "vssadmin" | StringComparison.OrdinalIgnoreCase | CWE-178 | SI-10 |

### Component: GenealogyEnricher

| Threat | STRIDE | Severity | Description | Mitigation | CWE | NIST |
|--------|--------|----------|-------------|------------|-----|------|
| Command line credential disclosure | Info Disclosure | Critical | password="abc123" stored verbatim | LogRedactor pattern detection | CWE-532 | AU-9 |
| Enrichment hangs detection pipeline | DoS | High | Slow WMI query | 5-second timeout + fire-and-forget | CWE-400 | SC-5 |
| Alert tampering via direct DB write | Tampering | High | Modify GenealogyRecord | Audit log Ed25519 signed + SQLCipher | CWE-345 | SI-7 |
| Race condition on concurrent enrichment | Tampering | Medium | Two alerts enriched simultaneously share DbContext | Scoped DI lifetime + SaveChanges per alert | CWE-362 | SI-7 |

## Coverage Summary
- Total threats identified: 13
- Mitigated: 11
- Residual risk: 2 (PID reuse race window, obfuscated command lines)

## MITRE ATT&CK Coverage
| Technique | ID | Status |
|-----------|------|--------|
| Office Macro Abuse | T1059.001 | Implemented |
| Phishing Attachment | T1566 | Implemented |
| Signed Binary Proxy | T1218 | Implemented |
| Inhibit Recovery (vssadmin) | T1490 | Implemented |
| Inhibit Recovery (bcdedit) | T1490 | Implemented |
| Obfuscated PowerShell | T1027 | Implemented |
| Unsigned Exe in TEMP | T1059 | Implemented |

## Compliance Mapping
- CWE Top 25: CWE-176, CWE-178, CWE-280, CWE-345, CWE-362, CWE-400, CWE-404, CWE-532, CWE-672, CWE-835
- NIST SP 800-53 Rev. 5: AC-6, SI-3, SI-7, SI-10, SC-5, AU-9
- ISO/IEC 27002:2022: 8.16 (monitoring), 8.15 (logging)
