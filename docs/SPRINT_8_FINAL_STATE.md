# Sprint 8 — Final State

**Branch:** `sprint8/recovery`
**HEAD:** `8ddccde` (this file; CI **40105961682** and agent-full **40105961942** both green on it)
**Date:** 2026-10-24

## 1. Test counts (from the last green CI run)

Pulled from the CI runs on `sprint8/recovery` at `041f805`:

| Suite | Count | Evidence |
|---|---|---|
| Agent (`RansomGuard.Agent.Tests.dll`, Category!=Stress) | **938 passed** / 0 failed / 0 skipped (54 s) | agent-full run **40095519670** |
| Agent Stress subset | **6 passed** / 0 failed / 0 skipped (3 min 29 s) | same run |
| GRID API (`grid/tests/api`) | **418 passed** (3 min 17 s) | CI run **40095519676** |
| GRID full (`grid/tests` incl. db + services + workers) | **413 passed, 1 skipped** (7 min 26 s, local) | local run of `pytest tests/ -x -q` on `041f805` |
| Console unit (`vitest run`) | **200 passed** / 16 test files | CI run **40095519676** and local |
| Console E2E (Playwright chromium) | **79 passed** (2 min 30 s locally; 1 min 20 s on CI) | CI run **40095519676** and local |

Both workflows on `041f805` green.

## 2. Commits made during this session

In order of merge onto `sprint8/recovery`:

### Session commits made before this directive
- `882599c` — fix(agent): ENTROPY reports degraded, it names no process and skips rule 3 (B4)
- `db2614c` — docs(journal): B4 ENTROPY passe à dégradé, avec ses runs

> **Superseded after this sprint.** B4 made ENTROPY declare two real gaps rather than hide them.
> Both have since been closed — `ba31a85`, `1351327`, `4ccb71e`: an ENTROPY alert names the
> process that wrote the file (AGT-ATTR-001) and rule 3, the directory-wide shift, is evaluated
> (AGT-EN-002). The module now reads `active` whenever the kernel FileIO session is running, and
> falls back to `degraded / attribution_unreliable` when it is not — the posture follows the
> capability at runtime instead of being fixed at the build. B4's principle is unchanged: the
> console never reads green for something the agent cannot do.

### Three review branches merged (Phase 1 of the directive)
- `630f283` tip of five installer commits (`3b02ac8`, `ff0ffb0`, `afd7c2c`, `03644f9`, `630f283`) — ADR-027 double-click installer
- `f4240fe` — feat(agent): EXFIL WATCH learning phase survives a restart (AGT-EXF-001, B2.3)
- `5e10107` — feat(agent): a suspension that closes is reported to the GRID (ADR-026, B1.6 groundwork)
- `abe7c31` — feat(grid): the console can list the processes an agent reports as suspended (ADR-026, B1.6)
- `177ffb6` — feat(console): Suspended Processes on the agent page, with the resume (ADR-026, B1.6)

### Supporting fixes needed to pass CI
- `b052c48` — fix(ci): per-GHSA allowlist for npm audit, with zod GHSA-mrhx-c7xc-m3w5 accepted
- `db2469c` — fix(grid): resolve mypy warnings in dashboard routes (Phase 2)
- `d37395d` — fix(ci): lint the npm-audit-check script with Node globals

### Documentation close-out
- `60b345f` — chore(docs): Sprint 8 completion report, journal rows for the final merges
- `041f805` — docs(modules): installer module page (ADR-027) with payload layout, flow, exit codes, test evidence

## 3. Issues found and fixed during the sweep

1. **No branch was fast-forwardable** when Phase 1 started: `sprint8/recovery` had moved ahead with B4 and the B4 journal row. All three review branches rebased onto the tip.
2. **One rebase conflict** in `dashboard/e2e/agents.spec.ts`: the installer branch pre-dated the English conversion and the removal of the mobile-overflow helper. Resolved by taking the installer-download flow with English assertions.
3. **New npm advisory** published 2026-10-23: GHSA-mrhx-c7xc-m3w5 on `zod < 4.6.6` (memory exhaustion). The project rule forbids suppressing by severity, so `dashboard/scripts/npm-audit-check.mjs` was added — it reads `npm audit --json` and fails on any high/critical runtime advisory except named GHSAs on an in-file allowlist. One accepted entry today, with register entry DEP-DASH-05.
4. **The audit script failed eslint** on first push because the flat config did not include Node globals for `.mjs` scripts. Added a `scripts/**/*.{js,mjs,cjs}` block to `eslint.config.js` with `globals.node`; the `/* eslint-env node */` comment is a flat-config no-op and was removed.
5. **Four mypy errors** on `grid/src/ransomguard_grid/api/v1/routes/dashboard.py`: TenantStatus enum → `.value` for TenantContext, bare `dict` → `dict[str, Any]`, two dead `type: ignore[assignment]` comments removed. The file is at zero mypy errors; 25 other warnings across the GRID source are pre-existing and out of directive scope.
6. **One timing flake** on `AlertForwardingIntegrationTests.A_complement_waiting_for_its_alert_is_not_a_transmission_failure` on CI runner timing (1.5-s sleep). Rerun was clean. Not touched; flagged in the completion report with a suggested fix for next time.
7. **Missing module doc for the installer** — `docs/modules/installer.md` did not exist. Written with the required five sections (what / how / config / known limits / test evidence).
8. **Stale local worktree** `C:\Users\Fabiola\source\repos\rg-inst` for the merged installer branch — removed.

