# EXFIL WATCH Module — STRIDE Threat Model

## Components Under Analysis

1. **NetworkActivityMonitor (ETW)** — Kernel-level Event Tracing for Windows session (`EtwNetworkCapture`) capturing DNS Client and TCP/IP provider events. Rate-limited at 100 events/second via `TokenBucketRateLimiter`, feeding a bounded channel (10,000 capacity) consumed by `ExfilWatchMonitor` BackgroundService.
2. **NetworkBaselineService** — Three-phase adaptive baseline (Learning → ActiveDetection → DriftDetected) using Welford's online variance algorithm. Tracks per-process and per-destination byte volumes with hourly and weekly patterns. Learning phase locks after configurable days (default 7). Active phase applies capped 5% daily updates to prevent baseline poisoning.
3. **ExfiltrationDetector (8 Rules)** — `ExfilRuleEngine` evaluating VolumeAnomalyRule, DnsTunnelingRule, AfterHoursExfilRule, SuspiciousDestinationRule, TorTrafficRule, UnknownProcessExfilRule, LolbasExfilRule, and EncryptedExfilCorrelationRule. Rules consume `IDataVolumeTracker` sliding windows and `IThreatIntelProvider` intelligence.
4. **ThreatIntelProvider** — `ThreatIntelDataLoader` with 1,286 real Tor exit nodes, 1,612 real C2 server IPs, 6,936 cloud provider CIDRs, and 30 LOLBAS binaries loaded from embedded JSON resources. Supports signed update packages with Ed25519 verification via `ThreatIntelUpdateValidator`.
5. **ExfilActionEngine** — Severity-based response: Low/Medium → AlertOnly, High → Alert + ThrottleProcess (QoS 10%), Critical → Alert + Throttle + BlockIp (INetFwPolicy2 24h auto-expiry). Cloud whitelist suppression with 1 GB/hr override threshold. Graceful fallback chain with 10-second timeout.

## Threat Matrix

### Component: NetworkActivityMonitor (ETW)

| Threat | STRIDE | Severity | Description | Mitigation | CWE | NIST |
|--------|--------|----------|-------------|------------|-----|------|
| IP source spoofing | Spoofing | High | An attacker could use raw sockets or a compromised network driver to forge the source IP address on outbound packets. The ETW TCP/IP provider reports the address from the kernel TCP stack, which reflects the actual socket binding — not the wire-level packet. However, a rootkit that hooks `NDIS` or `WFP` layers could present spoofed metadata to ETW consumers. At the application layer, `EtwNetworkCapture` trusts the kernel-reported PID and remote address. The mitigation cross-references the reported PID against the process table via `Process.GetProcessById` to verify the process exists and its image matches expectations. Residual risk: kernel-level rootkits that intercept ETW events are out of scope for a user-mode agent. | PID cross-reference against process table; ETW kernel-mode provider (higher trust than user-mode); documented rootkit residual | CWE-290 | SC-23 |
| ETW buffer overflow | DoS | Medium | The ETW session allocates a configurable number of 64 KB buffers (default 64, controlled by `ExfilWatchOptions.EtwBufferCount`). Under extreme network load (e.g., 100 Gbps NIC during a DDoS reflection), the kernel may lose events when all buffers are full. Lost events create detection blind spots where exfiltration could occur unnoticed. The mitigation monitors ETW session statistics (`EventsLost` counter) and logs a warning when loss exceeds 1% of total events. The `EtwBufferCount` is configurable up to 256 buffers (16 MB). Additionally, the bounded channel with DropOldest policy ensures the consumer never blocks the ETW callback thread, which would cascade into kernel buffer exhaustion. | Configurable EtwBufferCount (64-256); EventsLost monitoring; bounded channel DropOldest prevents callback blocking | CWE-400 | SC-5 |
| ETW session abuse | Elevation | Medium | An attacker with `SeSystemProfilePrivilege` could stop the RansomGuard ETW session (`RansomGuard-NetworkCapture`) using `logman stop` or `xperf -stop`. Alternatively, they could create a competing session on the same provider with exclusive access, blocking the agent's session. The mitigation runs the ETW session with `PROCESS_TRACE_MODE_REAL_TIME` and detects session loss by monitoring the heartbeat counter in `ExfilWatchMonitor`. If no events are received for 60 seconds, a critical alert is raised and the session is restarted. The `IIndicatorRemovalDetector` subsystem (Section B.4) also monitors for `auditpol` and `wevtutil` invocations that may indicate an attacker disabling audit infrastructure. | Session loss detection via heartbeat; auto-restart; IndicatorRemovalDetector monitors audit-disabling commands | CWE-269 | AC-6 |
| Network destinations in logs | Info Disclosure | Medium | Action log messages from `AlertOnlyAction`, `ThrottleProcessAction`, and `BlockIpAction` include destination IP addresses and process names. In a healthcare network, destination IPs could correlate to patient portals, telemedicine endpoints, or diagnostic equipment. The `_auditLog.AppendAsync` details string contains `Dest={finding.Destination}`. The mitigation leverages the global Serilog `LogRedactor` enricher that applies partial IP redaction (last octet masked: `192.168.1.xxx`) in file and console sinks. The Ed25519-signed audit log retains full IPs for forensic purposes, accessible only through the encrypted SQLCipher database with DACL-protected key material. | LogRedactor partial IP masking in sinks; full IPs only in encrypted audit DB | CWE-532 | AU-9 |

