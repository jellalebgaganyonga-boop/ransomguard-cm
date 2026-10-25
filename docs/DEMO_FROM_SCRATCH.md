# Demo from scratch — a brand-new machine, VM or physical

Two paths depending on where the agent will run:

- **A new VirtualBox VM you create today** → §A (host-only network, nothing on the LAN).
- **An actual physical machine** (another workstation, a laptop) → §B (LAN or Tailscale).

Both end at the same place: the console shows the real machine, SENTINEL fires on a canary file, the process gets suspended, you click Resume. **Everything on this document is for the demo bench only, nothing goes to a production server.**

Credentials for the console: tenant `yde-hospital`, email `admin@yde-hospital.cm`, password `MotDePasseDemo!2026`.

---

## 0. Prerequisites, once (host PC)

Everything after assumes these are done. They don't change between demos.

```bash
# 1. Docker Desktop running
docker ps                                         # should not error

# 2. Compose stack up (grid-api + nginx + mysql + redis + notifier)
cd deployment/grid
docker compose -p grid-local --env-file .env up -d
docker ps | grep grid-                            # 5 containers "healthy"

# 3. Installer parts published (zip + stub + install.ps1 + .sha256)
pwsh deployment/grid/publish-local-installer.ps1

# 4. The real-stack console (not the demo-data one)
bash ~/grid-local/start-console-real.sh           # http://localhost:5175
```

Sign in on **http://localhost:5175** with the credentials above. Agents page should load.

---

## A — new VirtualBox VM you create today

**Why this works.** The compose stack already binds its ports to `192.168.56.1`, the VirtualBox host-only adapter. Any VM given a host-only NIC on that same adapter gets an IP in `192.168.56.0/24` from the VirtualBox DHCP server and reaches `192.168.56.1` directly. No LAN involvement, no Tailscale.

### A.1 Create the VM

VirtualBox → **New** → set:

- Name: anything (e.g. `hospital-pc`)
- Type: **Microsoft Windows**, Version: **Windows 10 (64-bit)** or **Windows 11 (64-bit)**
- RAM: 4096 MB
- CPUs: **4** — important. 1 vCPU is unusable on this host because VirtualBox runs in NEM mode under Hyper-V and leaves the guest frozen (observed on VM `win` before the fix).
- Disk: 40 GB dynamic
- At the "Unattended Install" step, if offered, let it finish Windows Setup for you. Set a password you remember — you'll sign in later.
- Install from a Windows ISO you have, as usual.

Once Windows is installed and signed in:

```powershell
# From the HOST, add a host-only adapter to the VM (powered off).
& "C:\Program Files\Oracle\VirtualBox\VBoxManage.exe" controlvm "hospital-pc" acpipowerbutton   # or shut it down from inside
& "C:\Program Files\Oracle\VirtualBox\VBoxManage.exe" modifyvm "hospital-pc" --nic2 hostonly `
    --hostonlyadapter2 "VirtualBox Host-Only Ethernet Adapter" --nictype2 82540EM --cableconnected2 on
