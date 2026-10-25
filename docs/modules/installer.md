# Installer module — the double-click installer (ADR-027)

**Module:** `RansomGuard.Agent.Installer`
**Status:** operational since 2026-10-22; unsigned (AGT-INST-002 debt).

## What the installer does

An administrator opens the console, clicks **Add agent**, downloads one file
(`RansomGuard-CM-Installeur-<grid-host>.exe`, about 85 MB), carries it to the
hospital workstation, double-clicks. Windows prompts the SmartScreen "unknown
publisher" warning and the UAC consent; after both, the installer unpacks the
agent, writes `appsettings.json` with the enrollment code and GRID address
embedded in the download, adds the GRID's certificate authority to the machine
root store, starts the Windows service, waits up to 120 s for the first
enrollment, and reports plainly whether it succeeded.

No PowerShell command to copy. No `curl` with `-k`. No GRID certificate to
install by hand. If the GRID is unreachable the installer exits 4 and says so.

## How it works technically

### Payload layout (format `RGINST01`)

The CD publishes two files:
- `RansomGuard-Agent-win-x64.zip` — the agent package (unchanged artefact).
- `RansomGuard-Installer-stub.exe` — a self-contained .NET 8 single-file
  executable with `requireAdministrator` in its manifest. Published with
  `-r win-x64 --self-contained -p:PublishSingleFile=true`.

When an administrator clicks **Download the installer**, the GRID streams the
stub, then the payload, then a 56-byte trailer, in one response:

```
[stub bytes][zip: RansomGuard-Agent-win-x64.zip][config: UTF-8 JSON][trailer]
```

| Offset in trailer | Size | Content |
|---|---|---|
| 0 | 8 | ASCII `RGINST01` |
| 8 | 8 | zip length, little-endian int64 |
| 16 | 8 | config length, little-endian int64 (max 65 536) |
| 24 | 32 | SHA-256 of `zip ‖ config` |

A .NET single-file host finds its bundle by an offset written at publish time,
so bytes appended after the stub never disturb it. A CI step on `agent-full`
proves this on the published artefact before anything else: a bare stub exits
2; one with a payload appended starts, verifies the trailer, and exits 0 under
`--verify-payload`.

### Config JSON (snake_case)

| Field | Required | Content |
|---|---|---|
| `grid_base_url` | yes | HTTPS URL agents will call (same host the console was opened on) |
| `otp` | yes | 5-min enrollment code the console just generated |
| `otp_expires_at` | yes | ISO-8601 UTC |
| `tenant_code` | — | copied into `appsettings.json` for the log |
| `agent_package_sha256` | — | hex, verified against the embedded zip before extraction |
| `grid_ca_pem` | yes | the GRID's certificate authority, PEM-encoded, added to the machine root store |

### What the stub does, in order

1. **Read its own payload** (`Environment.ProcessPath`), verify the trailer and
   SHA-256. On a missing or altered payload: `"installeur incomplet,
   retéléchargez-le"`, **exit 2**, nothing written.
2. **Refuse an expired code** (`otp_expires_at` in the past) before touching the
   workstation: `"code expiré, générez-en un nouveau dans la console"`, **exit 3**.
3. **Stop and delete any existing `RansomGuardAgent` service.** OPS-001's dormant
   service on the owner's machine is not concerned: the installer is not run
   there.
4. **Extract** the agent to `C:\Program Files\RansomGuard-CM` (AGT-INST-001:
   never elsewhere); write `appsettings.json` with `BaseUrl`, `EnrollmentOtp`,
   `Hostname`, `Data Source`, `LogFilePath`; close the data folders to SYSTEM
   and Administrators only (AGT-KEY-001). The `%EACHUSERPROFILE%` watched paths
   stay as shipped.
5. **Add `grid_ca_pem` to the machine root store** (`X509Store(StoreName.Root,
   StoreLocation.LocalMachine)`) only if the thumbprint is not already there.
   Logs the thumbprint to the screen and to
   `C:\ProgramData\RansomGuard-CM\logs\install.log`.
6. **Create and start the Windows service** (LocalSystem, automatic start,
   restart-on-failure), wait for `Running`, then wait up to 2 minutes for
   `agent-state` (or the log) to report `enrolled`. On success: `"Poste enrôlé,
   il apparaît dans la console sous une minute"`, **exit 0**. On timeout:
   `"Le poste n'est pas enrôlé après 120 s : le GRID <url> est-il joignable
   depuis ce poste, le code est-il encore valide ?"`, **exit 4**.
7. Console window stays open: `"Appuyez sur une touche pour fermer"`.

### Exit codes

