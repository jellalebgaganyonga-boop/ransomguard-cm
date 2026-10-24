# Real screens — compose GRID, no seeded data

Captured **2026-10-24 18:48 UTC**, 1440x900, from the console at `http://localhost:5175`, whose
Vite proxy forwards to the **real** compose GRID: nginx on `192.168.56.1:8443`, grid-api built
from source, MySQL, Redis. The proxy validates the GRID's certificate against its CA
(`NODE_EXTRA_CA_CERTS`); nothing is skipped. GRID code at `fb41ac6`.

Nothing in these images comes from `seed_demo_data.py`: that script is excluded from the GRID
image and refuses to run outside `GRID_ENVIRONMENT=development` on SQLite. The `0 registered
endpoints` below is a real zero.

| File | What it shows |
|---|---|
| `01_agents_before_enrollment.png` | The Agents page on the real stack before any enrollment: 0 endpoints. |
| `02_add_agent_modal.png` | Add agent: a real enrollment code from the GRID, and the "Download the installer" button. |
| `03_installer_downloaded.png` | After clicking it: the download completed, and no "No installer is published" message — the error the owner reported. |

The file downloaded at that click: `RansomGuard-CM-Installeur-localhost.exe`, 93,958,314 bytes,
starts with `MZ`, ends with the `RGINST01` trailer. It embeds `localhost` because the console was
opened on `localhost`; an installer for the bench VM must embed `192.168.56.1`, which
`deployment/bench/prepare-vm-media.ps1` asks for explicitly.

## Not here yet: the agent and the detection

There is **no screenshot of an enrolled agent or of a SENTINEL alert**, because neither has
happened. The bench VM (`win`, Windows 10, VirtualBox) is at its sign-in screen: the `vboxuser`
password has expired and must be changed, and signing in to it is the owner's step, not an
automated one. The installer disc is prepared and inserted in that VM's DVD drive; see
`docs/DEMO_RUNBOOK.md` for what to do next. Screenshots of the enrollment and the detection
will be added here once they are real.
