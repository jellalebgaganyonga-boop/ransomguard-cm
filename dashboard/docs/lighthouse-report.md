# Lighthouse Report — RansomGuard-CM Dashboard

**Tool:** Lighthouse CLI 13.4.0
**Run date:** 2026-06-22
**Branch:** `feat/sprint7-phase0-dashboard`

---

## Scope Limitation

Lighthouse was not run against the full authenticated application in this sprint.

**Reason:** The dashboard is a React SPA behind JWT authentication. The production
build (`npm run build` → `npm run preview`) serves only the login page publicly —
all other routes redirect to `/login` without a valid access token. Running
Lighthouse against the preview server therefore audits only the login page redirect
shell, not the authenticated dashboard UI.

A meaningful Lighthouse audit of the authenticated pages (Dashboard, Alerts,
Agents, Users, Audit) requires either:
1. A headless Chrome session with a pre-seeded JWT (requires backend running),
2. A staging environment with Lighthouse CI configured in the CI pipeline, or
3. Manual Chrome DevTools Lighthouse run by a developer with backend access.

---

## Production Build Output (Proxy for Performance)

`npm run build` completed successfully on 2026-06-22:

| Bundle | Size (gzip) |
|--------|-------------|
| `index.html` | 0.74 kB |
| `index.css` | 8.41 kB |
| `react-vendor.js` | 51.58 kB |
| `i18n-vendor.js` | 27.56 kB |
| `form-vendor.js` | 23.37 kB |
| `query-vendor.js` | 13.44 kB |
| `chart-vendor.js` | 0.33 kB |
| `index.js` | 100.41 kB |
| **Total (gzip)** | **~225 kB** |

Manual code-splitting into 5 vendor chunks was applied (ADR-FE-001) to enable
parallel download and browser caching of stable third-party code separately from
the application chunk.

---

## Deferred to Sprint 8

| Task | Owner |
|------|-------|
| Configure `@lhci/cli` in GitHub Actions for authenticated Lighthouse CI runs | Platform |
| Add `/login` Lighthouse baseline (Performance / Accessibility / SEO) | Frontend |
| Add `playwright/lighthouse` integration for post-auth page audits | Frontend |

---

## Accessibility Coverage (Alternative)

While Lighthouse accessibility scoring was not captured this sprint, WCAG 2.2 AA
compliance was validated via axe-core on 10 pages (see `dashboard/docs/a11y-report.md`).
axe-core covers a superset of Lighthouse's accessibility rules with greater
detail and specificity.