### Component: NetworkBaselineService

| Threat | STRIDE | Severity | Description | Mitigation | CWE | NIST |
|--------|--------|----------|-------------|------------|-----|------|
| Baseline poisoning over 30 days | Tampering | High | A patient attacker gradually increases exfiltration volume over 30+ days, allowing the adaptive baseline to absorb the elevated traffic as normal. Once the baseline reflects the malicious traffic pattern, the `VolumeAnomalyRule` (which triggers when current volume exceeds `VolumeAnomalyMultiplier * baseline_average`) no longer fires because the baseline has been poisoned. The mitigation caps daily baseline updates at 5% of the current baseline value (`NetworkBaselineService` Welford variance with `MaxDailyUpdateRate = 0.05`). This means even with sustained elevated traffic, the baseline moves slowly — requiring approximately 60 days to double. The `DriftDetected` phase triggers when variance exceeds 3 standard deviations of the original learning-phase distribution, alerting analysts to potential poisoning. | 5% daily update cap; DriftDetected phase on 3-sigma variance; learning phase lock after 7 days | CWE-345 | SI-7 |
| Process correlation race condition | Race | Medium | The `EncryptedExfilCorrelationRule` (Rule 8) maintains a `ConcurrentDictionary<int, EntropySignal>` keyed by PID. If a process terminates and its PID is recycled by the OS before the 60-second correlation window expires, a legitimate new process could be falsely correlated with the terminated process's encryption activity. Windows recycles PIDs from a pool of ~65,536 values; under heavy process churn, recycling can occur within seconds. The mitigation adds the process start time as a secondary correlation key. The `EntropySignal.EmittedAt` timestamp must be within 60 seconds of the network event, and the PID must still map to the same process image name. Residual: same-name processes with recycled PIDs within 60 seconds remain a theoretical false positive source. | PID + process name + 60-second window; EmittedAt timestamp validation; documented residual for same-name PID recycling | CWE-362 | SI-10 |

### Component: ExfiltrationDetector (8 Rules)

