# GENEALOGY Module — Process Tree Forensics

**Module:** 3/5 innovation modules
**Version:** v0.5.0
**Status:** Operational

## What GENEALOGY Does

When SENTINEL or ENTROPY fires an alert, GENEALOGY answers three critical questions: Which process modified the file? Who spawned that process? Is the process chain suspicious?

GENEALOGY reconstructs the process tree of the offending process by walking the parent-process chain upward (up to 10 levels) and analyzing each relationship against 7 known MITRE ATT&CK ransomware patterns. The result transforms a generic alert ("file was encrypted") into an actionable forensic report ("powershell.exe spawned by winword.exe via macro, using EncodedCommand obfuscation, then called vssadmin to delete shadow copies").

## Process Tree Reconstruction

GENEALOGY uses three Windows APIs to capture process information:

1. **System.Diagnostics.Process**: PID, process name, executable path, memory, thread count, start time
2. **WMI (Win32_Process)**: Command line arguments and parent process ID (not available in System.Diagnostics)
3. **Restart Manager API**: Identifies which process holds a lock on the alerted file

The tree is built by:
1. Identifying the file-locking process via Restart Manager
2. Capturing a full snapshot of that process
3. Walking the parent chain via ParentProcessId (max 10 levels, cycle detection for PID reuse)
4. Running all 7 pattern detectors on the resulting tree

## The Seven MITRE ATT&CK Patterns

### T1490 — Inhibit System Recovery: vssadmin (CRITICAL)
**Detection:** Process name is "vssadmin" AND command line contains "delete shadows".
**Why critical:** ALL major ransomware families (LockBit, BlackCat, Ryuk, Conti, REvil) delete Volume Shadow Copies before encrypting to prevent recovery.

### T1490 — Inhibit System Recovery: bcdedit (CRITICAL)
**Detection:** Process name is "bcdedit" AND command line contains "recoveryenabled no".
**Why critical:** Disables Windows Recovery Environment, preventing boot-time repair.

### T1059.001 — Office Macro Abuse (HIGH)
**Detection:** Parent is winword/excel/powerpnt/outlook AND child is cmd/powershell/wscript/cscript.
**Why:** Most ransomware initial access vectors use Office macros that spawn command interpreters.

### T1566 — Phishing Attachment (HIGH)
**Detection:** Parent is outlook AND child executable path is NOT in Program Files or Windows directories.
**Why:** Email attachments executed directly from Outlook bypass normal software installation paths.

### T1218 — Signed Binary Proxy Execution (MEDIUM)
**Detection:** Parent is explorer AND child is wscript/mshta/regsvr32/rundll32.
**Why:** Living-off-the-land binaries (LOLBins) are used to execute malicious payloads via trusted Microsoft binaries.

### T1027 — Obfuscated PowerShell (HIGH)
**Detection:** Process is powershell/pwsh AND command line contains "-EncodedCommand" or "-enc" or "FromBase64String".
**Why:** Ransomware droppers frequently use Base64-encoded PowerShell to evade command-line logging.

### T1059 — Unsigned Exe in TEMP/APPDATA (HIGH)
**Detection:** Executable path contains \Temp\ or \AppData\.
**Why:** Legitimate software installs to Program Files; malware drops to temporary directories.

## Configuration Reference

| Setting | Default | Description |
|---------|---------|-------------|
| Enabled | true | Enable/disable genealogy enrichment |
| MaxAncestorDepth | 10 | Maximum parent chain levels |
| EnrichmentTimeoutSeconds | 5 | Timeout per enrichment attempt |

## Performance

- Process snapshot capture: < 50 ms per process
- Tree build (10 ancestors): < 500 ms
- Pattern detection: < 1 ms (in-memory string matching)
- Total enrichment: < 5 seconds (timeout enforced)
- Fire-and-forget: never blocks the detection pipeline

## Known Limitations

1. **Kernel-mode rootkits**: Cannot detect processes hidden at ring-0 (out of scope)
2. **Fast-exit processes**: If the process exits before enrichment, attribution may fail (cache mitigates)
3. **PID reuse**: Windows reuses PIDs; a small race window exists between capture and analysis
4. **Command line obfuscation**: Patterns use string matching; advanced obfuscation may evade (e.g., character insertion in "vssadmin")
5. **WMI dependency**: Requires WMI service running (default on all Windows editions)

## MITRE ATT&CK Coverage Table

| Technique ID | Technique Name | Severity | Ransomware Families Using It |
|-------------|----------------|----------|------------------------------|
| T1490 | Inhibit System Recovery | Critical | LockBit, BlackCat, Ryuk, Conti, REvil, Hive |
| T1059.001 | PowerShell via Office Macro | High | Emotet, TrickBot, QakBot |
| T1566 | Phishing Attachment | High | All initial access vectors |
| T1218 | Signed Binary Proxy | Medium | Advanced persistent threats |
| T1027 | Obfuscated Files | High | Most malware families |
| T1059 | Command Interpreter (TEMP) | High | Commodity malware |
