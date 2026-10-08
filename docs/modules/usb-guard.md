# USB GUARD Module — Removable Media Control

**Module:** USB GUARD
**Status:** Operational (Permissive by default; Strict is opt-in, see below)

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

| Action | Selected for | What happens today |
|---|---|---|
| Alert | every mode | Alert persisted, logged and forwarded. **Real.** |
| IRONCLAD physical port cut | Strict + Critical, IRONCLAD available, drive mapped to a port | Command sent to IRONCLAD. **Real**, but IRONCLAD defaults to the mock server (see below). |
| `ReadOnlyUsb` (key set read-only) | Permissive + Critical | **Not implemented**: the intent is logged, nothing is changed on the device. |
| `QuarantineFile` | Permissive + High | **Not implemented** in the action engine: the intent is logged, no file is moved. |
| `EjectUsb` | Strict + High/Medium | **Not implemented**: the intent is logged (`CM_Request_Device_Eject` not called), the device stays mounted. |
| `BlockAndEject` (software) | Strict + Critical without IRONCLAD cut | **Not implemented**: logs "added to blacklist" then the eject intent; there is no blacklist and no ejection. |

All four stubs report `Success = true` in the action result. Until they are
implemented (debt AGT-USB-001 in `agent/README.md`), **the only action that
changes anything on the workstation is the IRONCLAD cut.**

### What Strict does exactly

- **Critical**: USB GUARD first asks IRONCLAD to cut the USB port physically.
  If IRONCLAD is unavailable, if the drive letter does not map to a port, or
  if the cut fails, it falls back to the software action `BlockAndEject`
  (not implemented, see above).
- **High and Medium**: software ejection (`EjectUsb`, not implemented), without
  IRONCLAD.

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
below fires on every Strict start. An enrolment path (agent CLI and GRID
command, audited) is debt AGT-USB-001.

### What happens when the whitelist cannot be read

The whitelist is read at every device connection, **before** the bootable
check and the content scan. If that read fails (database unavailable, key
unreadable), connection handling stops on the error: **the device is let
through, neither scanned nor blocked, in every mode including Strict** — a
bootable key included. The error is logged as `USB GUARD: Connection handling
failed`. In Strict, the startup check also reads the whitelist and writes this
consequence to the log if it cannot.

## Is a Strict Block Reversible Without Restarting?

| Situation | Without restarting the agent? |
|---|---|
| Key ejected (`EjectUsb`) | Nothing is ejected today (not implemented). Once implemented: replugging the key re-scans it and ejects it again; with no whitelist enrolment path, there is no way to let it through. |
| Port cut by IRONCLAD | **No, and a restart does not restore it either.** `RestoreUsbPortAsync` exists but no command calls it. At agent start, `IronCladStateReconciliationService` restores cut ports only when `FailSafeRestoreOnDisconnect` is on (default `true`) **and** the stored state disagrees with the device — the crash case. A port cut normally, and recorded as cut, stays cut across restarts. |
| Leaving Strict (back to Permissive) | **No.** The configuration is read once, when the USB monitor is built: change `Agent:UsbGuard:OperatingMode` and restart the agent service. |

The development configuration runs Strict. On a development machine, the
owner's own keys are therefore exposed to Strict decisions: today only a
Critical finding on drive `E:` to `H:` triggers a real action (the IRONCLAD
cut, sent to the mock server by default); everything else is logged only.
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