| Threat | STRIDE | Severity | Description | Mitigation | CWE | NIST |
|--------|--------|----------|-------------|------------|-----|------|
| LOLBAS bypass via signed binary | Code Exec | High | The `LolbasExfilRule` checks `finding.ProcessName` against a HashSet of 30 known Living-Off-the-Land binaries (`certutil.exe`, `bitsadmin.exe`, `mshta.exe`, etc.). An attacker could rename a LOLBAS binary (e.g., copy `certutil.exe` to `cert_backup.exe`) to evade name-based detection. Alternatively, they could use a signed Microsoft binary not in the LOLBAS list (e.g., `dotnet.exe`, `csc.exe`) to compile and execute exfiltration code. The mitigation combines name matching with `IThreatIntelProvider.IsLolbasBinary` (which covers 30 binaries) and the `UnknownProcessExfilRule` (which flags any process not seen during the learning phase). Sprint 5 enhancement: image hash verification against a known-good catalog. | LOLBAS name set + UnknownProcessExfilRule for novel binaries; Sprint 5: image hash verification | CWE-94 | SI-3 |
| DNS tunneling missed by entropy threshold | Spoofing | High | The `DnsTunnelingRule` uses Shannon entropy of DNS subdomain labels (threshold default 4.5 bits/char) to detect encoded data in DNS queries. An attacker using dictionary-based encoding (mapping data to real English words, e.g., `apple-banana-cherry.evil.com`) produces low-entropy domain names that pass under the threshold. Legitimate CDN domains (e.g., `a1b2c3d4.cloudfront.net`) may also have high entropy, causing false positives. The mitigation combines entropy with query frequency (1,000 queries/5 min threshold) — dictionary encoding requires more queries to achieve the same bandwidth, making frequency a complementary signal. Additionally, `SuspiciousDestinationRule` escalates domains resolving to known C2 IPs regardless of entropy. | Dual signal: entropy + frequency threshold; C2 IP escalation independent of DNS entropy | CWE-693 | SI-3 |
| After-hours legitimate work | UX | Medium | The `AfterHoursExfilRule` flags network activity outside configured working hours (default 07:00-19:00). Healthcare professionals frequently work irregular shifts — night-shift nurses, on-call physicians, and emergency departments operate 24/7. False positives from legitimate after-hours work erode trust in the alerting system. The mitigation makes working hours fully configurable per deployment (`ExfilWatchOptions.WorkingHoursStart/End`). Hospital deployments can set a 24-hour window (start=0, end=0) to effectively disable after-hours detection. Alternatively, department-specific configurations can be applied via `appsettings.{Environment}.json` overrides. The rule severity is set to Medium (not Critical), ensuring it generates informational alerts without triggering aggressive response actions. | Configurable WorkingHoursStart/End; Medium severity (alert only, no throttle/block); environment-specific overrides | N/A | N/A |
| Cloud sync false positives spam | UX | Medium | OneDrive, Dropbox, and Google Drive sync clients generate sustained high-volume uploads during initial sync, folder reorganization, or bulk document updates. The `VolumeAnomalyRule` may fire repeatedly for legitimate cloud sync traffic, generating dozens of alerts per day. Alert fatigue causes security analysts to disable or ignore the module. The mitigation uses the cloud whitelist suppression in `ExfilActionEngine.ShouldSuppressForCloudWhitelist`: destinations matching `IThreatIntelProvider.IsKnownCloudProvider` (6,936 real CIDRs) suppress alerts when cumulative bytes to that destination are under `CloudAlertThresholdGbPerHour` (default 1.0 GB). When OneDrive syncs 500 MB, no alert fires. When an attacker abuses OneDrive to exfiltrate 2 GB/hr, the threshold override triggers a full alert with Critical severity. | Cloud CIDR whitelist (6,936 CIDRs) + configurable 1 GB/hr threshold; suppression under threshold, full alert over | N/A | N/A |
| Indicator removal undetected | Cover-up | Critical | A sophisticated attacker clears Windows Event Logs (`wevtutil cl`), deletes USN journal entries, disables Windows Defender real-time protection, or removes scheduled tasks to cover tracks after exfiltration. If these indicator removal actions occur without detection, the forensic timeline has critical gaps. The mitigation leverages the `MultiStageKillChainDetector` (Section B.4) which correlates indicator removal signals from four detectors (`EventLogClearingDetector`, `UsnJournalClearingDetector`, `DefenderTamperingDetector`, `SchedTaskTamperingDetector`) and escalates to Critical when multiple removal indicators occur within a configurable correlation window. Each detector uses ETW and WMI subscriptions to capture removal events in real-time before the evidence is destroyed. | MultiStageKillChainDetector with 4 detectors; real-time ETW/WMI capture; Ed25519-signed audit log preserves evidence | CWE-778 | AU-2 |

