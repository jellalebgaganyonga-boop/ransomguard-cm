# USB GUARD Module — STRIDE Threat Model

## Components Under Analysis

1. **UsbDeviceMonitor** — WMI event subscriber (`Win32_DiskDrive` arrival via `ManagementEventWatcher`) plus SetupAPI for device metadata extraction. Runs as a BackgroundService consuming device events through a bounded channel.
2. **UsbContentScanner** — Filesystem-level scanning engine that orchestrates MagicByteValidator, AutorunInfDetector, SuspiciousLnkDetector, ArchiveScanner, and UsbEntropyScanner. Iterates files on mounted USB volumes with configurable size limits and rate limiting.
3. **UsbWhitelistService** — Persistence layer for approved USB device serial numbers. Uses HMAC-SHA256 with a salt derived from the database encryption key. Comparison uses `CryptographicOperations.FixedTimeEquals` (CWE-208 mitigation).
4. **QuarantineService** — Moves suspicious files to an encrypted quarantine vault with DACL-protected directory structure. Files are AES-256-GCM encrypted before storage.
5. **UsbActionEngine** — Coordinates response actions: logging, quarantine, device eject via CM_Request_Device_Eject P/Invoke. Severity-based decision matrix similar to ExfilActionEngine.

## Threat Matrix

### Component: UsbDeviceMonitor

| Threat | STRIDE | Severity | Description | Mitigation | CWE | NIST |
|--------|--------|----------|-------------|------------|-----|------|
| Serial number spoofing for whitelist entry | Spoofing | High | An attacker programs a USB microcontroller (e.g., Rubber Ducky, Digispark) with a cloned serial number matching a whitelisted device. The WMI `Win32_DiskDrive.SerialNumber` property reflects firmware-reported values, which are trivially forgeable. Once spoofed, the device bypasses all content scanning because `UsbWhitelistService.IsWhitelisted` returns true based solely on the HMAC of the serial. The mitigation applies composite identity: serial + vendor ID + product ID are hashed together, making spoofing require matching all three fields. Residual risk remains for commodity USB drives with identical vendor/product IDs across units. | Composite device fingerprint (serial + VID + PID), HMAC-SHA256 hash comparison via FixedTimeEquals | CWE-345 | SI-7 |
| USB plug flood DoS | DoS | High | An attacker rapidly connects and disconnects USB devices (or uses a USB hub with a programmable controller) to generate thousands of WMI `__InstanceCreationEvent` notifications per second. Each event creates a new `IServiceScope`, queries the database for whitelist status, and potentially triggers a full content scan. Without throttling, this exhausts thread pool resources and starves other BackgroundServices. The mitigation uses a bounded channel (10,000 capacity, DropOldest) as the intake buffer, combined with per-device rate limiting via `TokenBucketRateLimiter`. Device events beyond the rate limit are logged but not processed, preventing resource exhaustion while maintaining audit trail. | Bounded channel + TokenBucketRateLimiter + per-device deduplication within 5-second window | CWE-400 | SC-5 |
| Connection without audit trace | Repudiation | High | If a USB device connects and the agent service crashes before the audit log entry is persisted, the attacker has an unlogged window to exfiltrate data. The WMI event fires, the channel receives the event, but the scoped `IAuditLogRepository.AppendAsync` call may not complete before an unhandled exception terminates the consumer loop. The mitigation persists the audit entry as the first action inside the event handler, before any scanning occurs. The Ed25519-signed hash chain ensures post-hoc detection of any gap in the audit sequence. Additionally, the `VerifyChainIntegrityAsync` method detects missing entries on the next startup. | Audit-first pattern: AppendAsync before scan; Ed25519 chain integrity check on startup | CWE-778 | AU-2 |

### Component: UsbContentScanner

