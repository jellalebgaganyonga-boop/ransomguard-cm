# USB GUARD with a real key

**Status on 2026-10-26: waiting for a USB key.** Nothing here is a pass. USB GUARD reports
`active` on both workstations and has never been shown a real device, because none has been
plugged into this machine. What follows is everything needed to do it in about five minutes when
one is.

## Why it has not been done

Two things are missing, and only one of them is in our hands.

1. **PC-LAB has no USB controller.** `VBoxManage showvminfo PC-LAB` reports `usb="off"`. A virtual
   machine with no controller cannot be given a device at all. Adding one needs the machine shut
   down; it is not a setting that can be changed while it runs.
2. **No USB key is plugged into the host.** `VBoxManage list usbhost` shows an Intel Bluetooth
   radio and a Realtek device built into the laptop, and no removable drive. There is nothing to
   pass through.

## Preparing the workstation (once, with the VM shut down)

```powershell
$vb = 'C:\Program Files\Oracle\VirtualBox\VBoxManage.exe'

# The machine has to be off. Shut it down from inside Windows, or:
& $vb controlvm PC-LAB acpipowerbutton

# A USB 2.0 controller. Not 3.0: that needs the Oracle extension pack, which is a separate
# install and a separate licence, and a USB key does not need the speed.
& $vb modifyvm PC-LAB --usbehci on

& $vb startvm PC-LAB --type gui
```

Confirm it took: `VBoxManage showvminfo PC-LAB --machinereadable | findstr usb` should read
`usb="on"` or show `usbehci="on"`.

## When a key is plugged into the host

```powershell
$vb = 'C:\Program Files\Oracle\VirtualBox\VBoxManage.exe'

# 1. Find it. A key appears as a Mass Storage device; note its UUID.
& $vb list usbhost

# 2. Give it to the workstation. Windows inside the VM sees it arrive.
& $vb controlvm PC-LAB usbattach <uuid>

# 3. Watch what USB GUARD does, in the console, on the PC-LAB page and in Alerts.

# 4. Give it back to the host.
& $vb controlvm PC-LAB usbdetach <uuid>
```

Eject the key in Windows before step 4, as on any machine: a drive pulled mid-write loses what
was being written, and the point of the exercise is not to damage somebody's key.

## What to record

Write down what actually happened, not what was hoped for:

- what the console shows on the PC-LAB page for USB GUARD, before and after;
- any alert raised, with its type, time and what it names;
- whether the key stayed usable, was blocked, or was quarantined;
- a screenshot of the console at each stage, into `docs/screenshots/real/`.

If USB GUARD does nothing at all, that is the result and it is written down as the result. The
module reporting `active` means it is loaded and watching; it has never been shown that it acts.

## What is already known without a key

`system-check.ps1` reports USB GUARD as **NOT TESTABLE HERE** with the reason "needs a USB device
passed through to the VM; the module itself reports active". That line is not a pass and is not
meant to read as one, which is why it is a different word from PASS.
