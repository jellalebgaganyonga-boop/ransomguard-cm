# How to use this guide

You play two roles:

- **The vendor (RansomGuard-CM)**: on your **host PC** you run the platform (the GRID server and the console).
- **The hospital (the customer)**: the virtual machine **PC-RECEPTION** (Windows 10) is a hospital reception computer. You install the agent on it and see how the platform protects it.

Do the steps in order. After each step, compare what you see with **"You should see"**. Tick the box when it matches. If it does not match, write down what you saw and go to the **"If something goes wrong"** table at the end.

All passwords are in the file `deployment\bench\credentials.local.md` on your host PC. That file is never sent to GitHub.

| What | Value |
|---|---|
| Console address | `http://localhost:5175` |
| Organization code (hospital) | `yde-hospital` |
| Hospital administrator | `admin@yde-hospital.cm` |
| Hospital analyst | `analyst@yde-hospital.cm` |
| Hospital auditor | `auditor@yde-hospital.cm` |
| Second customer (clinic) | code `test-clinic`, `admin@test-clinic.cm` |
| Virtual machine | **PC-RECEPTION**, Windows 10 |

**Signing in to the virtual machine:** the Windows user is **`vboxuser`** and it has **no password**. The sign-in screen shows only a **Sign in** button — press Enter.

> If it answers **"The username or password is incorrect"**, a stray keypress has been taken as a password. Click **OK** and press Enter again, once. There is no password to get wrong.

**Time needed:** about an hour. Much of it is waiting, by design: the five Resume clicks in Step 11 take roughly a minute each, because the agent asks the GRID for commands on a schedule, and Step 16 waits five minutes before a workstation is called disconnected.

# Part A — Start the platform (host PC)

## Step 0 — One command that does Part A for you

If you would rather not do steps 1 to 5 by hand, this does them and then checks the result:

```powershell
cd C:\Users\Fabiola\source\repos\ransomguard-cm
pwsh scripts\demo-ready.ps1
```

It starts whatever is not already running — Docker, the GRID, the console, and **PC-RECEPTION** — waits for each to answer, runs the full health check, and ends with one of two words.

**You should see:** a green **DEMO READY**, then the console address, the organization codes and the account emails. ☐

> If it says **NOT READY** it lists each problem and the command that fixes it. Do those, run it again, and carry on only once it says DEMO READY.
>
> It never prints a password. It tells you the file the passwords are in.
>
> It writes what it did to `docs\testing\demo-ready-<date>.md`, so there is a record of the state the machine was in before the demonstration.

Then go to **Step 5** to sign in to the workstation, or read steps 1 to 4 to see what it did.

## Step 1 — Start Docker Desktop

1. Open **Docker Desktop** and wait until it says **Engine running**.

**You should see:** the Docker whale icon in the taskbar is steady (not animated). ☐

## Step 2 — Start the GRID server

Open **PowerShell** on the host PC and type:

```powershell
cd C:\Users\Fabiola\source\repos\ransomguard-cm\deployment\grid
docker compose -p grid-local --env-file .env up -d
docker ps --format "{{.Names}}  {{.Status}}"
```

**You should see:** five lines, all **Up**, most of them **(healthy)**: `grid-nginx`, `grid-api`, `grid-notifier`, `grid-mysql`, `grid-redis`. Wait up to 2 minutes for "healthy". ☐

> You may also see `rg-redis-demo`, left over from an earlier demo. It is not part of the GRID and can be ignored.

## Step 3 — Start the console

In **Git Bash** type:

```bash
bash ~/grid-local/start-console-real.sh
```

Then open a browser at **`http://localhost:5175`**.

**You should see:** the RansomGuard-CM **Sign in** page, in English. ☐

## Step 4 — Sign in as the hospital administrator

Organization code `yde-hospital`, email `admin@yde-hospital.cm`, password from `credentials.local.md`. Click **Sign in**.

**You should see:** the **Executive dashboard**. Go to **Agents**: PC-RECEPTION is **not** in the list yet. ☐

> You will see **PC-LAB**, the other bench workstation, already there and active. That is the machine this guide was rehearsed on; leave it alone.

# Part B — Install the agent on the hospital computer

## Step 5 — Start PC-RECEPTION

1. Open **Oracle VirtualBox**.
2. Select **PC-RECEPTION** and click **Start**.
3. At the Windows sign-in screen, press **Enter** (the user `vboxuser` has no password).

