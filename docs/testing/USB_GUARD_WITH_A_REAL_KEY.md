# USB GUARD with a real key

**Run on 2026-10-26 on PC-RECEPTION, agent `1.0.0+aa16d56`, with a real USB key belonging to the
owner.** It detected the key in under a second and raised a Critical alert. It did not touch the
key, because it was deliberately put in a mode where it cannot.

## What happened

| | |
|---|---|
| Key | Kingston USB DISK 2.0, FAT32, 7.21 GB, 4.84 GB free — an ordinary key with files on it |
| Attached | `VBoxManage controlvm PC-RECEPTION usbattach <uuid>` |
| Seen by the agent | 20:00:37, as `Class: MassStorage`, `Removable Disk E:` |
| Alert | `UsbGuard`, **Critical**, MITRE **T1091** (Replication Through Removable Media) |
| Reached the GRID | 20:01:00 — two seconds after the alert |
| Action taken | **AlertOnly**. Nothing was done to the key |
| Key afterwards | back on the host, E:, FAT32, **Healthy**, same free space. Nothing written, nothing removed |

From the agent's own log:

```
20:00:37  USB GUARD: Device connected - Class MassStorage, Product "Removable Disk E:"
20:00:37  USB GUARD: MBR boot signature detected on Drive E:
20:00:37  USB GUARD: BOOTABLE USB detected on Drive E:                    [Fatal]
20:00:37  USB ACTION: AlertOnly on "Removable Disk E:"
          (mode Audit, severity Critical,
           reason "Boot signature (MBR/GPT) detected - blocked by policy")
20:00:59  USB GUARD ALERT: Critical - Removable Disk E:, Action: AlertOnly
```

## The finding that matters more than the pass

**It called an ordinary key Critical, and the reason was its boot signature.**

`BlockBootableUsb` is `true` by default, and `BootableUsbDetector` raises Critical on any MBR or
GPT boot signature. Essentially every USB key formatted by Windows has one: it is two bytes,
`0x55AA`, at the end of the first sector, and it is there on a blank key. This key had no malware
on it and was flagged on that alone.

That matters because of what the shipped default would then do. `OperatingMode` ships as
`Permissive`, and in Permissive a Critical finding is not an alert — it is `ReadOnlyUsb`:

| Mode | Critical | High | Lower |
|---|---|---|---|
| Audit | alert only | alert only | alert only |
| **Permissive (shipped)** | **drive made read-only** | **file quarantined** | alert only |
| Strict | block and eject | eject | alert only |

So on a hospital workstation as the product ships today, **a nurse plugging in an ordinary USB
key would find it turned read-only**, with "blocked by policy" as the explanation. That is not a
ransomware defence; it is a support ticket, and the kind that teaches people to work around the
agent.

The test was run in **Audit** mode on purpose, after asking the owner, precisely because the
default would have altered a key that belongs to somebody. That choice is the evidence for the
point: the first time this was pointed at real hardware, the safe thing to do was to turn the
product's own default off.

**Recommended before any hospital sees this:** either stop treating a bare boot signature as
Critical (look for an actual bootloader, or for autorun plus an executable, rather than two bytes
every key has), or stop shipping `Permissive` as the default. Both would be better.

## Repeating it

```powershell
$vb = 'C:\Program Files\Oracle\VirtualBox\VBoxManage.exe'

# The UUID changes every time the device re-enumerates; read it immediately before attaching.
& $vb list usbhost

& $vb controlvm PC-RECEPTION usbattach <uuid>
# ... watch the console and C:\ProgramData\RansomGuard-CM\logs\agent-<date>.log ...
& $vb controlvm PC-RECEPTION usbdetach <uuid>
```

Two things about this bench, both real limits rather than preferences:

- **There is no Oracle Extension Pack installed**, so USB 2.0 and 3.0 passthrough are not
  available. The controller enabled here is USB 1.1 (OHCI), which is built in. It is slow and it
  is enough: the detection happens when the device enumerates, not at reading speed.
- **A workstation needs a USB controller before any of this works.** Both VMs shipped with
  `usb="off"`, and adding one needs the machine shut down:
  `VBoxManage modifyvm PC-RECEPTION --usbohci on`.

## Before pointing it at a key that matters

USB GUARD in its shipped mode can make a key read-only or move files off it. Put it in Audit
first, with `deployment/bench/usb-audit-mode.ps1`, which also reports whether the agent is writing
its log. Audit raises every alert and acts on nothing.
