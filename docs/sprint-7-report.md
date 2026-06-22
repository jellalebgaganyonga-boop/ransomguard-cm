# Sprint 7 Report — RansomGuard-CM Console Dashboard

**Sprint dates:** 2026-06-14 → 2026-06-22
**Branch:** `feat/sprint7-phase0-dashboard`
**Target tag:** `v0.9.0-console` (pending explicit confirmation)

---

## Executive Summary

Sprint 7 delivered the complete **tenant operator console dashboard** for RansomGuard-CM. All 6 EPICs from the sprint plan were implemented and tested. The validation gate passes:

| Gate | Result |
|------|--------|
| `npm run typecheck` | ✅ 0 errors |
| `npm run lint` | ✅ 0 warnings |
| `npm test -- --run` | ✅ 83/83 unit tests |
| `npx playwright test --project=chromium` | ✅ 42/42 E2E tests |
| `npm audit --audit-level=high --omit=dev` | ✅ 0 production vulnerabilities |

---

## Story Points Delivered

| EPIC | SP | Status |
|------|----|--------|
| Phase 0 Backend (/me, /enable) | — | ✅ Complete |
| Day 1: Dashboard Bootstrap | — | ✅ Complete |
| EPIC-AUTH | 8 SP | ✅ Complete |
| EPIC-DASHBOARD | 13 SP | ✅ Complete |
| EPIC-ALERTS | 18 SP | ✅ Complete |
| EPIC-AGENTS | 16 SP | ✅ Complete |
| EPIC-USERS | 13 SP | ✅ Complete |
| EPIC-AUDIT | 8 SP | ✅ Complete (re-scoped) |
| **Total** | **76 SP** | |

---

## Acceptance Criteria Coverage

### EPIC-AUTH (AC1.x) — 8 SP
- ✅ AC1.1.1–5: Login form (tenant code + email + password), validation, error states
- ✅ AC1.2.3: 30-minute idle timeout → SessionExpiredModal → redirect to login
- ✅ AC1.3.1–5: Axios 401 interceptor with refresh token retry, deduplication, redirect on failure
- ✅ AC1.3.4: Boot sequence refresh → role-based routing

### EPIC-DASHBOARD (AC2.x) — 13 SP
- ✅ AC2.1.1–3: Role-based dashboard variants (admin/analyst/auditor)
- ✅ AC2.2.1–4: Metrics cards with real-time data
- ✅ AC2.3.1–3: AppShell with sidebar navigation filtered by role

### EPIC-ALERTS (AC3.x) — 18 SP
- ✅ AC3.1.1–5: Paginated list, sort, URL-synced filters, empty state, RBAC row actions
- ✅ AC3.2.1–6: Alert detail with MITRE, severity, status, multi-tenant 404 guard
- ✅ AC3.3.1–3: Acknowledge flow with optional note, optimistic update, RBAC guard
- ✅ AC3.4.1–3: Close flow with category + 20-char notes validation, RBAC guard

### EPIC-AGENTS (AC4.x) — 16 SP
- ✅ AC4.1.1–3: Agents list table with status filter, RBAC (isolate = admin only)
- ✅ AC4.2.1–3: Agent detail page with full profile
- ✅ AC4.3.1–5: Isolate command modal (tenant_admin only, 20+ char reason, ack checkbox)
- ✅ AC4.4.1–3: Heartbeat freshness chip, 30s auto-refresh, stale warning banner

### EPIC-USERS (AC5.x) — 13 SP
- ✅ AC5.1.1–3: Users list with status badges, role editor, disable/enable
- ✅ AC5.2.1–4: Create user modal with email, full name, password, initial roles
- ✅ AC5.3.1–2: Self-disable guard (absent from DOM + modal defense-in-depth)
- ✅ AC5.4.3: Self-demotion guard (cannot remove own tenant_admin role)

### EPIC-AUDIT (AC6.x) — 8 SP (re-scoped)
- ✅ AC6.1.1–4: Audit log table (agent_id link, sequence, key ID, received_at), gap detection
- ✅ AC6.2.1: RBAC — security_analyst → /dashboard redirect
- ✅ AC6.3.1: CSV export (frontend-only)