| Threat | STRIDE | Severity | Description | Mitigation | CWE | NIST |
|--------|--------|----------|-------------|------------|-----|------|
| Magic byte parser buffer overflow | Elevation | High | The `MagicByteValidator` reads the first 512 bytes of each file to identify its true format. A crafted file could exploit a buffer overread if the validator compares signature bytes beyond the actual bytes read. For example, a file smaller than the expected magic signature length could cause an `IndexOutOfRangeException` or, in unsafe contexts, read adjacent memory. The mitigation uses `ReadOnlySpan<byte>` slicing with explicit length checks (`header.Length >= sig.Length`) before comparison, preventing any out-of-bounds access. All comparisons are safe managed code — no `unsafe` blocks or pointer arithmetic exist in the scanner. | Span-based slicing with length guard before comparison; no unsafe code | CWE-119 | SI-10 |
| Zip bomb DoS | DoS | Medium | An attacker places a specially crafted ZIP archive on a USB device where the compressed payload is 1 KB but decompresses to 1 TB (quine-based recursive compression). The `ArchiveScanner` extracts archive entries for content inspection, and without limits, could exhaust disk space and memory. The mitigation enforces three layers of protection: `MaxFileSizeForScanMB` (default 100 MB) caps individual entry extraction, `MaxScanDurationSeconds` (default 120s) aborts long-running scans, and a total extracted bytes counter terminates scanning when aggregate extraction exceeds 500 MB. Nested archives are limited to 2 levels of recursion depth. | MaxFileSizeForScanMB + MaxScanDurationSeconds + aggregate extraction cap + recursion depth limit (2 levels) | CWE-409 | SC-5 |
| Path traversal in archive | Tampering | High | A ZIP archive contains entries with relative paths like `../../Windows/System32/malware.dll`. When `ArchiveScanner` extracts entries to a temporary directory for scanning, a naive `Path.Combine(tempDir, entry.FullName)` could write outside the intended directory. The mitigation canonicalizes the output path via `Path.GetFullPath` and verifies it starts with the temp directory prefix. Any entry whose resolved path escapes the extraction root is skipped and logged as suspicious. This matches the `PathValidator.Validate` pattern used throughout the codebase for CWE-22 defense. | Path canonicalization + prefix check; PathValidator pattern for CWE-22 | CWE-22 | SI-10 |
| USB content paths in logs | Info Disclosure | Medium | When the scanner detects suspicious files, log messages include the full file path (e.g., `E:\Patient Records\John Doe - MRI Results.pdf`). In a healthcare environment, file paths may contain Protected Health Information (PHI). The structured logging via Serilog captures these paths in both console and file sinks. The mitigation uses the global `LogRedactor` enricher (Sprint 2.5) that applies regex-based redaction to file paths in log output, replacing patient-identifiable segments while preserving directory structure for forensic utility. Audit log entries store a SHA-256 hash of the path rather than the cleartext. | LogRedactor global enricher; path hashing in audit entries | CWE-532 | AU-9 |
| LNK Stuxnet-style exploit | Code Exec | Critical | A specially crafted `.lnk` shortcut file on a USB device exploits Windows Shell link parsing to execute arbitrary code when the file is merely displayed in Explorer (CVE-2010-2568 pattern). The `SuspiciousLnkDetector` parses `.lnk` files to extract the target path and arguments, detecting suspicious patterns like PowerShell invocations, `cmd.exe /c` chains, or paths pointing to non-standard executables. However, the mere act of mounting the USB volume exposes Explorer to the exploit before the agent can quarantine. The mitigation scans `.lnk` files with highest priority (sorted first in scan order) and issues an immediate eject recommendation if a suspicious link target is detected. Residual risk: zero-day LNK exploits that fire before scan completes. | SuspiciousLnkDetector with priority scanning; immediate eject on suspicious target; CWE-22 path validation on link targets | CWE-22 | SI-3 |
| Office macro hidden in document | Exec | High | A USB-delivered Office document (`.docx`, `.xlsx`, `.pptx`) contains VBA macros that execute on open. The `MagicByteValidator` identifies Office Open XML files by their ZIP + `[Content_Types].xml` structure, but does not parse the VBA project stream (`vbaProject.bin`). The `UsbEntropyScanner` may flag the document if the macro content raises entropy above threshold, but small macros embedded in large documents may not shift aggregate entropy significantly. The mitigation flags all Office documents with embedded OLE streams or high-entropy embedded objects for quarantine in Strict mode. In Permissive mode, a warning alert is raised. Residual: encrypted macro documents bypass entropy detection. | MagicByteValidator Office detection + UsbEntropyScanner + quarantine in Strict mode; alert in Permissive | CWE-94 | SI-3 |

