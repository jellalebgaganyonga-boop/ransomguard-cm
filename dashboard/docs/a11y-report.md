# Accessibility Report — RansomGuard-CM Dashboard

**Standard:** WCAG 2.2 Level AA
**Engine:** axe-core via `@axe-core/playwright`
**Run date:** 2026-06-22
**Branch:** `feat/sprint7-phase0-dashboard`
**Command:** `npx playwright test --grep "@a11y" --project=chromium`

---

## Summary

| Metric | Result |
|--------|--------|
| Pages audited | 10 |
| Critical violations | 0 |
| Serious violations | 0 |
| Tests passing | 10/10 |

All 10 axe-core scans passed with zero critical or serious violations.

---

## Results by Page

Each test filtered violations by `impact === 'critical' || impact === 'serious'` and asserted `toEqual([])`.

| Test | File | Role | Result |
|------|------|------|--------|
| Login page | `e2e/auth.spec.ts` | — | ✅ 0 violations |
| Executive dashboard | `e2e/dashboard.spec.ts` | `tenant_admin` | ✅ 0 violations |
| Operational dashboard | `e2e/dashboard.spec.ts` | `security_analyst` | ✅ 0 violations |
| Read-only dashboard | `e2e/dashboard.spec.ts` | `read_only_auditor` | ✅ 0 violations |
| Alerts list | `e2e/alerts.spec.ts` | `security_analyst` | ✅ 0 violations |
| Alert detail | `e2e/alerts.spec.ts` | `security_analyst` | ✅ 0 violations |
| Agents list | `e2e/agents.spec.ts` | `tenant_admin` | ✅ 0 violations |
| Agent detail | `e2e/agents.spec.ts` | `tenant_admin` | ✅ 0 violations |
| Users list | `e2e/users.spec.ts` | `tenant_admin` | ✅ 0 violations |
| Audit log | `e2e/audit.spec.ts` | `read_only_auditor` | ✅ 0 violations |

---

## Remediation Applied (Day 9–10)

The following issues were identified during test runs and fixed before the final gate:

### 1. Severity badge color contrast (WCAG 1.4.3)

- **Problem:** `severity-high` used `orange-20` token (#C47A1E on white = 2.8:1, fails AA).
- **Problem:** `severity-critical` used `red-30` token (#D83B3B on white = 3.1:1, fails AA).
- **Fix:** `src/components/ui/badge.tsx`
  - `severity-high` → `--ref-palette-orange-30` (#834D11, 7.1:1 ✅)
  - `severity-critical` → `--ref-palette-red-40` (#B12626, 5.9:1 ✅)

### 2. Audit log filter inputs — missing label association (WCAG 1.3.1, 4.1.2)

- **Problem:** Three filter inputs (`<input type="text">`, two `<input type="datetime-local">`) had visible `<label>` elements but no `htmlFor`/`id` pairing.
- **Fix:** `src/pages/audit/audit-log-page.tsx`
  - Added `id="audit-filter-agent-id"` + `htmlFor="audit-filter-agent-id"`
  - Added `id="audit-filter-after"` + `htmlFor="audit-filter-after"`
  - Added `id="audit-filter-before"` + `htmlFor="audit-filter-before"`

### 3. Low-contrast body text (WCAG 1.4.3)

- **Problem:** Multiple pages used `text-text-tertiary` (#7E8493 on #F8F9FB = 3.56:1, fails AA).
- **Fix:** All occurrences changed to `text-text-secondary` (#5B6070 on #F8F9FB = 5.21:1 ✅) across:
  - `alerts-list-page.tsx`, `alert-detail-page.tsx`, `agents-list-page.tsx`,
  - `agent-detail-page.tsx`, `users-list-page.tsx`, `audit-log-page.tsx`,
  - `executive-dashboard.tsx`, `operational-dashboard.tsx`, `read-only-dashboard.tsx`

### 4. Create user modal — unlabelled form inputs (WCAG 4.1.2)

- **Problem:** Email, full name, and password inputs in `CreateUserModal` lacked programmatic label association.
- **Fix:** `src/components/users/create-user-modal.tsx` — added `id`/`htmlFor` pairs on all form fields.

### 5. SessionExpiredModal — incorrect ARIA role (WCAG 4.1.2)

- **Problem:** The idle-timeout modal used `role="alertdialog"` but the E2E test queried `getByRole('dialog')` — these are distinct ARIA roles.
- **Fix:** Tests updated to use `getByRole('alertdialog')` (no DOM change needed; the role was already correct in the component).

---

## Remaining Issues

| ID | Issue | Impact | Deferred To |
|----|-------|--------|-------------|
| A11Y-DEFER-001 | `@a11y` tests do not cover modal states (AcknowledgeModal, CloseModal, IsolateModal) in isolation — tested only through interaction flows | Low | Sprint 8 |
| A11Y-DEFER-002 | Dark mode token contrast not verified (design tokens define dark palette but no dark-mode E2E tests exist) | Low | Sprint 8 |

---

## Test Infrastructure

```
npm run test:a11y
# → npx playwright test --grep "@a11y" --project=chromium
# → 10 tests, 10 passed, 0 violations (critical/serious)
```

axe-core version: `4.10.x` (via `@axe-core/playwright` package).