> **RE-SCOPE NOTE:** The PRD v2 described a user-action log (actor_user_id, action_type). The actual backend AuditLog model is an **agent Ed25519 chain integrity log** (sequence_number, signing_key_id). A user-action log requires a new backend endpoint — deferred to Sprint 8.

---

## Technical Debt (Tracked)

| ID | Description | Sprint |
|----|-------------|--------|
| GRID-SEC-001 | Refresh token in JSON body instead of HttpOnly cookie — both tokens lost on reload | Sprint 8 |
| GRID-SEC-002 | disable_user backend has no auto-protection against disabling self — frontend-only guard | Sprint 8 |
| GAP-01 | AgentItem.enrolled_at missing in backend schema — not shown on agent detail | Sprint 8 |
| GAP-03 | Heartbeat timeline endpoint not implemented — Heartbeats tab descoped | Sprint 8 |
| GAP-08 | GET /users has no `roles` field — no Roles column in users list | Sprint 8 |
| DEP-001 | esbuild/vite/vitest dev-only vulnerability (GHSA-67mh-4wv8-2f99) — requires Vite v5→v8 breaking upgrade | Sprint 8 |

---

## E2E Test Coverage (42 tests)

| File | Tests | Status |
|------|-------|--------|
| `e2e/agents.spec.ts` | 7 | ✅ 7/7 |
| `e2e/alerts.spec.ts` | 8 | ✅ 8/8 |
| `e2e/audit.spec.ts` | 6 | ✅ 6/6 |
| `e2e/dashboard.spec.ts` | 5 | ✅ 5/5 |
| `e2e/idle-timeout.spec.ts` | 2 | ✅ 2/2 |
| `e2e/login.spec.ts` | (included) | ✅ |
| `e2e/users.spec.ts` | 6 | ✅ 6/6 |

### Key E2E Fixes Applied (Day 9–10)
- **Zod schema**: `artifacts`/`status_history` → `.nullable().optional()` (null from backend)
- **SelectContent z-index**: `z-dropdown`(100) → `z-popover`(400) — Select dropdown now renders above Dialog scrim (`z-modal`=300)
- **Badge contrast**: severity-high orange-30 (#834D11), severity-critical red-40 (#B12626) — WCAG 4.5:1
- **Audit log labels**: added `htmlFor`/`id` to all filter inputs (WCAG label association)
- **SessionExpiredModal**: uses `role="alertdialog"` — tests updated to `getByRole('alertdialog')`
- **RBAC redirect assertions**: changed `getByRole('table').not.toBeAttached()` to page-specific element checks (dashboard also has tables)
- **Create user route**: pattern `**/api/v1/dashboard/users*` to match query-param URLs
- **Acknowledge optimistic update**: stateful route mock returns Investigating after mutation
- **Close modal status assertion**: `getByText('Résolu', { exact: true })` to avoid matching "résolution" in labels
- **ProtectedRoute RBAC**: `<Navigate to="/dashboard" replace />` instead of ForbiddenPage
- **idle handleWarning**: stable no-op `useCallback(() => {}, [])` — prevents idle timer reset at 29min

---

## Architecture Decisions

| ADR | Decision |
|-----|----------|
| ADR-FE-001 | React 18 + TypeScript strict + Vite 5 |
| ADR-FE-002 | shadcn/ui + Radix primitives + TailwindCSS design tokens |
| ADR-FE-003 | Zustand for auth state (in-memory only) |
| ADR-FE-009 | STRIDE threat model: XSS → never persist tokens to localStorage |
| ADR-FE-010 | Optimistic UI for alert status mutations with onError revert |

---

## Validation Gate Checklist

```
✅ npm run typecheck     — 0 TypeScript errors
✅ npm run lint          — 0 ESLint warnings
✅ npm test -- --run     — 83/83 unit tests
✅ npx playwright test --project=chromium — 42/42 E2E tests
✅ npm audit --audit-level=high --omit=dev — 0 production vulnerabilities
⚠️  npm audit --audit-level=high         — 6 dev-only (vite/esbuild/vitest) — breaking fix deferred
```

**Ready to tag v0.9.0-console on explicit confirmation.**
