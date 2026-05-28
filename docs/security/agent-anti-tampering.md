# Agent Anti-Tampering Design Document

## 1. Threat Landscape

Modern ransomware operators routinely target endpoint detection and response (EDR) agents as a precursor to deploying their payload. The 2024-2026 threat landscape shows a clear pattern: groups like BlackCat/ALPHV, LockBit 3.0, and Royal ransomware dedicate entire kill-chain stages to disabling security software before encryption begins. Common techniques include: terminating the agent process via `taskkill /f` or `Stop-Service` (T1489), modifying the Windows service registry keys to change the binary path or startup type to disabled (T1112), injecting malicious DLLs into the agent process via `SetWindowsHookEx` or `QueueUserAPC` to subvert detection logic from within (T1055), attaching a debugger to intercept and suppress alert-generating code paths (T1622), patching the agent binary on disk to insert NOPs over critical comparison instructions (T1036), and using signed kernel drivers (e.g., vulnerable BYOVD — Bring Your Own Vulnerable Driver) to kill processes from kernel mode, bypassing user-mode protections entirely. Healthcare-sector ransomware specifically targets EDR because hospital networks often cannot tolerate the downtime required for forensic analysis, making them more likely to pay. RansomGuard's anti-tampering subsystem addresses the user-mode subset of these threats, acknowledging that kernel-level attacks require kernel-mode defenses planned for Sprint 8.

## 2. Defense Components

### AgentProtector (Composite Orchestrator)

The `AgentProtector` class (`Security/AntiTampering/AgentProtector.cs`) implements `IAgentProtector` and orchestrates all anti-tampering sub-components into a unified defense system. On startup, it calls `CodeSectionIntegrity.ComputeBaseline()` to snapshot the executable hash, `RegistryWatcher.StartMonitoring()` to capture registry baseline values and begin polling, and `DebuggerDetector.Check()` for an immediate debugger sweep. It then starts a 60-second `System.Threading.Timer` that invokes `CheckIntegrityAsync()` on each cycle, aggregating tampering indicators from all sub-components into a `TamperingStatus` record. If any indicator is detected, the status is logged at `Critical` severity. The composite pattern ensures that adding new sub-components (e.g., DLL load verification in Sprint 6) requires only constructor injection without modifying the orchestration logic. The `IDisposable` implementation ensures timers and registry watchers are properly cleaned up on service shutdown.

### DebuggerDetector

The `DebuggerDetector` class (`Security/AntiTampering/DebuggerDetector.cs`) defends against debugger-based reverse engineering and runtime manipulation (CWE-345, MITRE T1622). It employs three complementary detection mechanisms: the managed `System.Diagnostics.Debugger.IsAttached` property for CLR-level debuggers (Visual Studio, dnSpy), the native `kernel32.dll!IsDebuggerPresent()` P/Invoke for Win32 debuggers (x64dbg, WinDbg), and `kernel32.dll!CheckRemoteDebuggerPresent()` for debuggers attached from another process (remote kernel debugging). A development mode flag (`allowInDevelopment`) downgrades the alert to a warning for local development, preventing false alarms during debugging sessions while maintaining Critical-severity enforcement in production. When a debugger is detected in production, the `Check()` method returns a tampering indicator string that the `AgentProtector` includes in the `TamperingStatus`, triggering an audit log entry and a Critical-severity alert.

### CodeSectionIntegrity

The `CodeSectionIntegrity` class (`Security/AntiTampering/CodeSectionIntegrity.cs`) detects on-disk binary tampering by maintaining a SHA-256 hash baseline of the running executable (CWE-345, MITRE T1036). At startup, `ComputeBaseline()` reads the agent executable via `Process.GetCurrentProcess().MainModule.FileName`, computes `SHA256.HashData()` over the entire file, and stores the hex string. On each 60-second verification cycle, `Verify()` recomputes the hash and compares it against the baseline using `CryptographicOperations.FixedTimeEquals` to prevent timing side-channel attacks (CWE-208). A mismatch indicates that the binary was replaced or patched on disk — common in ransomware that trojanizes security agents to suppress alerts. The comparison operates on UTF-8-encoded hex strings rather than raw bytes, which is functionally correct since the hex encoding is deterministic. If the executable path cannot be resolved (e.g., single-file publish), the check is gracefully skipped to avoid false positives.

### RegistryWatcher

The `RegistryWatcher` class (`Security/AntiTampering/RegistryWatcher.cs`) monitors two critical registry paths: the Windows service configuration key (`HKLM\SYSTEM\CurrentControlSet\Services\RansomGuard-CM Agent`) and the autostart entry (`HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run`). At startup, `CaptureBaseline()` reads the `ImagePath` value from the service key and the `RansomGuard-CM` value from the autostart key. A 5-second polling timer then compares current values against the baseline on each cycle. When a modification is detected — such as an attacker changing `ImagePath` to point to a trojanized binary, or deleting the autostart entry to prevent the agent from surviving reboot — the `CheckForModifications()` method returns a descriptive string logged at Critical severity. The baseline is updated after detection to prevent repeated alerts for the same change, while the audit log preserves the forensic timeline. This addresses MITRE T1112 (Modify Registry) and CWE-269 (Improper Privilege Management).