& "C:\Program Files\Oracle\VirtualBox\VBoxManage.exe" startvm "hospital-pc" --type gui
```

Sign in. The second NIC should get a 192.168.56.x address from the host-only DHCP. Verify in the VM, in a PowerShell:

```powershell
Test-NetConnection 192.168.56.1 -Port 443 -InformationLevel Quiet
# → True
```

If **False**: the host-only adapter has no DHCP enabled. Either assign a static IP:
```powershell
# Admin PowerShell, in the VM
$a = Get-NetAdapter | Where-Object InterfaceDescription -like '*Intel*' | Select-Object -First 1
New-NetIPAddress -InterfaceIndex $a.InterfaceIndex -IPAddress 192.168.56.101 -PrefixLength 24
```
or enable DHCP on the host-only adapter from VirtualBox: **File → Preferences → Network → Host-only Networks → Edit → DHCP Server → Enable**.

### A.2 Prepare an installer disc, then insert it

```powershell
# On the host
$env:RG_TENANT = 'yde-hospital'
$env:RG_ADMIN_EMAIL = 'admin@yde-hospital.cm'
$env:RG_ADMIN_PASSWORD = 'MotDePasseDemo!2026'
pwsh deployment/bench/prepare-vm-media.ps1 -VmName hospital-pc
```

The script fetches a fresh installer from the GRID (code valid **5 minutes**), builds an ISO with it, the trigger, and a `LISEZ-MOI.txt`, and inserts it into the VM's DVD drive.

### A.3 Install the agent in the VM

1. Open **This PC** → double-click the DVD (usually `D:`).
2. Double-click **`RansomGuard-CM-Installer-192.168.56.1.exe`**.
3. Windows → **Yes** on the UAC prompt (Publisher: Unknown — it's your own unsigned build).
4. The installer runs 6 steps. Last line: **"Poste enrôlé (agent …). Il apparaît dans la console sous une minute."** — press any key.
5. In the console: **Agents** → within ~60 s your new VM appears with protection **PARTIAL** (orange, honest since B4).

### A.4 Trigger a real detection

In the VM, double-click **`trigger-sentinel.cmd`** on the disc. The PowerShell window prints its PID, then `write 1/5 done`, then **freezes** — SENTINEL caught the write, attributed it, and suspended the process.

In the console → **Alerts**: a CRITICAL `SentinelCanaryModified` (write) and a CRITICAL `SentinelProcessSuspended` (response).

In **Agents → your VM → Suspended processes**: `powershell (PID …)` with a **Resume** button. Click it, confirm, the window in the VM processes its queued writes and prints `finished`.

---

## B — a physical machine, not a VM

**Why this is different from A.** The physical machine isn't on the host-only adapter; it must reach the GRID through something that crosses machines: Tailscale (what the production bench uses, works anywhere on Earth) or the LAN (works only inside the same office network).

### B.1 Pick a reachable address for the GRID

Pick **one** of:

| Choice | Address on this PC | Reachable from | Needs |
|---|---|---|---|
| **Tailscale** (recommended) | `100.68.173.117` (your host's Tailscale IP) | Any device also on your tailnet (laptops at home, phones, a VM) | Tailscale installed and signed in on both this PC and the physical machine |
| **LAN** | `192.168.1.120` (your host's Wi-Fi/Ethernet IP) | Any device on the same local network as this PC | Both machines on the same Wi-Fi or LAN; no VPN separating them |

Verify from the physical machine first, before touching anything:
```powershell
# On the physical machine
Test-NetConnection 100.68.173.117 -Port 443 -InformationLevel Quiet
# → True
```

If `False`, pick the other option.

> The host's Tailscale IP shows in the Tailscale app's status (right-click the tray icon). Treat it as data, not a secret — it only works from inside your tailnet.

### B.2 Add that address to the GRID's TLS certificate

The certificate the GRID serves must include the chosen address in its SAN list, or the agent will refuse to connect (TLS validation, no `-k`, no bypass). One command rebuilds the server cert with extra SANs, preserving the Root CA:

```bash
cd deployment/grid/pki
# Back up the current server cert
STAMP=$(date +%Y%m%d-%H%M%S)
for f in server.key server.crt server-fullchain.crt; do [ -f "$f" ] && mv -v "$f" "$f.$STAMP.bak"; done

# Regenerate with the extra SANs
MSYS_NO_PATHCONV=1 bash ./init-pki.sh "Hopital Regional de Yaounde" CM \
  "IP:192.168.56.1,IP:100.68.173.117,IP:192.168.1.120,DNS:grid.local"
# Trim the IPs to only the ones you need; keep 192.168.56.1 so §A still works.

# Refresh the browser-facing (dashboard) cert too
cp server-fullchain.crt public-local/fullchain.crt
cp server.key public-local/privkey.key
cd ../..

# Reload nginx so the new cert takes effect (no stack restart needed)
docker exec grid-nginx nginx -s reload
```

Verify the new SANs:
```bash
openssl x509 -in deployment/grid/pki/server.crt -noout -ext subjectAltName
```

### B.3 Open the GRID on the chosen address

The compose stack already binds its listener to `192.168.56.1` only (BIND_ADDR in `deployment/grid/.env`). To let it serve the LAN or the tailnet too, change BIND_ADDR to `0.0.0.0` and restart just nginx:

```bash
# In deployment/grid/.env, set:
#   BIND_ADDR=0.0.0.0
# then
cd deployment/grid
docker compose -p grid-local --env-file .env up -d --force-recreate nginx
docker ps | grep grid-nginx    # listening on 0.0.0.0:443, 0.0.0.0:8443, 0.0.0.0:80
```

> **Important.** With BIND_ADDR=0.0.0.0, your GRID is reachable from anyone on the same network. The GRID still requires authentication (JWT, bcrypt'd passwords) and the agent channel still validates its own CA; nothing becomes anonymous. But if you don't want neighbours on the Wi-Fi to see the sign-in page, keep it 192.168.56.1 and use Tailscale instead — Tailscale is per-user authenticated at the network layer, so only devices on *your* tailnet can even reach the port.
>
> Flip BIND_ADDR back to `192.168.56.1` after the demo if you don't need wider access.

### B.4 Build an installer that points at the chosen address

The console embeds the GRID address when you click **Download the installer**. The address it embeds is `window.location.hostname` — the name the browser is on. So:

- Open the console on the physical machine's own name of this PC:
  - Tailscale: `http://100.68.173.117:5175` (if 5175 is bound to 0.0.0.0 too — see below)
  - LAN: `http://192.168.1.120:5175`