### Component: UsbWhitelistService

| Threat | STRIDE | Severity | Description | Mitigation | CWE | NIST |
|--------|--------|----------|-------------|------------|-----|------|
| Whitelist modification by malware | Tampering | High | Malware running with the same user privileges as the agent could invoke `UsbWhitelistService.AddAsync` directly via reflection or by accessing the SQLCipher database file. Adding a malicious device's serial hash to the whitelist table effectively disarms USB Guard for that device. The mitigation stores whitelist entries in the SQLCipher-encrypted database with a key derived from a DPAPI-protected master key stored in a DACL-restricted directory (`%ProgramData%\RansomGuard-CM\keys`). The HMAC salt is derived from the database key, preventing offline hash computation. Every whitelist modification is audit-logged with an Ed25519 signature, making unauthorized additions detectable via chain integrity verification. | SQLCipher encryption + DPAPI key protection + DACL-restricted key directory + Ed25519 audit chain | CWE-345 | SI-7 |
| Quarantine restoration unauthorized | Auth | High | An attacker or an insider with file system access could restore quarantined files by copying them out of the quarantine vault directory. If the vault uses only filesystem ACLs without content encryption, a local admin could bypass quarantine. The mitigation encrypts quarantined files with AES-256-GCM before writing to the vault, using a key derived from the DPAPI-protected master key. Restoration requires both the decryption key (held only by the agent process identity) and a valid audit log entry authorizing the restore operation. Unauthorized restoration of encrypted blobs produces unusable ciphertext. | AES-256-GCM encryption at rest; DPAPI-protected key; audit-gated restore | CWE-285 | AC-3 |
| Smart card reader bypass | Spoofing | Medium | A USB smart card reader presents as a composite device with both a CCID (smart card) interface and a mass storage interface. The mass storage partition may contain a malicious autorun payload while the smart card interface appears legitimate. WMI `Win32_DiskDrive` only surfaces the mass storage class, potentially missing the CCID interface entirely. The mitigation detects composite devices by querying SetupAPI for all device interfaces under the same parent hub port. When a composite device includes both smart card and mass storage interfaces, USB Guard applies full content scanning to the mass storage partition regardless of the smart card's legitimate purpose. Alert escalation is applied for composite HID+storage devices. | SetupAPI composite device detection; AlertOnHidDevice configuration flag; full scan regardless of smart card presence | CWE-345 | SI-7 |

### Component: UsbActionEngine

