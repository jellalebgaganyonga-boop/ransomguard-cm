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
| FE-009 | In-memory access token + HttpOnly refresh cookie |
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
- The refresh token is an HttpOnly cookie the frontend cannot read. All
  refresh logic goes through `POST /auth/refresh` with
  `withCredentials: true`.
- `src/api/client.ts` deduplicates concurrent refresh attempts via a
  module-level singleton promise — do not add a second refresh call site
  without reading that file's docstring first (refresh-token rotation +
  reuse detection on the backend will invalidate the session if you do).
- `resolvePrimaryRole()` (`src/hooks/use-me.ts`) fails closed to
  `read_only_auditor` for any unrecognized/empty role data. This is a
  client-side UX convenience ONLY — every API endpoint enforces RBAC
  server-side independently (STRIDE WT4.2, WT4.5).

---

## Sprint 7 Build Status

| Day | Scope | Status |
|---|---|---|
| **Day 1** | React bootstrap: tokens, config, stores, API client, i18n, UI primitives, layouts, routing, RBAC guard | ✅ Done |
| Day 2-3 | EPIC-AUTH (full login flow) + EPIC-DASHBOARD (3 role-based dashboards) | ⏳ Pending |
| Day 4-5 | EPIC-ALERTS (list + detail, inverted-pyramid narrative) | ⏳ Pending |
| Day 6-7 | EPIC-AGENTS (inventory + isolation modal) + EPIC-USERS | ⏳ Pending |
| Day 8 | EPIC-AUDIT + polish | ⏳ Pending |
| Day 9-10 | E2E Playwright + a11y (axe-core) + visual regression | ⏳ Pending |

**Tag target on completion:** `v0.9.0-console`