| Code | Meaning |
|---|---|
| 0 | installed, service running, enrolled within 120 s |
| 2 | payload missing or altered — download again |
| 3 | enrollment code expired |
| 4 | installed and running, but not enrolled after 120 s |
| 64 | invalid argument on the command line |

### GRID endpoint

`POST /api/v1/dashboard/agents/installer`, body `{otp, host, port}`.
`tenant_admin` only, the code must belong to the caller's tenant and still be
valid, else 404. Streams `Content-Disposition: attachment;
filename="RansomGuard-CM-Installeur-<host>.exe"`. No cache. Logs
`installer_downloaded` to `UserActionLog` without the code. The code remains
single-use: the downloaded file is one 5-minute, one-workstation ticket.

### Console flow

In **Add agent**: the code and its countdown stay; the former PowerShell
one-liner and SHA-256 are gone. Three lines of instruction instead:

> 1. Copy the installer to the endpoint (USB key, network share).
> 2. Double-click. Windows shows "Unknown publisher": "More info", then "Run
>    anyway", then "Yes" to the administrator prompt.
> 3. The endpoint shows up here in green within two minutes. Nothing to type.

A discreet link labelled "Scripted deployment (GPO, SCCM)" points to the
`install.ps1` documentation, kept and tested.

## Configuration options and defaults

The installer takes no configuration file. Every value it needs is in the
embedded config JSON (above). Defaults the GRID applies when building the
payload:

| Setting | Default | Explicit on command line |
|---|---|---|
| Install directory | `C:\Program Files\RansomGuard-CM` | not configurable; checked at step 4 |
| Data directory | `C:\ProgramData\RansomGuard-CM` | not configurable |
| Service name | `RansomGuardAgent` | not configurable |
| Enrollment timeout | 120 s | hard-coded; the retry loop keeps running after exit |
| CA store | `LocalMachine\Root` | hard-coded; idempotent |
| Watched paths | `%EACHUSERPROFILE%\Desktop`, `%EACHUSERPROFILE%\Documents`, `%EACHUSERPROFILE%\Desktop\RansomGuard-TestZone` | from the shipped `appsettings.json`, not overridden by the installer |

## Known limitations (what the installer does not do)

- **Unsigned.** SmartScreen shows "Windows protected your PC / Unknown publisher".
  Documented in the console text with the exact two-click bypass. Signing is
  **AGT-INST-002** debt, to land with the MSI (AGT-INST-001).
- **No silent install.** No `/quiet`, no `/norestart`, no `/log` command-line
  switches. Scripted deployment goes through `install.ps1` instead.
- **No uninstaller yet.** `sc.exe delete` + folder removal by hand. An MSI
  package would carry an uninstall entry.
- **CA added at machine scope.** The GRID's CA is trusted machine-wide while
  the agent is installed; removal requires manual action on the certificate
  store (also AGT-PKI-001 debt until a tighter scoping lands).
- **Expired code = three retries, no automatic refresh.** The user must
  generate a new code in the console. The installer never calls the GRID for
  a fresh one; the download is a one-shot ticket.

## Test evidence

| Scope | What | CI run |
|---|---|---|
| Stub reads its own payload | `--verify-payload` on bare stub exits 2; after `--append`, exits 0 and prints `grid.ci.invalid` (host) with masked code | agent-full job `Agent — double-click installer (ADR-027)` on run **40089955335** (branch `review/adr-027-installer` at `630f283`) and run **40090581784** (`sprint8/recovery` after merge) |
| Full install from double-click | runner's agent-full job: installer extracts to `C:\Program Files\RansomGuard-CM`, writes `appsettings.json`, adds the CI test CA to `LocalMachine\Root`, starts the service, waits 120 s, exits 4 honestly when GRID unreachable | Run **39692722924** on `review/adr-027-installer` before merge, repeated on **40089955335** |
| GRID endpoint builds and streams | `grid/tests/api/test_agent_installer_download.py` on CI | `grid/tests/api` suite, 418 passed on **40092110694** |
| Console download modal | `dashboard/e2e/agents.spec.ts` chromium runs on sprint8/recovery | 79 passed on **40092110694** |

Last verified green on `sprint8/recovery` HEAD `60b345f`, CI run **40092773549**, agent-full **40092773590**.

## Not covered

- Real-hospital installation on a Windows machine held by Fabiola has not been
  rerun since the AGT-PATH-001 rebase. The CI runners cover the extract,
  trust, service-start and exit-4 cases; a staff workstation with Bitdefender
  installed may trigger extra prompts the CI runners do not.
- Signed-installer flow (Authenticode, EV certificate, Windows timestamp)
  is **AGT-INST-002** and not implemented.