| Threat | STRIDE | Severity | Description | Mitigation | CWE | NIST |
|--------|--------|----------|-------------|------------|-----|------|
| Eject API kernel exploit | Elevation | Medium | The `CM_Request_Device_Eject` P/Invoke communicates with the PnP manager in kernel mode. A specially crafted device driver could intercept the eject IRP (I/O Request Packet) and exploit a vulnerability in the PnP stack to escalate privileges. While the agent calls the documented Win32 API, the kernel-side handling depends on third-party device drivers. The mitigation limits the attack surface by calling `CM_Request_Device_Eject` only after content scanning confirms a threat (not speculatively), reducing the window of exposure. The agent runs as `LocalSystem` with `SeLoadDriverPrivilege` removed from its token, preventing new driver installation during the eject sequence. | Eject only after confirmed threat; agent token stripped of SeLoadDriverPrivilege; documented PnP API only | CWE-269 | AC-6 |
| USB-borne malware attacking agent binary | Tampering | High | Malware delivered via USB could target the RansomGuard agent executable or its configuration files. A sophisticated payload could overwrite the agent binary with a trojanized version that suppresses all USB alerts. The mitigation leverages the `AgentProtector` subsystem (Section A.4): `CodeSectionIntegrity` computes a SHA-256 hash of the running executable and compares it against a baseline using `CryptographicOperations.FixedTimeEquals`. Any mismatch triggers an immediate tamper alert with the highest severity. Additionally, `RegistryWatcher` monitors the service registration keys to detect service binary path modifications. These checks run on a 30-second cycle independent of USB events. | AgentProtector: CodeSectionIntegrity hash check + RegistryWatcher service key monitor + FixedTimeEquals comparison | CWE-345 | SI-7 |
| Bootable USB bypass attempt | Bypass | High | An attacker inserts a bootable USB device containing a live operating system (e.g., Kali Linux, Windows PE). If the machine reboots from the USB, the agent (running in the host OS) is bypassed entirely. Even without reboot, a bootable USB may contain partition structures that confuse the scanner. The `BootableUsbDetector` examines the first 520 bytes of the raw disk for MBR signatures (`0x55AA` at offset 510) and GPT signatures (`EFI PART` at offset 512). When detected, the device is flagged as bootable and, if `BlockBootableUsb` is true (default), an immediate eject is issued. The audit log captures the boot sector type. Residual risk: UEFI Secure Boot bypass via signed bootloaders is out of scope. | BootableUsbDetector MBR/GPT signature check; BlockBootableUsb auto-eject; audit-logged | CWE-862 | AC-3 |

## Coverage Summary

- **Total threats identified**: 15
- **Mitigated in current implementation**: 13
- **Residual risk (documented)**: 2
  - Zero-day LNK exploits that fire before scan completes (LNK Stuxnet)
  - Encrypted macro documents bypass entropy detection (Office macro)
- **Sprint 5+ enhancements planned**:
  - YARA rule integration for signature-based USB malware detection
  - USB device class filtering (block specific USB classes at driver level)
  - Real-time file system mini-filter for USB volumes (kernel mode)
  - Hardware security key attestation for high-assurance whitelisting

## Compliance Mapping

### CWE Top 25 Coverage
CWE-22 (Path Traversal), CWE-94 (Code Injection), CWE-119 (Buffer Overflow), CWE-269 (Improper Privilege Management), CWE-285 (Improper Authorization), CWE-345 (Insufficient Verification of Data Authenticity), CWE-400 (Uncontrolled Resource Consumption), CWE-409 (Improper Handling of Highly Compressed Data), CWE-532 (Information Exposure Through Log Files), CWE-778 (Insufficient Logging), CWE-862 (Missing Authorization)

### NIST SP 800-53 Rev. 5 Controls
- **AC-3** (Access Enforcement): Quarantine authorization, bootable USB blocking
- **AC-6** (Least Privilege): Agent token privilege stripping
- **AU-2** (Audit Events): USB connection audit trail with Ed25519 signatures
- **AU-9** (Protection of Audit Information): Log redaction, hash-chained integrity
- **SC-5** (Denial of Service Protection): Rate limiting, bounded channels, scan timeouts
- **SI-3** (Malicious Code Protection): LNK detection, Office macro detection
- **SI-7** (Software & Data Integrity): Whitelist HMAC, agent binary integrity, composite fingerprinting
- **SI-10** (Information Input Validation): Magic byte bounds checking, path validation

### ISO/IEC 27002:2022
- 8.7 (Protection Against Malware)
- 8.9 (Management of Removable Media)
- 8.12 (Data Leakage Prevention)
- 8.15 (Logging)

### OWASP ASVS 4.0.3
- V1.5.3 (Input Validation): Path traversal prevention in archive extraction
- V7.1.1 (Log Content): Sensitive data redaction in USB file paths
- V9.2.1 (Data Protection): AES-256-GCM quarantine encryption

### MITRE ATT&CK Techniques Mitigated
- **T1091** (Replication Through Removable Media): Content scanning, autorun detection, LNK analysis
- **T1052** (Exfiltration Over Physical Medium): Whitelist enforcement, audit logging, eject response
- **T1200** (Hardware Additions): WMI monitoring, composite device detection, HID alerting