### Component: ThreatIntelProvider

| Threat | STRIDE | Severity | Description | Mitigation | CWE | NIST |
|--------|--------|----------|-------------|------------|-----|------|
| Threat intel update tampering | Tampering | Critical | An attacker with write access to the update directory (`%ProgramData%\RansomGuard-CM\updates\`) could place a malicious threat intel package that removes C2 IPs from the detection list, effectively whitelisting attacker infrastructure. The `ThreatIntelUpdateValidator` mitigates this with a multi-layer verification: (1) ZIP structural validation, (2) `manifest.json` parsing with version downgrade rejection, (3) SHA-256 content hash of all package entries, (4) Ed25519 signature verification using `NSec.Cryptography.SignatureAlgorithm.Ed25519.Verify`. The signing key is held only by the build server; the agent embeds only the public key. A forged package with an invalid signature is rejected and audit-logged as `ExfilBlock` with the error message. Rollback to the previous data set occurs automatically on validation failure. | Ed25519 signed packages (NSec/libsodium); version downgrade rejection; SHA-256 content hash; automatic rollback on failure | CWE-345 | SI-7 |

### Component: ExfilActionEngine

| Threat | STRIDE | Severity | Description | Mitigation | CWE | NIST |
|--------|--------|----------|-------------|------------|-----|------|
| Bluetooth tethering missed | Spoofing | Medium | An attacker could use Bluetooth PAN (Personal Area Network) tethering to a mobile phone to exfiltrate data over cellular. Bluetooth network adapters appear as standard network interfaces to Windows, but ETW TCP/IP provider events include all interfaces indiscriminately. The detection rules still apply — `VolumeAnomalyRule` and `SuspiciousDestinationRule` evaluate traffic regardless of the network interface. However, the carrier NAT on the mobile device may obscure the true destination IP. The mitigation documents that Bluetooth tethering is detectable by volume anomaly (new interface with sudden high traffic) but destination attribution is limited by carrier NAT. Sprint 6: interface-aware baseline tracking. | Volume anomaly detection applies across all interfaces; documented carrier NAT limitation; Sprint 6: interface-aware baselines | CWE-693 | SC-7 |
| 4G dongle exfil missed | Spoofing | Medium | A USB 4G/LTE dongle creates an independent network path that may bypass corporate firewall and proxy infrastructure. The ETW kernel trace captures TCP/IP events on all interfaces including the dongle's virtual adapter, so the agent detects the traffic. However, the destination IPs route through the carrier's network, and without the corporate DNS, domain-based threat intel is unavailable. The mitigation relies on `UnknownProcessExfilRule` (process never seen during learning when only corporate interfaces existed) and `VolumeAnomalyRule` (new interface baseline is zero, so any significant traffic triggers). The `AlertOnNetworkDevice` USB Guard configuration flag also raises an alert when a USB network adapter is detected via WMI. | ETW captures all interfaces; UnknownProcessExfilRule for novel interface traffic; USB Guard AlertOnNetworkDevice flag | CWE-693 | SC-7 |
| VPN exfil masking | Spoofing | High | An attacker establishes a VPN tunnel (WireGuard, OpenVPN, or SSH tunnel) to a remote server, encapsulating all exfiltrated data within the encrypted VPN session. The ETW trace sees only the outer VPN connection to a single IP, not the inner traffic destinations. The `SuspiciousDestinationRule` may not flag the VPN server IP if it is a legitimate cloud provider. The mitigation detects VPN usage through behavioral signals: (1) sustained high-bandwidth connection to a single IP from a process not in the learning baseline, (2) `UnknownProcessExfilRule` flags the VPN client process if it was not present during learning, (3) `TorTrafficRule` covers common anonymization ports (9001, 9030). Residual risk: VPN to a cloud VM on a whitelisted CIDR under the 1 GB threshold. | Behavioral detection: single-destination high bandwidth + unknown process; Tor port detection; documented residual for cloud VPN under threshold | CWE-693 | SC-7 |

## Coverage Summary

- **Total threats identified**: 15
- **Mitigated in current implementation**: 12
- **Residual risk (documented)**: 3
  - Kernel-level rootkits manipulating ETW events (IP spoofing)
  - Same-name process PID recycling within 60-second correlation window
  - VPN exfiltration to whitelisted cloud CIDR under 1 GB/hr threshold
- **Sprint 5+ enhancements planned**:
  - Interface-aware baseline tracking (detect new network adapters)
  - Process image hash verification for LOLBAS bypass resistance
  - JA3/JA3S TLS fingerprinting for encrypted tunnel detection
  - NetFlow integration for east-west lateral movement detection
  - Real-time STIX/TAXII feed subscription for threat intel freshness

## Compliance Mapping

### CWE Top 25 Coverage
CWE-269 (Improper Privilege Management), CWE-290 (Authentication Bypass by Spoofing), CWE-345 (Insufficient Verification of Data Authenticity), CWE-362 (Race Condition), CWE-400 (Uncontrolled Resource Consumption), CWE-532 (Information Exposure Through Log Files), CWE-693 (Protection Mechanism Failure), CWE-778 (Insufficient Logging), CWE-94 (Code Injection)

### NIST SP 800-53 Rev. 5 Controls
- **AC-6** (Least Privilege): ETW session privilege requirements
- **AU-2** (Audit Events): Indicator removal detection, Ed25519 audit chain
- **AU-9** (Protection of Audit Information): IP redaction, encrypted audit DB
- **SC-5** (Denial of Service Protection): ETW buffer management, channel backpressure
- **SC-7** (Boundary Protection): Network interface monitoring, VPN detection
- **SC-23** (Session Authenticity): IP source verification via kernel ETW
- **SI-3** (Malicious Code Protection): LOLBAS detection, DNS tunneling
- **SI-7** (Software & Data Integrity): Baseline poisoning caps, Ed25519 signed updates
- **SI-10** (Information Input Validation): Process correlation validation

### ISO/IEC 27002:2022
- 8.7 (Protection Against Malware)
- 8.12 (Data Leakage Prevention)
- 8.15 (Logging)
- 8.16 (Monitoring Activities)
- 8.20 (Network Security)

### OWASP ASVS 4.0.3
- V1.11.1 (Business Logic Security): Baseline poisoning resistance
- V7.1.1 (Log Content): Sensitive destination redaction
- V9.2.1 (Data Protection): Ed25519 signed audit integrity

### MITRE ATT&CK Techniques Mitigated
- **T1041** (Exfiltration Over C2 Channel): SuspiciousDestinationRule, C2 IP detection
- **T1071.004** (Application Layer Protocol: DNS): DnsTunnelingRule entropy + frequency
- **T1090.003** (Proxy: Multi-hop Proxy): TorTrafficRule, exit node detection
- **T1218** (System Binary Proxy Execution): LolbasExfilRule, 30 LOLBAS binaries
- **T1486** (Data Encrypted for Impact): EncryptedExfilCorrelationRule cross-module
- **T1070** (Indicator Removal): MultiStageKillChainDetector, 4 sub-detectors
- **T1562** (Impair Defenses): ETW session monitoring, IndicatorRemovalDetector
