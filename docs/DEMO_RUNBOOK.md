# Demo runbook — real GRID, real agent, one SENTINEL detection

Local only. The GRID runs in Docker on the host PC; the agent runs on a **bench VM**, never on
the host PC (the host keeps its old `RansomGuardAgent` service disabled and does not trust the
demo CA).

Every output below is marked:

- **Observed** — copied from a real run on 2026-10-24, GRID at `0c55734`.
- **Expected, not yet observed** — what the code says should happen. Steps 6–9 have not been
  run yet: the bench VM's account needs its expired password changed first, which is the owner's
  step (see [Status](#status-on-2026-10-24)).

Credentials for the console: tenant `yde-hospital`, `admin@yde-hospital.cm`, `MotDePasseDemo!2026`.

---

## 0. Know this before you start: VirtualBox is slow on this PC

Docker Desktop and WSL2 use Hyper-V, which takes the CPU's virtualization extensions. VirtualBox
then runs on top of it ("NEM" mode). **Observed** in the VM log:

```
HM: HMR3Init: Attempting fall back to NEM: VT-x is not available
```

In that mode a Windows 10 VM with **1 vCPU pinned at 97% and stopped repainting its screen**
(observed, VM `win`). With **4 vCPUs** it reached a usable sign-in screen. Keep `win` at 4 vCPUs:

```powershell
& "C:\Program Files\Oracle\VirtualBox\VBoxManage.exe" modifyvm win --cpus 4   # VM powered off
```

Run one bench VM at a time. Disabling Hyper-V would make VirtualBox fast but stops Docker, and
with it the GRID: not an option during the demo.

---

## 1. Start the GRID stack

```bash
cd deployment/grid
docker compose -p grid-local --env-file .env up -d
docker ps --format '{{.Names}}\t{{.Status}}\t{{.Ports}}' | grep grid-
```

Use `-p grid-local`: it is the project that owns the MySQL volume. Without it Compose creates a
second, empty stack and stops on a container-name conflict.

**Observed:**

```
grid-nginx     Up       192.168.56.1:80->80/tcp, 192.168.56.1:443->443/tcp, 192.168.56.1:8443->8443/tcp
grid-notifier  Up (healthy)
grid-api       Up (healthy)   8000/tcp
grid-mysql     Up (healthy)
grid-redis     Up (healthy)
```

`192.168.56.1` is the VirtualBox host-only adapter: the VM can reach it, the LAN cannot.
Port 443 is the **agent** channel, 8443 the **console**.

`deployment/grid/.env` (gitignored) must say, explicitly:

| Key | Value | Why |
|---|---|---|
| `BIND_ADDR` | `192.168.56.1` | reachable from the VM, not from the LAN |
| `AGENT_PACKAGES_DIR` | `../../agent/packages` | where step 2 publishes the installer parts |
| `TLS_PUBLIC_DIR` | `./pki/public-local` | console certificate covering `192.168.56.1` |
| `DASHBOARD_DIST_DIR` | `../../dashboard/dist` | the built console nginx serves |
| `GRID_AGENT_AUTH_MODE` | `agent_id` | agents have no client certificate before Sprint 10 mTLS |
| `GRID_ENVIRONMENT` | `development` | the GRID **refuses to start** with `agent_id` unless the deployment says it is development |

The server certificate must cover `192.168.56.1`. Generated once with:

```bash
cd deployment/grid/pki
MSYS_NO_PATHCONV=1 bash ./init-pki.sh "Hopital Regional de Yaounde" CM "IP:192.168.56.1,DNS:grid.local"
mkdir -p public-local && cp server-fullchain.crt public-local/fullchain.crt && cp server.key public-local/privkey.key
```

(`MSYS_NO_PATHCONV=1`: in Git Bash, without it the certificate subject `/C=CM/...` is rewritten
into a Windows path and `openssl req` fails.)

---

## 2. Publish the installer parts (first time, and after any agent change)

```powershell
pwsh deployment/grid/publish-local-installer.ps1
```

**Observed:**

```
agent version : 1.0.0+0a99aa0aa1ab32fc4d44cccf1b889b2d496645e8

Name                                     MB
install.ps1                            0.01
RansomGuard-Agent-win-x64.zip         56.00
RansomGuard-Agent-win-x64.zip.sha256   0.00
RansomGuard-Installer-stub.exe        33.60
RansomGuard-Installer-stub.exe.sha256  0.00

package sha256: e7400836cfa157f04952371d54cadf34a9799457745a79606240e1290849d272
stub    sha256: 61d809b8dec993b338368dc96e963568053d996c12e77de6dd285c56dc541ecb
```

Without these files the console says *"No installer is published on this GRID"*.

---

## 3. Open the console on the real stack

```bash
bash ~/grid-local/start-console-real.sh      # then open http://localhost:5175
```

The Vite proxy validates the GRID's certificate; the CA is given to that one Node process
(`NODE_EXTRA_CA_CERTS`), not to Windows. **Observed:** the same request is refused without the CA
(`SELF_SIGNED_CERT_IN_CHAIN`) and answered with it (`HTTP 200`).

**Observed** (`docs/screenshots/real/01`): Agents, *0 registered endpoints*.

Port 5174 is the other console, on the SQLite database with **synthetic** demo rows. Do not mix
them up: 5175 is the real one.

---

## 4. Check the installer download

In the console: **Agents → Add agent → Download the installer.**

**Observed** (`docs/screenshots/real/02`, `03`): an enrollment code, then a completed download,
no error. The file: 93,958,314 bytes, starts with `MZ`, ends with the `RGINST01` trailer.

---

## 5. Put the installer in the VM

The VM has no Guest Additions (no shared folder), and it cannot use the console before the
installer has added the GRID's CA. So the installer goes in as a **disc**, like a USB key.

The enrollment code is valid **30 minutes**: run this right before step 6.

```powershell
$env:RG_TENANT = 'yde-hospital'
$env:RG_ADMIN_EMAIL = 'admin@yde-hospital.cm'
$env:RG_ADMIN_PASSWORD = 'MotDePasseDemo!2026'
pwsh deployment/bench/prepare-vm-media.ps1 -VmName win
```

**Observed:**

```
[1/3] installer from the GRID
installer     : ...\agent\packages\bench\media\RansomGuard-CM-Installer-192.168.56.1.exe
size          : 93,958,317 bytes
sha256        : 24c8a2b86c046b57dfa388bea6ad8db95397421a59025c9c3062dc855b4a2ce9
grid_base_url : https://192.168.56.1:443
code expires  : 2026-10-24T19:15:17.247133+00:00  (install before this)
[2/3] ISO
        90.8 MB
[3/3] insert into win
```

The SHA-256 changes with every download: each file embeds its own code.

---

## 6. Install on the VM

1. Sign in to the VM.
2. Open the DVD drive (usually `D:`). Read `LISEZ-MOI.txt`.
3. Double-click `RansomGuard-CM-Installer-192.168.56.1.exe`.
4. UAC: *Publisher: Unknown* → **Yes**. From a disc there is no SmartScreen step (no
   Mark-of-the-Web); from a browser download there is: *More info → Run anyway*.

**Expected, not yet observed** (installer exit codes, `docs/modules/installer.md`):

| Exit | Message on screen | Meaning |
|---|---|---|
| 0 | `Poste enrôlé, il apparaît dans la console sous une minute` | installed, running, enrolled |
| 3 | `code expiré, générez-en un nouveau dans la console` | redo step 5 |
| 4 | `Le poste n'est pas enrôlé après 120 s : le GRID ... est-il joignable ...` | see the failure table |

The installer adds the GRID's CA to **the VM's** `LocalMachine\Root`, not the host's.

---

## 7. See the agent in the console

**Expected, not yet observed:** within two minutes, Agents shows the VM's hostname with
protection **PARTIAL** (orange), reason *ENTROPY degraded: attribution unreliable*. That is the
honest state of this build since B4 (`882599c`): ENTROPY cannot name the process that wrote a
file. It is not a fault to fix before the demo.

---

## 8. Trigger SENTINEL

On the VM, double-click **`D:\trigger-sentinel.cmd`**. It writes one byte, five times, three
seconds apart, into **one** of the decoys SENTINEL planted in
`Desktop\RansomGuard-TestZone` (`0001_*`). Nothing else is touched.

**Expected, not yet observed:**

- the window prints its PID and the canary path, then `write 1/5 done`, then **stops**: the agent
  suspended it (`Agent.Sentinel.Response.SuspendProcess = true`, ADR-026);
- if the counter runs to `5/5` without stopping, **the suspension did not happen**. Note it as a
  failure; do not hide it.

---

## 9. See the detection, then resume from the console

**Expected, not yet observed:**

- Alerts: a **Critical** `SentinelCanaryModified`, module SENTINEL, from the VM;
- alert page, *Who did it?*: `powershell.exe — PID <the PID the window printed>` (ADR-025
  attribution, state `resolved`);
- agent page, **Suspended processes**: that PID, with **Resume** (tenant_admin only), behind a
  confirmation. The console sends the alert and suspension ids; the GRID derives the PID itself;
- after Resume: the command shows as sent, the counter in the VM continues to `5/5` and prints
  `finished`, and the process leaves the Suspended list once the agent reports it resumed.

Screenshots of these screens go in `docs/screenshots/real/` once they are real.

---

## If it fails

| Symptom | Cause | Fix |
|---|---|---|
| Console: *No installer is published on this GRID* | `AGENT_PACKAGES_DIR` points at a folder without the stub and the zip (it held only `install.ps1`) | step 2, and `AGENT_PACKAGES_DIR=../../agent/packages` |
| `docker compose up`: *container name "/grid-mysql" is already in use* | started without `-p grid-local` | always `docker compose -p grid-local ...` |
| grid-api unhealthy: *GRID_AGENT_AUTH_MODE=agent_id is accepted with GRID_ENVIRONMENT=development only* | the GRID's own guard | `GRID_ENVIRONMENT=development` — the bench is a development deployment |
| Image build: *failed to compute cache key: short read* | no `.dockerignore`: the whole repo was the build context | fixed by `.dockerignore` (`ee2827b`) |
| `init-pki.sh`: *subject name is expected to be in the format /type0=value0...* | Git Bash rewrote `/C=CM/...` into a path | `MSYS_NO_PATHCONV=1` |
| `curl` cannot validate the GRID (`schannel ...`) | Git for Windows' curl uses schannel, ignores `--cacert` | validate with Python/Node and the CA file, as the scripts do; never `-k` |
| Installer exit **4** | the VM cannot reach `https://192.168.56.1:443` | the VM's NIC is NAT, which normally reaches the host's IPs; if not, add a host-only adapter: `VBoxManage modifyvm win --nic2 hostonly --hostonlyadapter2 "VirtualBox Host-Only Ethernet Adapter"` (VM off) |
| Installer exit **3** | code older than 30 min | rerun step 5 |
| An agent installed from an installer downloaded **before `e38b0a6`** never enrolls | that installer embeds `https://host:8443`; nginx answers an enrollment on 8443 with `405` | download a new one: the console now asks for port 443 |
| VM stuck on its lock screen, CPU pinned | VirtualBox in NEM mode with 1 vCPU | power off, `--cpus 4` (section 0) |
| VM sign-in: *Your password has expired and must be changed* | the `vboxuser` password expired | sign in with the current password and set a new one; keep it with the bench notes |
| Console on 5175: `502` | the Vite proxy refused the certificate | start it with `start-console-real.sh`, which sets `NODE_EXTRA_CA_CERTS` |

---

## Status on 2026-10-24

| Step | State |
|---|---|
| 1. Stack up on `192.168.56.1` | done, observed |
| 2. Installer parts published | done, observed |
| 3. Console on the real stack | done, observed |
| 4. Download the installer in the console | done, observed — the reported bug is fixed |
| 5. Disc in the VM | done, observed |
| 6. Install on the VM | **not done**: the VM account's password has expired; signing in is the owner's step |
| 7–9. Enrollment, detection, suspension, resume | **not done**, depend on 6 |
