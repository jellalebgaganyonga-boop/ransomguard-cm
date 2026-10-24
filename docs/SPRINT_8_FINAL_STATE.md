# Sprint 8 — Final State

**Branch:** `sprint8/recovery`
**HEAD:** `041f805`
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

## 6. Confirmation

**All tests pass. All docs up to date. Branch ready for jury demonstration from a code-review standpoint.**

What this does NOT claim: that an end-to-end hospital-floor run on real hardware has been rehearsed. That is a separate exercise (DemoScenarios tool + bench setup + `TESTS_JURY.md`), deferred by earlier directives and still not done.
