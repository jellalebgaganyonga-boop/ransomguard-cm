# RansomGuard-CM — Dashboard (Sprint 7 Frontend)

Security console for `tenant_admin`, `security_analyst`, and
`read_only_auditor` roles. React 18 + TypeScript (strict) + Vite,
styled with TailwindCSS driven by a 3-tier design token system.

This README covers **Sprint 7 Day 1 (React Bootstrap)**. Day-by-day
implementation status is tracked at the bottom.

---

## Quick Start

```bash
# 1. Install dependencies
npm install

# 2. Copy environment template
cp .env.example .env.local

# 3. Start dev server (proxies /api -> https://localhost:8443, see vite.config.ts)
npm run dev
```

The dev server runs at `http://localhost:5173`. Ensure the GRID backend
(`grid/`) is running locally with Phase 0 applied (`v0.8.1-backend-phase0`)
for `GET /api/v1/dashboard/me` to resolve.

---

## Scripts

| Command | Purpose |
|---|---|
| `npm run dev` | Vite dev server with HMR |
| `npm run build` | Type-check (`tsc -b`) + production build |
| `npm run preview` | Preview the production build locally |
| `npm run lint` | ESLint (flat config, strict — zero warnings) |
| `npm run format` | Prettier write (incl. Tailwind class sorting) |
| `npm run typecheck` | `tsc --noEmit` |
| `npm test` | Vitest — Small tests (unit, pure logic) |
| `npm run test:watch` | Vitest watch mode |
| `npm run test:e2e` | Playwright — Medium/Large tests |
| `npm run test:e2e:ui` | Playwright UI mode |
| `npm run test:a11y` | Playwright tests tagged `@a11y` (axe-core) |
| `npm run tokens:build` | Regenerate `src/styles/tokens.css` from `design-tokens/*.json` |
| `npm run storybook` | Storybook dev server (component catalog) |

---

## Architecture (ADR Index)

All decisions below are documented in full in
`09-10_mockups_prototype_adrs.md` (Design Phase). Summary:

| ADR | Decision |
|---|---|
| FE-001 | React 18 + TypeScript strict + Vite 5 |
| FE-002 | shadcn/ui pattern + TailwindCSS + Radix UI primitives |
| FE-003 | TanStack Query (server state) + Zustand (client state) |
| FE-004 | Axios + interceptors (JWT injection, 401 refresh-and-retry) |
| FE-005 | React Hook Form + Zod (forms + runtime API response validation) |
| FE-006 | react-i18next + ICU MessageFormat, French-default |
| FE-007 | Style Dictionary token pipeline (3-tier: reference/system/component) |
| FE-008 | Vitest (Small) + Playwright (Medium/Large + a11y + visual) |
| FE-009 | In-memory access token + in-memory refresh token (HttpOnly cookie deferred — GRID-SEC-001) |
| FE-010 | Optimistic UI via TanStack Query mutations |

---

## Directory Structure

```
dashboard/
├── design-tokens/          # Tier 1/2/3 token source (JSON) + Style Dictionary config
├── src/
│   ├── api/                 # Axios client + endpoint modules (Zod-validated)
│   ├── components/
│   │   ├── layout/           # Templates & route guards (AppShellLayout, AuthLayout, ProtectedRoute)
│   │   └── ui/                # Atoms/molecules (Button, Badge, SeverityBadge, PriorityScore, ...)
│   ├── config/               # Navigation config (RBAC-filtered nav items)
│   ├── hooks/                # useMe, role resolution
│   ├── i18n/                 # react-i18next setup + fr/en locales
│   ├── lib/                   # cn() and other small utilities
│   ├── pages/                 # Route-level components
│   ├── stores/                # Zustand stores (auth, ui)
│   ├── styles/                # tokens.css (generated) + globals.css
│   └── test/                  # Vitest setup + MSW handlers
├── eslint.config.js
├── tailwind.config.ts
├── vite.config.ts
├── vitest.config.ts
└── playwright.config.ts
```

---

## Security Notes (read before touching auth code)

- **Never** persist the access token to `localStorage` / `sessionStorage`.
  It lives ONLY in `useAuthStore` (in-memory, lost on reload by design).
  See `src/stores/auth.store.ts` and ADR-FE-009.
- **GRID-SEC-001 (tracked debt):** The refresh token is currently returned in
  the JSON response body and stored in-memory — it is NOT an HttpOnly cookie.
  Both tokens are lost on page reload; the user must re-authenticate. A proper
  HttpOnly cookie implementation requires a backend change deferred to Sprint 8.
- `src/api/client.ts` deduplicates concurrent refresh attempts via a
  module-level singleton promise — do not add a second refresh call site
  without reading that file's docstring first.
- `resolvePrimaryRole()` (`src/hooks/use-me.ts`) fails closed to
  `read_only_auditor` for any unrecognized/empty role data. This is a
  client-side UX convenience ONLY — every API endpoint enforces RBAC
  server-side independently (STRIDE WT4.2, WT4.5).

---

## Testing

### Unit tests (Vitest)

```bash
npm test -- --run    # 83/83 passing
```

Tests cover: alert permissions, audit integrity (gap detection), user permissions,
agent status helpers, alert filter hooks.

### E2E tests (Playwright)

```bash
npm run test:e2e                         # all 42 tests
npx playwright test --project=chromium  # Chromium only (42/42)
npm run test:a11y                        # axe-core WCAG 2.2 AA (10/10)
```

E2E tests use Playwright's `page.route()` to intercept all API calls — no
backend required. Each spec file ships its own mock fixtures.

Full results: `docs/sprint-reports/SPRINT_7_REPORT.md`
Accessibility report: `docs/a11y-report.md`
Lighthouse report: `docs/lighthouse-report.md`

### Security audit

```bash
npm audit --audit-level=high --omit=dev  # 0 production vulnerabilities
```

6 dev-only advisories (GHSA-67mh-4wv8-2f99) in vite/esbuild/vitest chain —
not in the production bundle. Vite v5→v8 breaking upgrade deferred to Sprint 8.

---

## Sprint 7 Build Status

| Day | Scope | Status |
|---|---|---|
| **Day 1** | React bootstrap: tokens, config, stores, API client, i18n, UI primitives, layouts, routing, RBAC guard | ✅ Done |
| **Day 2-3** | EPIC-AUTH (full login flow) + EPIC-DASHBOARD (3 role-based dashboards) | ✅ Done |
| **Day 4-5** | EPIC-ALERTS (list + detail, acknowledge, close) | ✅ Done |
| **Day 6-7** | EPIC-AGENTS (inventory + isolation modal) + EPIC-USERS | ✅ Done |
| **Day 8** | EPIC-AUDIT (Ed25519 chain view, re-scoped from user-action log) | ✅ Done |
| **Day 9-10** | E2E Playwright (42/42) + axe-core a11y (10/10) + bug fixes | ✅ Done |

**Tag:** `v0.9.0-console` (pending explicit confirmation)

### Known Technical Debt (Sprint 8)

| ID | Description |
|----|-------------|
| GRID-SEC-001 | Refresh token in JSON body (not HttpOnly cookie) — tokens lost on reload |
| GRID-SEC-002 | Backend has no self-disable guard — frontend-only protection |
| GAP-01 | `AgentItem.enrolled_at` missing in backend schema |
| GAP-03 | Heartbeat timeline endpoint not implemented |
| GAP-08 | `GET /users` has no `roles` field |
| DEP-001 | Vite v5→v8 breaking upgrade required for dev vuln fix |
