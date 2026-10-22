# USB GUARD Module — Removable Media Control

**Module:** USB GUARD
**Status:** Detection operational. Read-only, ejection and quarantine real since 2026-10-22; the block of a key on re-insertion is not implemented (see below).

> **Ne pas activer Strict en production tant que la gestion de la whitelist
> depuis la console n'existe pas.** Strict éjecte dès Medium : sans liste
> blanche, les clés légitimes de l'établissement aussi.
>
> *Do not enable Strict in production until whitelist management from the
> console exists.* (debt AGT-USB-003)

## What USB GUARD Does

USB GUARD watches USB device arrivals (WMI), scans the content of mass-storage
devices and acts according to the **operating mode** and the **severity** of
the scan result. The scanners that can raise a High or Critical severity are:

| Detector | Trigger | Severity |
|---|---|---|
| `AutorunInfDetector` | an `autorun.inf` on the device | Critical |
| `MagicByteValidator` | extension does not match the real content | High or Critical |
| `ArchiveScanner` | executable inside an archive | High or Critical |
| `SuspiciousLnkDetector` | suspicious `.lnk` shortcut | High or Critical |

## The Three Operating Modes

Decision matrix: `UsbActionEngine.SelectAction` (`agent/src/RansomGuard.Agent.Core/Detection/UsbGuard/Actions/UsbActionEngine.cs`).

| Mode | Critical | High | Medium | Low |
|---|---|---|---|---|
| **Audit** | Alert | Alert | Alert | Alert |
| **Permissive** (default) | Key set read-only | File quarantined | Alert | Alert |
| **Strict** | Physical cut + eject | Eject | Eject | Alert |

Every action also raises an alert. This table is the **decision** the engine
takes; what each action actually does today is below.

### Implementation status of each action — read this first

| Action | Selected for | What happens (since 2026-10-22) |
|---|---|---|
| Alert | every mode | Alert persisted, logged and forwarded. **Real.** |
| IRONCLAD physical port cut | Strict + Critical, IRONCLAD available, drive mapped to a port | Command sent to IRONCLAD. **Real**, but IRONCLAD defaults to the mock server (see below). |
| `ReadOnlyUsb` | Permissive + Critical | **Real.** The physical disk behind the drive letter is set read-only (`IOCTL_DISK_SET_DISK_ATTRIBUTES`, what `diskpart attributes disk set readonly` does), not persisted: every later write on the key fails with "write protected" until it is unplugged. The attribute is read back before "done" is reported. |
| `QuarantineFile` | Permissive + High | **Real.** Every file the scan flagged at High or above leaves the key, encrypted (AES-256-GCM) into `%ProgramData%\RansomGuard-CM\quarantine`, restorable from there (`QuarantineService`, 30 days). The result counts what went and what did not, file by file. |
| `EjectUsb` | Strict + High/Medium | **Real.** `CM_Request_Device_Eject` on the device node, what "Safely remove" does. A veto (an open file, a process) is reported with the system's reason. |
| `BlockAndEject` (software) | Strict + Critical without IRONCLAD cut | **Ejects for real; the block does not exist.** The same key plugged again is scanned again, not refused. The result says so (`Success = false`, reason `block_not_implemented`): the ejection is never dressed up as a block. (AGT-USB-001) |

Each device call is bounded (10 s): a driver that never answers gives `device_control_failed`, never
a hang of the monitor. An action that was not carried out always says why, in the result, in the
alert (`action_taken`, for example `EjectUsb (device_control_failed)`) and in the **signed audit log**
(`UsbAction` entry, `outcome=executed | <reason code>`). Reason codes: `block_not_implemented`,
`no_device_control`, `no_drive_letter`, `device_control_failed`, `no_quarantine_service`,
`no_file_to_quarantine`, `quarantine_failed`.

**What has been exercised where.** The engine (what is asked of the system, what is reported) is
covered by `UsbRealActionsTests` with a stand-in for the device calls; the Windows calls themselves
refuse a bad argument before any call (`WindowsUsbDeviceControlTests`). A CI runner has no USB key:
read-only, ejection and quarantine on a real key are checked on the two demonstration machines
(TESTS_JURY.md), not in CI. Until that run is recorded there, treat them as implemented and
unit-tested, not as field-proven.

### Module posture

| Mode | Posture reported in the heartbeat | Why |
|---|---|---|
| Audit | `active` | Promises the alert; it happens. |
| Permissive | `active` | Promises read-only and quarantine; they happen (2026-10-22). |
| Strict | `degraded`, reason `actions_not_implemented` (FR « Blocage non implémenté », EN « Blocking not implemented ») | Promises ejection, which happens, and the block of the key on re-insertion, which does not exist (AGT-USB-001). |

### What Strict does exactly

- **Critical**: USB GUARD first asks IRONCLAD to cut the USB port physically.
  If IRONCLAD is unavailable, if the drive letter does not map to a port, or
  if the cut fails, it falls back to the software action `BlockAndEject`:
  the key is ejected; the block on re-insertion does not exist and the
  result says so.
- **High and Medium**: software ejection (`EjectUsb`), without IRONCLAD.

Two facts to know before relying on the physical cut:

- IRONCLAD defaults to `CommunicationMode = TcpMock`
  (`IronCladOptions.cs`): the cut goes to the mock Arduino server, not to real
  hardware, unless the configuration selects the serial-port communicator.