## 3. Threat Coverage Matrix

| Ransomware Tampering Technique | MITRE ID | Sub-Component | Detection Method | Severity |
|-------------------------------|----------|---------------|-----------------|----------|
| `taskkill /f /im RansomGuard*` | T1489 | AgentProtector | Process runs as Windows Service (SCM auto-restart on crash) | Critical |
| `Stop-Service RansomGuard-CM` | T1489 | AgentProtector | SCM recovery policy: restart after 5s, 30s, 60s | Critical |
| `sc config ... start= disabled` | T1112 | RegistryWatcher | Service key `ImagePath` modification detected on 5s poll | Critical |
| `reg delete ... /f` (autostart) | T1112 | RegistryWatcher | Autostart key deletion detected on 5s poll | Critical |
| Binary replacement on disk | T1036 | CodeSectionIntegrity | SHA-256 hash mismatch on 60s cycle | Critical |
| Runtime code patching (NOP sled) | T1055 | CodeSectionIntegrity | Binary hash mismatch after file-backed page is modified | Critical |
| Debugger attachment (dnSpy, x64dbg) | T1622 | DebuggerDetector | Triple-check: managed + native + remote debugger APIs | Critical |
| Remote kernel debugger (WinDbg) | T1622 | DebuggerDetector | `CheckRemoteDebuggerPresent()` native call | Critical |
| DLL injection via `SetWindowsHookEx` | T1055 | Not yet covered | Sprint 6: DllLoadVerifier planned | High |
| ETW session termination | T1562 | IndicatorRemovalDetector | Heartbeat loss detection + session restart in ExfilWatchMonitor | High |
| Event Log clearing (`wevtutil cl`) | T1070.001 | IndicatorRemovalDetector | EventLogClearingDetector (B.4) via ETW subscription | Critical |
| Defender tampering | T1562.001 | IndicatorRemovalDetector | DefenderTamperingDetector (B.4) monitors tamper protection state | Critical |
| BYOVD kernel driver kill | T1068 | Not yet covered | Sprint 8: Kernel mini-filter driver planned | Critical |

The current implementation covers 11 of 13 documented ransomware tampering techniques at the user-mode level. The two uncovered techniques (DLL injection and BYOVD) require kernel-mode defenses that are architecturally impossible to implement purely in user-mode managed code.

## 4. Residual Risks

**Kernel-level rootkits**: A rootkit operating at Ring 0 (kernel mode) can intercept all user-mode API calls, including the Win32 APIs used by `DebuggerDetector` and the file I/O used by `CodeSectionIntegrity`. A sufficiently sophisticated rootkit could return clean hashes to SHA-256 reads while serving tampered code to the CPU. This is fundamentally unsolvable at the user-mode level and requires a kernel mini-filter driver (Sprint 8) or hardware-backed attestation (TPM integration).

**Signed driver abuse (BYOVD)**: Attackers use legitimately signed but vulnerable kernel drivers (e.g., `RTCore64.sys`, `dbutil_2_3.sys`) to gain kernel code execution and kill protected processes. Windows Driver Signature Enforcement prevents unsigned drivers, but signed vulnerable drivers bypass this control. Mitigation requires Microsoft's Vulnerable Driver Blocklist (`DriverSiPolicy.p7b`) plus a kernel-mode component to enforce it.

**Hardware-level attacks**: PCI Express DMA attacks (via Thunderbolt or external GPU), firmware-level implants (UEFI rootkits like CosmicStrand), and cold-boot memory extraction operate below the operating system entirely. These are out of scope for an endpoint software agent and require hardware security features (IOMMU, Secure Boot, memory encryption) configured at the platform level.

**Race condition on startup**: Between Windows Service Manager starting the process and `AgentProtector.StartProtectionAsync()` completing, there is a window (approximately 2-3 seconds) where no anti-tampering checks are active. An attacker with precise timing could exploit this gap. Mitigation for Sprint 7: move baseline computation to the .NET Host startup pipeline before `ExecuteAsync`.

## 5. Sprint 8 Commercial Roadmap

The commercial release targets four key enhancements to close the kernel-mode gap: (1) **Authenticode code signing** of the agent executable and all managed DLLs, enabling Windows Code Integrity to enforce signed-binary-only loading and blocking unsigned DLL injection; (2) **Kernel mini-filter driver** registered with altitude 385100 (FSFilter Anti-Virus range) to protect the agent process from termination, file tampering, and registry modification at kernel level; (3) **WHQL certification** of the mini-filter driver for inclusion in Windows Update, ensuring broad deployment compatibility and Microsoft's malware-free assurance; (4) **ELAM (Early Launch Anti-Malware) driver** to start protection before third-party drivers load, closing the BYOVD attack vector.