**You should see:** the Windows 10 desktop. In `C:\Users\Public\Documents\Hospital-FICTIONAL\` there are four folders of invented hospital documents — Patients, Laboratory, Pharmacy, Imaging, 50 files each. ☐

## Step 6 — Prepare the installer disc (host PC)

The installer contains a one-time enrollment code. **It expires after 15 minutes**, so do this step just before Step 7.

In **PowerShell** on the host PC:

```powershell
cd C:\Users\Fabiola\source\repos\ransomguard-cm
$env:RG_TENANT = 'yde-hospital'
$env:RG_ADMIN_EMAIL = 'admin@yde-hospital.cm'
$env:RG_ADMIN_PASSWORD = '<admin password from credentials.local.md>'
powershell -ExecutionPolicy Bypass -File deployment\bench\prepare-vm-media.ps1 -VmName PC-RECEPTION
```

**You should see:** `[1/3] installer from the GRID`, `[2/3] ISO`, `[3/3] insert into PC-RECEPTION`, then **Done**. The line `code expires` gives the time limit. ☐

> Why a disc? The virtual machine is like a hospital computer receiving the installer on a USB key. The disc plays the role of the USB key.

## Step 7 — Install the agent (inside PC-RECEPTION)

1. Open **File Explorer** → **This PC** → the DVD drive **RANSOMGUARD** (usually `D:`).
2. Open `README.txt` and read it.
3. Double-click **`RansomGuard-CM-Installer-192.168.56.1.exe`**.
4. Windows asks for permission ("Publisher: Unknown"). Click **Yes**. *(The installer is not signed yet. This is a known limit.)*

   > The prompt can take **up to half a minute** to appear: the installer is a 94 MB single file and unpacks itself before Windows can ask. Nothing seems to happen in the meantime. Wait — do not double-click a second time, or you will install twice.

5. Wait. A window shows the steps 1/6 to 6/6.

**You should see:** a final line **"Workstation enrolled (agent …). It appears in the console within a minute."**, then "Press any key to close." ☐

> If the DVD drive still shows the contents of a previous disc, eject it (right-click the drive → Eject) and open it again. Windows does not always notice that the disc was swapped.

## Step 8 — See the hospital computer in the console (host PC)

In the console, go to **Agents**. Wait up to 2 minutes (the page refreshes every 30 seconds).

**You should see:**

- **PC-RECEPTION** in the list, status **Active**. ☐
- Protection **PARTIAL** (orange). This is correct: EXFIL WATCH is still learning the normal network traffic. ☐
- **Alerts: 0.** Installing the agent must not raise any alert. ☐

Click **PC-RECEPTION** to open its page. In the modules table:

| Module | Expected state |
|---|---|
| SENTINEL | active |
| ENTROPY | active |
| GENEALOGY | active |
| USB GUARD | active |
| EXFIL WATCH | degraded — learning phase |
| ANTI-TAMPERING | inactive — not implemented |

☐ All rows match.

# Part C — Test the protection

## Step 9 — Simulate an attack on a decoy file (inside PC-RECEPTION)

The test script is harmless. It writes into **one decoy file** that the agent planted for this purpose, five times, a few seconds apart. It touches nothing else.

1. Open the DVD drive **RANSOMGUARD** again.
2. Double-click **`trigger-sentinel.cmd`**.

**You should see:**

- The window prints `This PowerShell PID : <number>`. **Write this number down: ______** ☐
- It prints `write 1/5 done`, then **it stops**. The window looks frozen. That is the agent **suspending** the process. ☐

## Step 10 — See the detection in the console (host PC)

1. Go to **Alerts**.

**You should see:** a **Critical** alert `SentinelCanaryModified` from PC-RECEPTION, and a `SentinelProcessSuspended` alert. **One `SentinelCanaryModified` per write**, not two. ☐

2. Open the `SentinelCanaryModified` alert.

**You should see:**

- **Who did it?** → `powershell`, with the **same PID** you wrote down. ☐
- **What started it?** → the chain that led to the write, newest last: **`explorer -> cmd -> powershell`**. This is GENEALOGY. It answers the question attribution cannot: not only which program wrote to the decoy, but what launched it. ☐
- **Response**: the process was suspended. ☐

> The chain is captured while the writer is still alive and sent to the GRID just after the alert, so it can appear a few seconds after the alert itself. Reload the page if the section is not there yet.
>
> The middle link is `cmd` because Windows runs a `.cmd` file through `cmd.exe`, whether you double-click it or type it. Rehearsed both ways on PC-RECEPTION on 2026-10-26: the chain is the same.

3. Open the **PC-RECEPTION** agent page.

**You should see:** a **Suspended processes** block with `powershell (PID …)` and a **Resume** button. ☐

## Step 11 — Resume the process (host PC, then PC-RECEPTION)

1. Click **Resume**, then confirm.
2. Look at the trigger window in PC-RECEPTION.

**You should see:** the counter continues (`write 2/5 done`). It stops again almost immediately: each new write to the decoy is detected again, which is correct. Click **Resume** again each time — **five times in all**. ☐

> Allow up to a minute between clicking Resume and the counter moving. The agent asks the GRID for commands on a schedule; it is not instant.

At the end the window prints **`finished. A pause above was the suspension; the resume let the script continue.`**, and the **Suspended processes** block in the console is **empty**. ☐

# Part D — Test the hospital accounts and the SaaS isolation

## Step 12 — The auditor can only look

Sign out (account menu, top right). Sign in as `auditor@yde-hospital.cm`.

**You should see:** the **Compliance view**. The auditor can read alerts and logs, but has **no Resume button** and cannot change anything. ☐

## Step 13 — The analyst can handle alerts

Sign out. Sign in as `analyst@yde-hospital.cm`. Open an alert and **acknowledge** it.

**You should see:** the alert status changes. The analyst cannot manage users. ☐

## Step 14 — Another customer sees nothing of the hospital

Sign out. Sign in with organization code `test-clinic` and `admin@test-clinic.cm`.

**You should see:** **0 agents, 0 alerts**. The clinic cannot see the hospital's computers or alerts. This is the SaaS promise: each customer only sees its own data. ☐

## Step 15 — Everything is recorded

Sign in again as `admin@yde-hospital.cm`. Open **Audit log** and **User actions**.

**You should see:** your sign-ins, the Resume actions and the acknowledged alert, with date and user. ☐

# Part E — Test a lost connection

## Step 16 — Turn the hospital computer off and on

1. In PC-RECEPTION: **Start → Power → Shut down**.
2. In the console, Agents page: wait about 5 minutes.

**You should see:** PC-RECEPTION becomes **Disconnected**. ☐

> It takes about 5 minutes, not seconds: the GRID waits that long before deciding a workstation is gone, so a brief network hiccup does not raise a false alarm.

3. Start PC-RECEPTION again in VirtualBox and sign in.

**You should see:** PC-RECEPTION comes back to **Active** within about 2 minutes. ☐

# Part F — What NOT to expect (and what to say to the jury)

| Topic | The truth |
|---|---|
| Green "PROTECTED" | Not on a new computer: EXFIL WATCH learns the network first. PARTIAL is the honest state. |
| USB GUARD | Active, but not tested here: a real USB key must be passed into the VM. |
| EXFIL WATCH | Cannot raise alerts before the end of its learning period. |
| GENEALOGY | Works: the "What started it" chain is on the alert page (Step 10). It is captured while the writer is still alive, so an alert whose writer vanished instantly carries no chain. |
| IRONCLAD (backup) | Code exists, tested only with a simulated Arduino. Not connected to real hardware. |
| Anti-tampering | Not built yet. |
| Installer | Not signed: Windows says "Publisher: Unknown". |
| The name in the console | A workstation's name is recorded when it enrolls and is not updated afterwards. If you rename the machine, the console keeps showing the old name until it enrolls again. |

# If something goes wrong

| What you see | What it means | What to do |
|---|---|---|
| Console: "No installer is published on this GRID" | Installer parts not published | Ask Claude Code to run `deployment\grid\publish-local-installer.ps1` |
| Installer says the code has expired | More than 15 minutes since Step 6 | Do Step 6 again, then Step 7 |
| Installer says the workstation is not enrolled after 120 s | Either the VM cannot reach the GRID, or this machine was enrolled before | See the two rows below |
| The GRID log shows `409 Conflict` on enroll | This workstation already has an agent record | In the console, open the old agent and **Decommission** it, then do Step 6 and Step 7 again |
| PC-RECEPTION never appears in Agents | Agent service not running | Note the installer's last message and give it to Claude Code |
| The trigger says "No canary" | Agent not installed, or not started yet | Wait 1 minute after install and run it again |
| The counter goes to 5/5 without stopping | The suspension did not happen | Do not hide it: take a screenshot and give it to Claude Code |
| Console page shows `502` | The GRID API is still starting | Wait a minute and reload; check Step 2 shows `grid-api` healthy |
| The VM is frozen or very slow | Not enough CPU | Shut it down, give it 4 CPUs in VirtualBox settings, start again |
| Installer: "Start not confirmed by sc.exe … starting it once more" | Windows gave up waiting at 30 s, which is its limit and not ours. The installer waits up to 120 s and starts the service a second time | Nothing. Let it finish; it says "Service started" when the service is up |
| VirtualBox says the VM "aborted", or the window goes black and the VM stops | A VirtualBox failure, not the agent. It happened once on this bench while the host was busy building and testing at the same time | Close the message, start the VM again, sign in. The agent starts by itself; check the console shows the workstation Active |

# Taking screenshots

- Host PC or VM: press **Windows + Shift + S** and select the area.
- Save each screenshot with the step number, for example `step10_alert.png`.

# Repeating the test

To start again from a clean computer, put the reset script on a disc and run it inside the VM:

```powershell
pwsh deployment\bench\build-script-iso.ps1 -VmName PC-RECEPTION -ScriptName reset-agent.ps1 -Label RESET
```

then, in PC-RECEPTION, open PowerShell **as administrator** and run `D:\run.cmd`. That removes the agent, its data and the decoys it planted, and leaves the documents, the `nurse` account and the machine name alone. Then **decommission the old agent in the console** (otherwise the GRID refuses the new enrollment with `409`) and start again from **Step 6**.