- The drive-letter-to-port mapping is fixed: `E:`→1, `F:`→2, `G:`→3, `H:`→4;
  any other letter maps to no port, so Strict falls back to the software
  action.

## The False-Positive Risk of Strict

Strict ejects from **Medium** upward, and `MagicByteValidator` judges content
against the extension. A radiology operator's key holding DICOM exports, a
device's `.dat` files or files renamed by a manufacturer can score High:
in Strict the **whole key is ejected in the middle of an examination**, where
Permissive would only quarantine the offending file.

The guard rail is the device whitelist (`IUsbWhitelistService`). **Before
Strict is used in production, the whitelist must hold the facility's
legitimate keys.**

## The Whitelist

A whitelisted device skips the content scan entirely. Entries are stored in
the agent's encrypted database; the serial number is stored as
SHA-256(serial || agent salt), compared in constant time. An entry can carry
an expiry date, after which it is deactivated.

### How to add a device — no supported way today

`IUsbWhitelistService` offers `AddAsync` and `TemporaryApproveAsync`, but
**nothing calls them**: there is no agent CLI sub-command, no GRID command and
no console screen to enrol a device. Inserting a row by hand is not a
practical workaround either: the database is encrypted (SQLCipher) and the
stored value is a salted hash computed by the agent.

Consequence: the whitelist is always empty today, and the startup warning
below fires on every Strict start. An approval screen in the console, where
the hospital admin approves a device, with audit, is debt AGT-USB-003 — a
prerequisite of Strict in production.

### What happens when the whitelist cannot be read

The whitelist is read at every device connection, **before** the bootable
check and the content scan. If that read fails (database unavailable, key
unreadable), connection handling stops on the error: **the device is let
through, neither scanned nor blocked, in every mode including Strict** — a
bootable key included. The error is logged as `USB GUARD: Connection handling
failed`. In Strict, the startup check also reads the whitelist and writes this
consequence to the log if it cannot. Fixing this (a read failure never
short-circuits the scan; in Strict the device is treated as not approved;
an alert is raised) is debt AGT-USB-002.

## Is a Strict Block Reversible Without Restarting?

| Situation | Without restarting the agent? |
|---|---|
| Key ejected (`EjectUsb`) | The key is ejected (real since 2026-10-22). Replugging it re-scans it and ejects it again; with no whitelist enrolment path, there is no way to let it through. |
| Port cut by IRONCLAD | **No, and a restart does not restore it either** (debt AGT-IRC-001). `RestoreUsbPortAsync` exists but no command calls it. At agent start, `IronCladStateReconciliationService` restores cut ports only when `FailSafeRestoreOnDisconnect` is on (default `true`) **and** the stored state disagrees with the device — the crash case. A port cut normally, and recorded as cut, stays cut across restarts. |
| Leaving Strict (back to Permissive) | **No.** The configuration is read once, when the USB monitor is built: change `Agent:UsbGuard:OperatingMode` and restart the agent service. |

The development configuration runs Strict. On a development machine, the
owner's own keys are therefore exposed to Strict decisions: a Medium finding
ejects the key, a Critical one on drive `E:` to `H:` asks IRONCLAD for the cut
(sent to the mock server by default) and ejects otherwise.
To work without it, set `OperatingMode` to `Permissive` or `Audit` in
`appsettings.Development.json` and restart the agent.

## Configuration Rules

```json
{
  "Agent": {
    "UsbGuard": {
      "Enabled": true,
      "OperatingMode": "Permissive",
      "MaxFileSizeForScanMB": 100,
      "MaxScanDurationSeconds": 120,
      "ScanArchiveContents": true,
      "BlockBootableUsb": true,
      "AlertOnHidDevice": true,
      "AlertOnNetworkDevice": true,
      "ScanRatePerSecond": 50
    }
  }
}
```

| Where | Mode | Why |
|---|---|---|
| Code default (`AgentConfiguration.cs`, `UsbGuardOptions.OperatingMode`) | `Permissive` | The code default applies to any installation that forgets to configure. A dangerous behaviour must be a written choice, never an inheritance. |
| Shipped configuration (`appsettings.json`) | `Permissive` | Same reason. |
| Development configuration (`appsettings.Development.json`) | `Strict` | To demonstrate and exercise the mode. |
| Deployment configuration | `Strict` only once the whitelist is populated | Written decision of the facility. |

### Startup warning

In Strict mode the agent reads the whitelist at startup
(`UsbGuardStartupCheck`, called by `UsbDeviceMonitor.ExecuteAsync` before the
monitor reports itself active) and writes a **warning** when:

- the whitelist holds no active entry: every device scored Medium or above
  gets the Strict action, the facility's legitimate keys included;
- the whitelist cannot be read: devices are let through, neither scanned nor
  blocked, as long as it stays unreadable.

It never blocks the agent: the warning makes the consequence explicit.
The startup log line also states the active mode:
`USB GUARD monitor active (mode: Strict)`.

### Known limitation

An unrecognised `OperatingMode` value (a typo such as `"Stirct"`) currently
falls back to `Permissive` **silently**. This is one of the agent's silent
defaults, to be closed by the configuration-validation work (startup refused,
naming the invalid value), not by a module-specific patch.
