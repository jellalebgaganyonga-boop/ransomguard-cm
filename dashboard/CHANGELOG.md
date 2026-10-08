# Changelog — RansomGuard-CM Dashboard

All notable changes to the dashboard frontend are documented here.

## [0.9.0] — 2026-06-22 — Sprint 7 Console (feat/sprint7-phase0-dashboard)

### Added

**EPIC-AUTH (8 SP)**
- Login page with tenant code + email + password (AC1.1.x)
- In-memory token storage (access token only; refresh token in-memory per GRID-SEC-001)
- Axios interceptor for silent token refresh + retry on 401 (AC1.3.x)
- Idle timeout: 30-minute inactivity → SessionExpiredModal → logout (AC1.2.3)
- Boot sequence: POST /auth/refresh on mount → role-based routing (AC1.3.4)

**EPIC-DASHBOARD (13 SP)**
- Role-based dashboard: executive (tenant_admin), operational (security_analyst), read-only (read_only_auditor) (AC2.1.x)
- Metrics cards: total agents, active agents, alerts 24h, critical alerts 24h (AC2.2.x)
- AppShell: Sidebar + Header with responsive navigation (AC2.3.x)
- Bilingual FR/EN with react-i18next (all strings in locales/fr.json + en.json)

**EPIC-ALERTS (18 SP)**
- Paginated alerts list: 50/page, sort by detected_at/severity/status, filters synced to URL (AC3.1.x)
- Alert detail page with MITRE technique, severity badge, status history (AC3.2.x)
- Acknowledge flow: AcknowledgeModal → POST /status {new_status: "Investigating"} with optimistic update (AC3.3.x)
- Close flow: CloseModal with category dropdown + 20-char minimum notes → POST /status (AC3.4.x)
- RBAC: read_only_auditor sees read-only notice, no write buttons (AC3.3.3, AC3.4.3)

**EPIC-AGENTS (16 SP)**
- Agents list: hostname, OS, version, heartbeat freshness chip, status (AC4.1.x)
- Heartbeat freshness: green <5min, yellow 5–60min, red >60min (AC4.4.1)
- Auto-refresh every 30s with stale agent warning banner (AC4.4.2–3)
- Agent detail: full profile + tabs (Overview, Commands placeholder) (AC4.2.x)
- Isolate command modal: tenant_admin only, reason 20+ chars + ack checkbox (AC4.3.x)

**EPIC-USERS (13 SP)**
- Users list: email, status badge, last login, role editor, disable/enable toggle (AC5.1.x)
- Create user modal with email, full name, provisional password, initial roles (AC5.2.x)
- Self-disable guard: Désactiver button absent from own row (GRID-SEC-002) (AC5.3.x)
- Self-demotion guard: cannot remove own tenant_admin role (AC5.4.3)

**EPIC-AUDIT (8 SP — re-scoped)**
- Audit log page: Ed25519 agent chain integrity view (re-scoped from user-action log)
- Sequence gap detection (frontend-only `detectSequenceGaps`) (AC6.1.4)
- Date range + agent_id filters (AC6.1.2–3)
- CSV export (frontend-only, no backend endpoint) (AC6.3.1)
- RBAC: security_analyst redirected to /dashboard (AC6.2.1)

**E2E + Accessibility (Day 9–10)**
- 42 Playwright tests on Chromium (100% passing)
- WCAG 2.2 AA compliance: severity badge contrast, form label associations
- SelectContent z-index fix: z-popover (400) > z-modal (300) for Select-in-Dialog

### Changed
- SelectContent z-index: `z-dropdown` → `z-popover` to appear above Dialog scrim
- All `text-text-tertiary` low-contrast occurrences → `text-text-secondary` (WCAG fix)
- Severity badge colors: orange-30 (#834D11) and red-40 (#B12626) for WCAG contrast

### Security
- **GRID-SEC-001** (tracked debt): refresh_token in JSON body, not HttpOnly cookie — both tokens lost on reload by design until Sprint 8 backend fix
- **GRID-SEC-002** (frontend guard): self-disable prevented in DOM; DisableEnableModal has defense-in-depth guard
- **Dev dependency advisories** (GHSA-67mh-4wv8-2f99): esbuild dev server vulnerability in vite/vitest chain — dev-only, NOT in production bundle; fix requires breaking Vite v5→v8 upgrade, deferred to Sprint 8

### Tests
- Unit: **83/83** passing (Vitest)
- E2E: **42/42** passing (Playwright, Chromium)
- Production audit: **0 vulnerabilities** (`npm audit --omit=dev`)

---

## [0.8.4] — 2026-06-20 — Sprint 7 Day 6–8 (Agents + Users + Audit)

See git tag `v0.8.4-day6-8-agents-users-audit`.

## [0.8.1] — 2026-06-17 — Sprint 7 Phase 0 Backend

See git tag `v0.8.1-backend-phase0`.

## [0.8.0] — 2026-06-14 — Sprint 6 GRID Server

See git tag `v0.8.0-grid`.