## 4. Branches merged, branches deleted

Merged into `sprint8/recovery`:
- `review/adr-027-installer`
- `review/exfil-learning-phase`
- `review/adr-026-suspension-live`

Deleted on origin:
- `wip/installer-1-ci`
- `review/adr-027-installer`
- `review/exfil-learning-phase`
- `review/adr-026-suspension-live`

Historical review branches (`review/etw-*`, `review/adr-025-*`, `review/v4-*`, `review/v5-*`) left as-is; they are pre-Sprint-8 artefacts outside this directive's scope.

## 5. Documentation state

Up to date on this HEAD:
- `docs/SPRINT_8_COMPLETION.md` — merged-features summary with every real commit hash and CI run ID.
- `docs/SPRINT_8_FINAL_STATE.md` — this file.
- `docs/JOURNAL_DES_ACTIONS.md` — evidence rows for every B-series deliverable, the audit allowlist, and the mypy fix.
- `docs/modules/sentinel.md`, `entropy.md`, `exfil-watch.md`, `usb-guard.md`, `installer.md` — all present; posture tables honest.
- `docs/security/dependency-risk-register.md` — DEP-DASH-05 (zod) added.
- `agent/README.md` — AGT-EXF-001 updated to reflect the persisted phase start and the still-in-memory baselines.

## 6. Running it locally

Dev-only, on this machine, nothing deployed anywhere. Two processes:

| Process | Address | Backing store |
|---|---|---|
| GRID (uvicorn, `ransomguard_grid.main:app`) | `http://127.0.0.1:8443` | SQLite at `%LOCALAPPDATA%\rg-demo-dev.db`, migrated with Alembic and seeded with `grid/scripts/seed_initial_data.py` |
| Console (Vite dev server) | `http://localhost:5174`, proxying `/api` to `127.0.0.1:8443` | — |

Every setting is explicit in a local env file, no silent defaults:
`GRID_JWT_SECRET_KEY`, `GRID_DATABASE_URL`, `GRID_ENVIRONMENT=development`,
`GRID_AGENT_AUTH_MODE=agent_id`, `GRID_THREAT_INTEL_SIGNING_KEY_PATH`,
`GRID_THREAT_INTEL_PACKAGES_DIR`, `GRID_REDIS_URL=redis://127.0.0.1:6379/0`.

**Redis is not optional.** Since GRID-SEC-004/005 moved sessions out of process
memory, `POST /auth/login` answers `503 Session store unavailable` when it
cannot reach Redis — the GRID refuses to issue a session it cannot revoke. A
container with a published host port covers it locally
(`docker run -d --name rg-redis-demo -p 127.0.0.1:6379:6379 redis:7-alpine`).
The `docker-compose` stack's own `grid-redis` publishes no host port, so it
serves the containerised GRID only, not a uvicorn on the host.

Verified on this HEAD, through the Vite proxy:

| Call | Result |
|---|---|
| `POST /api/v1/auth/login` | 200, `roles: ["tenant_admin"]` |
| `GET /api/v1/dashboard/me` | 200 |
| `GET /api/v1/dashboard/metrics/summary` | 200 — `total_agents: 0` |
| `GET /api/v1/dashboard/agents` | 200 — `total: 0` |
| `GET /api/v1/dashboard/alerts` | 200 — `total: 0` |

### The Agent, locally

The three product projects build clean on this machine, `dotnet build` with
0 warnings and 0 errors each: `RansomGuard.Agent.Core`,
`RansomGuard.Agent.Service`, `RansomGuard.Agent.Installer`.

`RansomGuard.Agent.Tests` does **not** build locally. It fails
`MSB3021: Access to the path 'RansomGuard.Agent.Tests.dll' is denied` — the
endpoint-protection file lock on the repo folder. The agent suite's evidence
path is therefore CI, where it runs green (938 + 6, §1). Building the whole
solution in one command fails for this one project; build the three product
projects individually to get a clean local build.

The agent was **not started** on this machine. The dormant `RansomGuardAgent`
service here stays disabled (OPS-001), so a local service start is out of
scope by standing instruction, not by failure.

**The console is live and empty, and that is the honest state.** No agent has
enrolled against this database, so there are no agents, no alerts and no
posture rows to show. The counts above are real zeros, not placeholders, and
nothing was seeded to make the screens look populated. Filling them means
enrolling a real agent through the ADR-027 installer flow and letting SENTINEL
fire on the canary test zone; that run has not been done (see §7).

## 7. Confirmation

**All tests pass. All docs up to date. Branch ready for jury demonstration from a code-review standpoint.**

What this does NOT claim: that an end-to-end hospital-floor run on real hardware has been rehearsed. That is a separate exercise (DemoScenarios tool + bench setup + `TESTS_JURY.md`), deferred by earlier directives and still not done.