- Sign in, click **Add agent**, **Download the installer**. The downloaded file will be named `RansomGuard-CM-Installer-100.68.173.117.exe` (or the LAN IP, depending).

Or, if you want to drive the download from the host and carry the file to the physical machine, do it from the command line with the right host:

```powershell
# On the host
$env:RG_TENANT = 'yde-hospital'
$env:RG_ADMIN_EMAIL = 'admin@yde-hospital.cm'
$env:RG_ADMIN_PASSWORD = 'MotDePasseDemo!2026'
python deployment\bench\fetch_installer.py `
  --grid https://192.168.56.1:8443 `
  --agent-host 100.68.173.117 --agent-port 443 `
  --ca deployment\grid\pki\ca.crt --out agent\packages\downloaded
```

The script prints the file path, size, SHA-256, and `grid_base_url` (should read `https://100.68.173.117:443`). Copy that `.exe` to the physical machine (USB key is cleanest — a disc carries no Mark-of-the-Web, meaning Windows asks for UAC but not SmartScreen).

### B.5 Install on the physical machine, trigger, resume

Steps A.3 and A.4, same procedure. The installer's step 5 adds the GRID's CA to that machine's `LocalMachine\Root`. Enrollment at step 6 goes to `https://100.68.173.117:443` (or whatever address you picked). The agent appears in the console within ~60 s under the physical machine's hostname.

**Clean-up when the demo is over,** on the physical machine, Admin PowerShell:
```powershell
Stop-Service RansomGuardAgent -Force -ErrorAction SilentlyContinue
sc.exe delete RansomGuardAgent
Remove-Item -Recurse -Force "C:\Program Files\RansomGuard-CM"
Remove-Item -Recurse -Force "C:\ProgramData\RansomGuard-CM"
Get-ChildItem "$env:USERPROFILE\Desktop" -Filter '0001_*' -Force | Remove-Item -Force
Get-ChildItem "$env:USERPROFILE\Documents" -Filter '0001_*' -Force | Remove-Item -Force
Remove-Item -Recurse -Force "$env:USERPROFILE\Desktop\RansomGuard-TestZone" -ErrorAction SilentlyContinue
Get-ChildItem Cert:\LocalMachine\Root | Where-Object { $_.Subject -like '*RansomGuard-CM*' } | Remove-Item
```

---

## What changed recently that this document captures

- **Enrollment code is 5 minutes**, not 30. If an installer is prepared and nobody double-clicks within 5 minutes, run `prepare-vm-media.ps1` (or `fetch_installer.py`) again.
- **The console has a light/dark toggle** (sun/moon button in the header, next to Notifications). Choice is per browser, stored in `localStorage`.
- **Clicking your avatar** opens the account menu with real profile fields (role, hospital, language, last sign-in), all read from `/dashboard/me`.
- **The installer bundles the Visual C++ 2015-2022 runtime** itself. A fresh Windows 10 that never had the redistributable installed can still run the agent.
- **nginx's agent channel routes `/commands`** too, so a Resume issued in the console reaches the agent.

## If it fails

| Symptom | Where to look |
|---|---|
| Installer says "No installer is published on this GRID" | §0 step 3 — run `publish-local-installer.ps1` |
| Installer step 6 fails (sc 1053) | Should not happen since the VC runtime bundle. If it does: on the target, check `C:\ProgramData\RansomGuard-CM\logs\install.log` as admin, and the agent exe output as described in `docs/screenshots/real/README.md`. |
| Agent enrolls but no alerts arrive | Check `docker logs grid-api --since 2m` for `POST /api/v1/agents/.../alerts` 200 lines; if 404 or 405, the nginx `agent.conf` route may be out of date (`commands` was added recently) |
| Suspended Processes list keeps the entry after Resume | Known open item — the agent completes NtResumeProcess but does not post the closure alert. Documented in `docs/screenshots/real/README.md`. The suspension in the OS is actually lifted; only the console display is stale. |
| VM is at 1 vCPU and freezes at login | A.1 — give it 4 vCPUs. On this host Docker/Hyper-V forces VirtualBox into NEM mode which is slow. |
| TLS error from the physical machine | §B.2 — the GRID's cert must list the address the physical machine is using. Regenerate with the right SAN, reload nginx. |
