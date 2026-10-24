# Changelog — RansomGuard-CM

All notable changes to this project are documented here, following
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) conventions.
Versions follow Semantic Versioning. Pre-1.0 versions use `0.SPRINT.PATCH`.

---

## [Unreleased]

---

## [0.9.0] — 2026-06-22 — Sprint 7: Tenant Operator Console Dashboard

**Branch:** `feat/sprint7-phase0-dashboard`
**Full dashboard changelog:** `dashboard/CHANGELOG.md`

### Added

- **EPIC-AUTH (8 SP):** Login page (tenant code + email + password), in-memory
  JWT storage, Axios 401 interceptor with refresh-and-retry, 30-minute idle
  timeout → SessionExpiredModal, boot-sequence refresh → role-based routing.
- **EPIC-DASHBOARD (13 SP):** Role-based dashboards (executive / operational /
  read-only), metrics cards (total agents, active agents, alerts 24h, critical
  24h), AppShell with RBAC-filtered sidebar, bilingual FR/EN (react-i18next).
- **EPIC-ALERTS (18 SP):** Paginated alert list (50/page, URL-synced filters),
  alert detail with MITRE technique + severity + status history, Acknowledge
  flow (optimistic update), Close flow (category dropdown + 20-char notes min).
- **EPIC-AGENTS (16 SP):** Agent inventory with heartbeat freshness chip,
  30s auto-refresh, stale banner; agent detail page; Isolate command modal
  (tenant_admin only, 20+ char reason + ack checkbox).
- **EPIC-USERS (13 SP):** User list with role editor and disable/enable toggle,
  Create user modal, self-disable guard (DOM-level), self-demotion guard.
- **EPIC-AUDIT (8 SP — re-scoped):** Ed25519 agent chain integrity log,
  sequence gap detection, date range + agent_id filters, CSV export.
  *(A user-action log deferred to Sprint 8 — backend endpoint does not yet exist.)*
- **E2E + Accessibility:** 42 Playwright tests (Chromium), 10 axe-core WCAG 2.2
  AA scans, WCAG severity badge contrast fixes, form label association fixes.
- **Phase 0 backend:** `GET /api/v1/dashboard/me` and
  `POST /api/v1/dashboard/users/{id}/enable` endpoints added to the GRID server.

### Security

- **GRID-SEC-001** (tracked debt): refresh token stored in JSON body response,
  not an HttpOnly cookie — both tokens lost on page reload until Sprint 8 fix.
- **GRID-SEC-002** (frontend guard): self-disable prevented at DOM level;
  backend has no auto-protection against disabling self.
- Dev dependency advisories (GHSA-67mh-4wv8-2f99): esbuild/vite/vitest chain —
  dev-only, not in production bundle; Vite v5→v8 breaking upgrade deferred to Sprint 8.

### Tests

- Unit (Vitest): **83/83**
- E2E (Playwright, Chromium): **42/42**
- Production audit: `npm audit --omit=dev` → **0 vulnerabilities**

---

## [0.8.0] — 2026-06-10 — Sprint 6: GRID Server

**Tag:** `v0.8.0-grid`

### Added

- FastAPI + SQLAlchemy 2.0 async backend (Python 3.12) for multi-tenant
  agent telemetry collection and alert management.
- 21 database models, 22 tables, MySQL 8.4 with full migration history.
- mTLS mutual authentication for agent ↔ server communication (nginx 1.27).
- Multi-tenant isolation: every query scoped to `tenant_id` from JWT.
- 10/10 isolation tests (no cross-tenant data leakage).
- Docker Compose deployment stack (4 containers: api, mysql, redis, nginx).
- 94 backend tests passing.

### Architecture

- ADR-023: FastAPI over Django/Flask — async-first, OpenAPI native.
- ADR-024: Multi-tenant design — tenant_id foreign key on all data models.

---

## [0.7.0] — 2026-05-27 — Sprint 5: IRONCLAD (Software-Only)

**Tag:** `v0.7.0-ironclad`

### Added

- IRONCLAD backup/recovery module (software-only, ADR-022).
- Encrypted, immutable backup chain with Ed25519 signing.
- Guided recovery wizard (CLI).
- 525 C# agent tests passing (all 8 modules).

---

## [0.6.0] — 2026-05-10 — Sprint 4: EXFIL ACTION + USB GUARD hardening

**Tag:** `v0.6.0-exfil-action`

### Added

- EXFIL ACTION module (data movement prevention).
- USB GUARD hardening (allowlist + hardware ID enforcement).
- Full STRIDE threat model coverage for all 8 modules.

---

## [0.5.0] — 2026-04-20 — Sprint 3: EXFIL WATCH + INDICATOR REMOVAL + THREAT INTEL

**Tag:** `v0.5.0-sprint3`

### Added

- EXFIL WATCH module (network exfiltration detection).
- INDICATOR REMOVAL module (log tampering detection).
- THREAT INTEL module (online + offline IoC feed integration).
- Performance benchmarks: all modules within SLO thresholds.

---

## [0.4.0] — 2026-04-01 — Sprint 2: GENEALOGY + Persistence Layer

**Tag:** `v0.4.0-sprint2`

### Added

- GENEALOGY module (process ancestry tracking).
- SQLite persistence layer for alert history.
- Windows Service installer.

---

## [0.3.0] — 2026-03-15 — Sprint 1: SENTINEL + ENTROPY

**Tag:** `v0.3.0-sprint1`

### Added

- SENTINEL module (suspicious process detection, 3-signal convergence rule).
- ENTROPY module (high-entropy write detection for ransomware behaviour).
- Medical process whitelist (false positive guard).
- Core agent framework (.NET 8 Windows Service).
