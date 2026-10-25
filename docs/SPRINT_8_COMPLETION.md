# Sprint 8 — Completion Report

**Branch:** `sprint8/recovery`
**Head after merges:** `177ffb6`
**Date:** 2026-10-24

## Delivered Features

### B4 — ENTROPY Honest Posture

- Commit: `882599c` (merged to `sprint8/recovery`)
- CI evidence: red run **39708552611** (2 of 919 tests red on `EntropyPostureTests`), green run **39709716541** (CI) + **39709716532** (agent-full)
- ENTROPY now publishes `Degraded / AttributionUnreliable` and logs AGT-ATTR-001 and AGT-EN-002 at startup. The ancient `Publish(Active)` call is the mutation that the red run catches.
- Consequence: a healthy endpoint reads "Partial protection" instead of green until the directory-shift rule exists and signals name a writer.
- **Both conditions met after this sprint** (`ba31a85`, `1351327`, `4ccb71e`): signals name their writer and the directory-shift rule is evaluated. A healthy endpoint reads green once its agent is reinstalled from a freshly published installer and the kernel FileIO session is up; without that session it is still "Partial protection", which remains the honest answer.

### B2.3 — EXFIL Learning Phase Persistence (AGT-EXF-001)

- Commit: `f4240fe` (merged to `sprint8/recovery`)
- CI evidence: branch run **40090618113** (CI) + **40090618047** (agent-full, rerun after one timing-flake on an unrelated `AlertForwardingIntegrationTests` assertion); integration run **40091589914** (CI) + **40091589931** (agent-full)
- Learning start persisted in SQLite `ExfilLearningPhases` (one row, written once, never moved). A restart no longer begins the seven days again.
- Known debt: the baselines themselves remain in memory and a restart still empties them. The module leaves the learning phase on time with baselines to refill, so `VolumeAnomaly` has nothing to compare against until traffic has been seen. Documented in `agent/README.md` AGT-EXF-001, `docs/modules/exfil-watch.md`, and the journal.

### B1.6 — ADR-026 Suspension Closure Reporting

- Commits: `5e10107` (agent), `abe7c31` (GRID), `177ffb6` (console) — merged to `sprint8/recovery`
- CI evidence: branch run **40091642318** (CI) + **40091642238** (agent-full); integration run **40092110694** (CI) + **40092110711** (agent-full)
- Agent reports three closure types: `SentinelProcessResumed`, `SentinelSuspensionEnded`, `SentinelProcessResumeFailed`.
- GRID endpoint `GET /dashboard/agents/{agent_id}/suspensions` lists open suspensions, hides those the agent reported closed, flags `resume_failed`, counts escalations, resolves the canary alert id (not the response alert id).
- Console shows a "Suspended Processes" section on the agent detail page with the resume behind a confirmation. The resume names `alert_id` and `suspension_id`; the GRID derives the PID server-side from the alert's attribution. Analyst and auditor see the list, no resume button.

### ADR-027 — Double-Click Installer

- Commit: `630f283` (tip after rebase; 5 commits merged to `sprint8/recovery`): `3b02ac8` (stub + payload), `ff0ffb0` (GRID builder), `afd7c2c` (installer logic), `03644f9` (console download modal), `630f283` (installer-job CI step exit 0)
- CI evidence: branch run **40089955426** (CI) + **40089955335** (agent-full); integration run **40090581824** (CI) + **40090581784** (agent-full)
- Installer stub (`RGINST01` trailer) + payload built by the GRID for one enrollment code; double-click installs, adds the GRID CA to the machine Root store idempotently, waits 120 s for enrollment, reports exit 4 honestly when the GRID is unreachable. The console offers the installer as a download, nothing to copy or type by hand.

## Test Summary (post-merge, from CI runs on `177ffb6`)

- Agent tests: **938** passed (`RansomGuard.Agent.Tests.dll`, 54 s) + **6** passed stress subset (3 m 29 s). Total 944.
- GRID API tests: **418** passed (3 m 17 s, `grid/tests/api`)
- Console unit tests: **200** passed across **16** test files
- Console E2E (Playwright, chromium only): **79** passed (1 min 20 s)

## Supporting Fixes Made During Phase 1

### `fix(ci): per-GHSA allowlist for npm audit`

- Commits: `b052c48` + `d37395d`
- New runtime advisory landed 2026-10-23: GHSA-mrhx-c7xc-m3w5 (zod memory exhaustion). Fix only in zod v4 (breaking).
- The project rule forbids suppressing by severity. `dashboard/scripts/npm-audit-check.mjs` reads `npm audit --json` and fails on every high/critical runtime advisory except named GHSAs on an in-file allowlist. One entry: zod GHSA-mrhx-c7xc-m3w5, with the risk assessment in `docs/security/dependency-risk-register.md` DEP-DASH-05 and the planned fix (zod 4 upgrade next sprint). An unknown advisory still fails CI.
- `eslint.config.js` got a `scripts/**/*.{js,mjs,cjs}` block with Node globals so the script lints cleanly.

### `fix(grid): resolve mypy warnings in dashboard routes` (Phase 2)

- Commit: `db2469c`
- Four mypy errors in `grid/src/ransomguard_grid/api/v1/routes/dashboard.py` cleared: TenantStatus enum → `.value` for TenantContext, bare `dict` → `dict[str, Any]`, two dead `type: ignore[assignment]` comments removed. The file is at zero mypy errors; 25 other warnings across the GRID source are pre-existing and out of scope per the directive.

## Design Decisions (documented, not changing)

1. **PID derived server-side from the alert's attribution.** The resume command sends `alert_id` + `suspension_id` only. A console user cannot name an arbitrary process. Verified in `dashboard/src/components/agents/suspended-processes-section.tsx:70-78` and `dashboard/src/components/alerts/alert-response.tsx:26-35`.
2. **Alert type names unchanged.** The agent emits `SentinelProcess*`, the GRID maps module from the `Sentinel` prefix. Rewriting to `SENTINEL_PROCESS_*` would break the wire format across all three components at once.
3. **Four EXFIL rule classes stay unregistered.** `TorExitNodeRule`, `SuspiciousDestinationRule`, `UnknownProcessRule`, `EncryptedExfilRule` are present as classes; the threat-intelligence lists they depend on are not fed. Registering them would make them fire on empty data.
4. **ENTROPY degraded, not false green.** `Publish(Degraded / AttributionUnreliable)`. See B4 above.

## Branches Merged

- `review/adr-027-installer`
- `review/exfil-learning-phase`
- `review/adr-026-suspension-live`

## Branches Deleted on Origin

- `wip/installer-1-ci`
- `review/adr-027-installer`
- `review/exfil-learning-phase`
- `review/adr-026-suspension-live`

Older `review/etw-*`, `review/adr-025-*`, `review/v4-*`, `review/v5-*` branches are pre-Sprint 8 historical artifacts untouched by this directive.

## One Flake Observed, Not Fixed

`AlertForwardingIntegrationTests.A_complement_waiting_for_its_alert_is_not_a_transmission_failure` failed once (`40090618047`) on the agent-full workflow after the EXFIL rebase. The test waits 1.5 s for a background task to pick up an enqueued alert, which depends on CI runner timing. It passed on `sprint8/recovery` immediately before, on the previous EXFIL commit, and on the rerun; the EXFIL change does not touch alert forwarding. Treated as a non-reproducing timing flake; a dedicated fix is not part of this directive. If it recurs, raise the delay to 3 s or wait on a counter instead of a fixed sleep.
