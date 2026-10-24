# Sprint 7 — RansomGuard Console: Production Web Dashboard

**Document Type**: Product Requirements Document (PRD) + Engineering Specification
**Status**: Ready for Implementation
**Authored by**: Staff Product Manager + Tech Lead (collaborative)
**Target Audience**: Claude Code (implementation agent), Engineering Team
**Sprint Code Name**: Sprint 7 "RansomGuard Console"
**Target Git Tag**: `v0.9.0-console`
**Estimated Duration**: 10–12 working days

---

## Table of Contents

1. [Sprint Goal](#1-sprint-goal)
2. [Business Context & Strategic Rationale](#2-business-context--strategic-rationale)
3. [System Roles (RBAC Reference)](#3-system-roles-rbac-reference)
4. [Epic 1 — Authentication & Authorization](#epic-1--authentication--authorization)
5. [Epic 2 — Dashboard Overview](#epic-2--dashboard-overview)
6. [Epic 3 — Incidents Management](#epic-3--incidents-management)
7. [Epic 4 — Endpoints Management](#epic-4--endpoints-management)
8. [Epic 5 — Compliance Reporting](#epic-5--compliance-reporting)
9. [Epic 6 — User & Role Management](#epic-6--user--role-management)
10. [Epic 7 — Threat Intelligence Display](#epic-7--threat-intelligence-display)
11. [Epic 8 — Audit Logs Viewer](#epic-8--audit-logs-viewer)
12. [Technical Stack & Architecture](#12-technical-stack--architecture)
13. [Non-Functional Requirements (NFRs)](#13-non-functional-requirements-nfrs)
14. [Sprint Backlog & Estimation](#14-sprint-backlog--estimation)
15. [Definition of Done (DoD)](#15-definition-of-done-dod)
16. [Risk Register](#16-risk-register)
17. [Out of Scope](#17-out-of-scope)

---

## 1. Sprint Goal

**One-sentence Sprint Goal:**

> *Deliver a production-grade React web console that allows the three RBAC roles (tenant_admin, security_analyst, read_only_auditor) to authenticate, monitor incidents, manage endpoints, and generate compliance reports through a bilingual French/English interface aligned with the existing GRID Server API.*

**Success Criteria (binary, measurable):**

- [ ] All eight Epics implemented and tagged `v0.9.0-console`
- [ ] All 32 User Stories pass Acceptance Criteria
- [ ] Test coverage ≥ 75% (unit + integration + E2E)
- [ ] Lighthouse Performance score ≥ 90
- [ ] Lighthouse Accessibility score ≥ 90 (WCAG 2.1 AA)
- [ ] Zero hardcoded user names in source code
- [ ] All user-facing strings externalized in i18n resource files
- [ ] Backend API contract conformance verified (OpenAPI)

---

## 2. Business Context & Strategic Rationale

### 2.1 Why this Sprint matters

The GRID Server backend (`v0.8.0-grid`) is operationally complete but invisible to end-users. Without a web console, RansomGuard-CM cannot be:
- Demonstrated to academic juries
- Sold to hospital administrators
- Operated daily by IT officers
- Audited by compliance officers

### 2.2 Business value delivered

| Stakeholder | Value Delivered |
|---|---|
| Hospital Administrator | Executive visibility + compliance signing capability |
| IT Officer | Daily operational console + incident response toolkit |
| Auditor / ANTIC | Self-service compliance report download |
| Sales Team | Demonstrable product for B2B prospecting |
| Engineering Team | Frontend foundation for future feature additions |

### 2.3 Strategic alignment

This Sprint advances the product from "backend-only research project" to "commercial-grade SaaS product" comparable to Kaspersky Endpoint Security Cloud, CrowdStrike Falcon Console, and SentinelOne Singularity Operations Center.

---

## 3. System Roles (RBAC Reference)

The system supports exactly **four roles**. These are role identifiers in code — never hardcoded user names.

| Role Code | Display Name | Scope | Description |
|---|---|---|---|
| `super_admin` | Super Administrator | Global (cross-tenant) | RansomGuard editor — out of Sprint 7 scope |
| `tenant_admin` | Hospital Administrator | Single tenant | Configures system, manages users, signs reports |
| `security_analyst` | Security Analyst | Single tenant | Daily operations, incident response, endpoint management |
| `read_only_auditor` | Read-Only Auditor | Single tenant (read) | Compliance review, report download |

**Critical Engineering Rule:**

> **The system stores only ROLES (configurable, parameterized). It does NOT store hardcoded user names. Personas (e.g., "Mvondo", "Lewis", "Bilo'o") are design artifacts only — they MUST NEVER appear in source code, database, API responses, or UI strings.**

**Code Pattern (correct):**

```typescript
if (hasRole(currentUser, 'tenant_admin')) {
  showAdminMenu();
}
```

**Code Pattern (FORBIDDEN):**

```typescript
if (currentUser.name === 'Mvondo') { // ❌ NEVER
  showAdminMenu();
}
```

---

# EPIC 1 — Authentication & Authorization

**Epic ID**: `EPIC-AUTH`
**Estimated Effort**: 1.5 days
**Story Points**: 13 SP
**Business Value**: HIGH — Foundation for all other Epics
**Risk Level**: HIGH — Security-critical

### Epic Goal

Enable any user with a valid account in the GRID Server to authenticate securely into the console using credentials + MFA, with session management and password reset flows compliant with industry security standards.

### Success Metrics

- 100% of authentication attempts result in either success, MFA challenge, or rejection
- Zero successful logins without MFA
- Session timeout enforced at 30 minutes idle
- Password reset flow completed in < 60 seconds

### User Stories in this Epic

- US1.1 — Login with credentials
- US1.2 — MFA verification (TOTP)
- US1.3 — Logout (single + all sessions)
- US1.4 — Password reset via email
- US1.5 — Session timeout & refresh

---

## US1.1 — Login with credentials

**Story ID**: `US1.1`
**Story Points**: 3 SP
**Estimated Effort**: 0.5 day

**As a** user (any role)
**I want to** log in to the console using my email and password
**So that** I can access my role-appropriate dashboard

### Acceptance Criteria

```gherkin
AC1.1.1: Valid credentials redirect to MFA
  GIVEN a user account exists with role assigned and active status
  WHEN the user submits valid email + password
  THEN the system returns HTTP 200
  AND  the response contains a pre-MFA token (valid for 5 minutes)
  AND  the user is redirected to /auth/mfa-verify
  AND  an audit log entry "login_attempt_success" is created

AC1.1.2: Invalid credentials are rejected
  GIVEN any combination of email and password
  WHEN the credentials do not match an active account
  THEN the system returns HTTP 401
  AND  the response message is generic: "Invalid credentials" (no enumeration)
  AND  an audit log entry "login_attempt_failed" is created
  AND  rate limiting applies (5 attempts per 15 minutes per IP)

AC1.1.3: Disabled account is rejected
  GIVEN a user account with status="disabled"
  WHEN the user submits valid credentials
  THEN the system returns HTTP 401
  AND  the response message is generic: "Invalid credentials"
  AND  an audit log entry "login_attempt_disabled_account" is created

AC1.1.4: Login form validates input client-side
  GIVEN the login form is rendered
  WHEN the user submits empty email or password
  THEN the form displays inline validation errors in user's language (FR/EN)
  AND  no API call is made

AC1.1.5: Bilingual UI
  GIVEN the user has set language preference to FR
  WHEN they access /auth/login
  THEN all labels, placeholders, error messages appear in French
  AND  same applies for EN preference
```

### Technical Notes

- Endpoint: `POST /api/v1/auth/login`
- Pre-MFA token stored in HttpOnly cookie + memory (Zustand)
- Password validation: client-side minimum (length > 0), server-side authoritative
- HTTPS-only (HSTS enforced)

---

## US1.2 — MFA verification (TOTP)

**Story ID**: `US1.2`
**Story Points**: 5 SP
**Estimated Effort**: 0.5 day

**As a** user holding a valid pre-MFA token
**I want to** verify my identity using a TOTP code from my authenticator app
**So that** I receive a full session token and can access the console

### Acceptance Criteria

```gherkin
AC1.2.1: Valid TOTP code grants session
  GIVEN a user holds a valid pre-MFA token
  AND   the TOTP secret is configured for that user
  WHEN the user submits a TOTP code valid for the current 30-second window
  THEN the system returns HTTP 200
  AND  the response contains an access_token (JWT, 30 minutes validity)
  AND  the response contains a refresh_token (HttpOnly cookie, 7 days validity)
  AND  the user is redirected to /dashboard (role-appropriate landing)
  AND  an audit log entry "mfa_success" is created

AC1.2.2: Invalid TOTP code is rejected
  GIVEN a user holds a valid pre-MFA token
  WHEN the user submits an incorrect TOTP code
  THEN the system returns HTTP 401
  AND  the response message is "Invalid verification code"
  AND  the pre-MFA token remains valid for retry (up to 3 attempts)
  AND  an audit log entry "mfa_failed" is created

AC1.2.3: Expired pre-MFA token redirects to login
  GIVEN a user holds a pre-MFA token older than 5 minutes
  WHEN the user submits any TOTP code
  THEN the system returns HTTP 401 with code "PRE_MFA_EXPIRED"
  AND  the client redirects to /auth/login
  AND  the user must restart authentication

AC1.2.4: First-time MFA setup flow
  GIVEN a user logs in for the first time and has no TOTP configured
  WHEN they reach /auth/mfa-verify
  THEN the system displays a QR code (Google Authenticator format)
  AND  the user scans the QR code in their authenticator app
  AND  the user submits the first TOTP code to confirm setup
  AND  the TOTP secret is persisted in the database (encrypted)
  AND  recovery codes (10) are displayed once and not stored anywhere except hashed

AC1.2.5: TOTP code accepts ±1 window (clock drift tolerance)
  GIVEN a valid TOTP secret
  WHEN the user submits a code from the previous OR next 30-second window
  THEN the code is accepted (RFC 6238 standard tolerance)
```

### Technical Notes

- Endpoint: `POST /api/v1/auth/mfa/verify`
- TOTP library: industry-standard (RFC 6238 compliant)
- Recovery codes: 10 codes, single-use, hashed with bcrypt
- QR code rendering: client-side library

---

## US1.3 — Logout (single + all sessions)

**Story ID**: `US1.3`
**Story Points**: 2 SP
**Estimated Effort**: 0.25 day

**As a** authenticated user (any role)
**I want to** log out of my current session or all my sessions
**So that** my account is secured when I leave the workstation

### Acceptance Criteria

```gherkin
AC1.3.1: Single session logout
  GIVEN an authenticated user
  WHEN the user clicks "Logout" in the user menu
  THEN the access_token is invalidated server-side
  AND  the refresh_token cookie is cleared
  AND  the local state (Zustand) is reset
  AND  the user is redirected to /auth/login
  AND  an audit log entry "logout" is created

AC1.3.2: Logout from all sessions
  GIVEN an authenticated user with multiple active sessions
  WHEN the user clicks "Logout from all devices" in Settings
  THEN all refresh_tokens for this user are invalidated server-side
  AND  all access_tokens become unusable after their TTL expires
  AND  the user is redirected to /auth/login
  AND  an audit log entry "logout_all_sessions" is created

AC1.3.3: Idle logout (frontend)
  GIVEN an authenticated user is inactive for 30 minutes
  WHEN no API calls have been made and no user input detected
  THEN the client automatically calls logout
  AND  a modal "Session expired" is briefly shown
  AND  the user is redirected to /auth/login
```

### Technical Notes

- Endpoints: `POST /api/v1/auth/logout`, `POST /api/v1/auth/logout-all`
- Token blacklist: Redis-backed with TTL = remaining JWT validity
- Idle detection: client-side activity listener (mousemove, keypress, scroll)

---

## US1.4 — Password reset via email

**Story ID**: `US1.4`
**Story Points**: 3 SP
**Estimated Effort**: 0.25 day

**As a** user who forgot their password
**I want to** reset it via an email-delivered link
**So that** I can regain access to my account without administrator intervention

### Acceptance Criteria

```gherkin
AC1.4.1: Request password reset
  GIVEN the user enters their email on /auth/forgot-password
  WHEN they submit the form
  THEN the system always returns HTTP 200 with generic message (anti-enumeration)
  AND  if the email matches an active account, a reset link is sent via SMTP
  AND  the reset token is valid for 1 hour, single-use
  AND  an audit log entry "password_reset_requested" is created

AC1.4.2: Reset password with valid token
  GIVEN a user clicks the reset link with a valid, unused token
  WHEN they submit a new password meeting policy (12+ chars, mixed case, digit, symbol)
  THEN the password is updated (bcrypt-hashed)
  AND  all existing sessions for the user are invalidated
  AND  the reset token is marked used
  AND  the user is redirected to /auth/login
  AND  an audit log entry "password_reset_success" is created

AC1.4.3: Reset password with expired token
  GIVEN a user clicks a reset link with an expired or used token
  WHEN they try to submit a new password
  THEN the system returns HTTP 410 (Gone)
  AND  the user is shown an error and a link to request a new reset

AC1.4.4: Password policy validation
  GIVEN the password reset form
  WHEN the user types a new password
  THEN real-time strength indicator is displayed (red/orange/green)
  AND  submission is blocked until policy requirements are met
```

### Technical Notes

- Endpoints: `POST /api/v1/auth/forgot-password`, `POST /api/v1/auth/reset-password`
- SMTP integration: configured at tenant level
- Email template: bilingual FR/EN, signed with DKIM
- Password policy: enforced server-side (authoritative)

---

## US1.5 — Session timeout & refresh

**Story ID**: `US1.5`
**Story Points**: 3 SP
**Estimated Effort**: 0.25 day

**As a** authenticated user
**I want to** have my session automatically refreshed during active use, but expired during inactivity
**So that** my session is both usable and secure

### Acceptance Criteria

```gherkin
AC1.5.1: Access token expires after 30 minutes
  GIVEN a user has an access_token issued at time T
  WHEN T + 30 minutes elapse
  THEN the access_token is rejected by the API
  AND  the client automatically attempts refresh

AC1.5.2: Refresh token rotation
  GIVEN the access_token expires and a valid refresh_token exists in cookie
  WHEN the client calls POST /api/v1/auth/refresh
  THEN a new access_token is issued (30 min validity)
  AND  the refresh_token is ROTATED (old invalidated, new issued, 7 days validity)
  AND  this happens transparently without user re-authentication

AC1.5.3: Idle timeout disables refresh
  GIVEN no user activity has been detected for 30 minutes
  WHEN the access_token expires
  THEN the refresh attempt is blocked client-side
  AND  the user is logged out and redirected to /auth/login

AC1.5.4: Concurrent refresh requests are de-duplicated
  GIVEN multiple simultaneous API calls fail due to expired token
  WHEN they trigger refresh
  THEN only ONE refresh request is sent to the server (queued)
  AND  all original calls are replayed with the new token after refresh succeeds
```

### Technical Notes

- Endpoint: `POST /api/v1/auth/refresh`
- Refresh strategy: silent refresh with axios interceptor
- Token rotation: enforced (replay attack prevention)

---

# EPIC 2 — Dashboard Overview

**Epic ID**: `EPIC-DASHBOARD`
**Estimated Effort**: 1.5 days
**Story Points**: 13 SP
**Business Value**: HIGH — First impression for all users
**Risk Level**: MEDIUM

### Epic Goal

Provide each role with a personalized landing page that surfaces the most relevant information for their job, eliminating unnecessary cognitive load.

### Success Metrics

- Time-to-meaningful-info < 2 seconds (Lighthouse FCP)
- 100% role coverage (each role has a tailored landing)
- Real-time KPI updates within 5 seconds of backend change

### User Stories in this Epic

- US2.1 — Executive landing (tenant_admin)
- US2.2 — Operational landing (security_analyst)
- US2.3 — Reports landing (read_only_auditor)
- US2.4 — Real-time KPI updates

---

## US2.1 — Executive landing (tenant_admin)

**Story ID**: `US2.1`
**Story Points**: 3 SP
**Estimated Effort**: 0.5 day

**As a** user with role `tenant_admin`
**I want to** land on an executive summary page after login
**So that** I can grasp the security posture of my hospital in under 5 seconds

### Acceptance Criteria

```gherkin
AC2.1.1: Layout shows global status card
  GIVEN a user with role tenant_admin logs in
  WHEN they land on /dashboard
  THEN the top card displays ONE of:
       - "All systems normal" (green) if zero critical incidents in last 7 days
       - "X critical incidents require attention" (red) if any unresolved
       - "X warnings to review" (orange) otherwise

AC2.1.2: Layout shows four KPI cards
  GIVEN the dashboard is rendered
  WHEN the page is displayed
  THEN four KPI cards are visible:
       - Total protected endpoints
       - Active incidents (last 24h)
       - Days since last critical incident
       - Compliance status (% policies enforced)

AC2.1.3: Layout shows recent incidents widget
  GIVEN the dashboard is rendered
  WHEN there are recent incidents
  THEN a widget displays the 5 most recent incidents (chronological)
  AND  each row is clickable to navigate to incident detail
  AND  if zero incidents, an empty state message is shown

AC2.1.4: Layout shows quick actions
  GIVEN a user with role tenant_admin
  WHEN they view the dashboard
  THEN two prominent buttons are visible:
       - "Download monthly compliance report"
       - "Manage users"

AC2.1.5: Layout is responsive
  GIVEN the dashboard is rendered on viewport widths 320px, 768px, 1024px, 1440px
  WHEN the viewport changes
  THEN cards reflow gracefully (no horizontal scroll)
  AND  touch targets are ≥ 44×44 px on mobile
```

### Technical Notes

- Route: `/dashboard` (with role-based redirect)
- Data sources: `GET /api/v1/dashboard/summary` (aggregated endpoint)
- Cache strategy: TanStack Query with 30-second refetch
- Skeleton loaders during data fetch

---

## US2.2 — Operational landing (security_analyst)

**Story ID**: `US2.2`
**Story Points**: 5 SP
**Estimated Effort**: 0.5 day

**As a** user with role `security_analyst`
**I want to** land on an operational console showing live endpoint health and incident feed
**So that** I can immediately start triaging during my work shift

### Acceptance Criteria

```gherkin
AC2.2.1: Layout shows endpoint health overview
  GIVEN a user with role security_analyst logs in
  WHEN they land on /dashboard
  THEN a top panel displays:
       - Number of endpoints online (green)
       - Number of endpoints offline (red)
       - Number of endpoints with warnings (orange)

AC2.2.2: Layout shows live incident feed
  GIVEN the dashboard is rendered
  WHEN incidents exist in the tenant
  THEN a scrollable feed displays incidents sorted by severity then recency
  AND  each row shows: timestamp, severity badge, endpoint name, summary
  AND  the feed auto-updates every 5 seconds (polling or WebSocket)

AC2.2.3: Layout shows endpoint health table
  GIVEN the dashboard is rendered
  WHEN endpoints are registered
  THEN a paginated table (50 rows) displays all endpoints with columns:
       - Hostname
       - OS version
       - Last heartbeat (relative time)
       - Status (online/offline/isolated)
       - Quick action buttons (Isolate, View details)

AC2.2.4: Layout supports keyboard shortcuts
  GIVEN the dashboard is focused
  WHEN the user presses Ctrl+K
  THEN a command palette opens
  AND  searchable commands include: "Isolate endpoint", "Open incident X", "Search"

AC2.2.5: Layout shows quick filters
  GIVEN the endpoints table is rendered
  WHEN the user clicks a filter chip
  THEN the table filters by: All / Online only / Offline only / With warnings
```

### Technical Notes

- Route: `/dashboard` (role-based component swap)
- Data sources: `GET /api/v1/endpoints?summary=true`, `GET /api/v1/incidents/feed`
- Real-time: polling every 5s, WebSocket if backend supports
- Virtual scrolling for large endpoint lists

---

## US2.3 — Reports landing (read_only_auditor)

**Story ID**: `US2.3`
**Story Points**: 2 SP
**Estimated Effort**: 0.25 day

**As a** user with role `read_only_auditor`
**I want to** land on a compliance reports library
**So that** I can immediately download the report I need for my audit

### Acceptance Criteria

```gherkin
AC2.3.1: Layout shows monthly reports list
  GIVEN a user with role read_only_auditor logs in
  WHEN they land on /dashboard
  THEN a list of available monthly compliance reports is displayed
  AND  each row shows: month/year, generation date, file size, signed status
  AND  rows are sorted by date descending

AC2.3.2: Layout shows quarterly comparison
  GIVEN the dashboard is rendered
  WHEN compliance data is available
  THEN a small chart shows compliance trend over last 12 months
  AND  the chart is read-only (no interactive editing)

AC2.3.3: Download report
  GIVEN a report row is visible
  WHEN the user clicks "Download PDF"
  THEN the PDF is downloaded in the user's language preference (FR/EN)
  AND  the download is logged in audit logs ("report_downloaded")

AC2.3.4: No write actions visible
  GIVEN a user with role read_only_auditor
  WHEN they view the dashboard
  THEN no buttons for "Generate report", "Sign report", "Modify settings" are visible
  AND  attempts to access /settings or /users routes redirect to /dashboard with HTTP 403
```

### Technical Notes

- Route: `/dashboard` (role-based component swap)
- Data sources: `GET /api/v1/reports?status=signed`
- Download: streaming PDF response with Content-Disposition

---

## US2.4 — Real-time KPI updates

**Story ID**: `US2.4`
**Story Points**: 3 SP
**Estimated Effort**: 0.25 day

**As a** authenticated user
**I want to** see KPIs update in near-real-time without manual refresh
**So that** I always work with current data

### Acceptance Criteria

```gherkin
AC2.4.1: Polling refresh every 30 seconds (fallback)
  GIVEN the dashboard is open
  WHEN 30 seconds elapse without WebSocket
  THEN TanStack Query refetches dashboard summary
  AND  the UI updates without page reload

AC2.4.2: WebSocket live updates (if available)
  GIVEN the backend exposes a WebSocket endpoint
  WHEN a new incident is created in the tenant
  THEN the dashboard receives a push event
  AND  the relevant KPI card increments immediately
  AND  a subtle visual animation indicates the update

AC2.4.3: Manual refresh button
  GIVEN any dashboard view
  WHEN the user clicks the refresh icon in the header
  THEN data is refetched immediately
  AND  a spinner indicates loading state

AC2.4.4: Stale data indicator
  GIVEN the connection to the backend is lost
  WHEN the client detects offline state
  THEN a banner displays "Connection lost. Last update: HH:MM"
  AND  data remains visible (cached) until reconnection
```

### Technical Notes

- Polling: TanStack Query `refetchInterval: 30_000`
- WebSocket: optional progressive enhancement (check capability at runtime)
- Offline detection: `navigator.onLine` + ping endpoint

---

# EPIC 3 — Incidents Management

**Epic ID**: `EPIC-INCIDENTS`
**Estimated Effort**: 2 days
**Story Points**: 21 SP
**Business Value**: CRITICAL — Core operational feature
**Risk Level**: MEDIUM

### Epic Goal

Allow tenant_admin and security_analyst to triage, investigate, acknowledge, and close incidents, while read_only_auditor can view (read-only) for compliance review.

### Success Metrics

- Time-to-acknowledge median < 1 minute
- Time-to-close median < 15 minutes
- 100% of incidents traceable via forensic trail

### User Stories in this Epic

- US3.1 — Incidents list with filters
- US3.2 — Incident detail view
- US3.3 — Acknowledge incident
- US3.4 — Close incident with resolution notes
- US3.5 — Forensic trail (process tree, file timeline)

---

## US3.1 — Incidents list with filters

**Story ID**: `US3.1`
**Story Points**: 5 SP
**Estimated Effort**: 0.5 day

**As a** authenticated user
**I want to** see a paginated, filterable list of all incidents in my tenant
**So that** I can quickly find the incident I need to act on

### Acceptance Criteria

```gherkin
AC3.1.1: List shows incidents with pagination
  GIVEN incidents exist in the user's tenant
  WHEN the user navigates to /incidents
  THEN a paginated table displays incidents (50 per page)
  AND  columns are: ID, Created at, Severity, Endpoint, Status, Assigned to
  AND  sorting is enabled on Created at, Severity, Status

AC3.1.2: Filters apply to the list
  GIVEN the incidents list is rendered
  WHEN the user applies any of:
       - Date range
       - Severity (critical, high, medium, low)
       - Status (open, acknowledged, closed)
       - Endpoint name (text search)
  THEN the table updates with filtered results
  AND  the URL query string reflects the filters (shareable)

AC3.1.3: Search by ID or summary
  GIVEN the incidents list is rendered
  WHEN the user types in the search box
  THEN the table filters by incident ID OR summary (debounced 300ms)

AC3.1.4: Empty state
  GIVEN no incidents exist or filters yield no results
  WHEN the list is rendered
  THEN an empty state component displays: "No incidents found"
  AND  for tenant_admin / security_analyst: a hint to check filters

AC3.1.5: Multi-tenant isolation enforced
  GIVEN a user belongs to tenant A
  WHEN the API call includes tenant B's incident IDs in URL manipulation
  THEN the server returns HTTP 404 (not 403, to avoid enumeration)

AC3.1.6: Role-based action visibility
  GIVEN a user with role read_only_auditor
  WHEN they view the list
  THEN columns are read-only and no row actions ("Acknowledge", "Close") are visible
```

### Technical Notes

- Route: `/incidents`
- API: `GET /api/v1/incidents?page=1&size=50&severity=critical&status=open`
- Server-side pagination + filtering
- URL state via React Router search params

---

## US3.2 — Incident detail view

**Story ID**: `US3.2`
**Story Points**: 5 SP
**Estimated Effort**: 0.5 day

**As a** authenticated user
**I want to** view full details of a specific incident
**So that** I have all the information needed to investigate and respond

### Acceptance Criteria

```gherkin
AC3.2.1: Header shows incident metadata
  GIVEN an incident exists with ID X
  WHEN the user navigates to /incidents/X
  THEN the header displays:
       - Incident ID (copyable)
       - Severity badge
       - Status badge
       - Created at + age
       - Endpoint affected (with link to endpoint detail)

AC3.2.2: Body shows incident summary and detection details
  GIVEN the incident page is rendered
  WHEN data loads
  THEN the body shows:
       - Detection module (SENTINEL / ENTROPY / GENEALOGY / USB GUARD / EXFIL WATCH / IRONCLAD)
       - Confidence score
       - Detection narrative (human-readable explanation)
       - Affected files (if any)
       - Process tree (collapsible)

AC3.2.3: Timeline of related alerts
  GIVEN the incident has correlated alerts
  WHEN the page is rendered
  THEN a chronological timeline shows all related alerts
  AND  each entry is expandable to show alert payload

AC3.2.4: Action buttons depend on role and status
  GIVEN the user role and incident status
  WHEN the page is rendered
  THEN buttons are conditionally visible:
       - tenant_admin OR security_analyst + open: [Acknowledge] [Isolate Endpoint]
       - tenant_admin OR security_analyst + acknowledged: [Close] [Escalate]
       - read_only_auditor: no action buttons, only [Export PDF]

AC3.2.5: Export incident report
  GIVEN any role views the incident
  WHEN they click "Export PDF"
  THEN a PDF is generated with all incident details, signed digitally
  AND  the export is logged in audit logs
```

### Technical Notes

- Route: `/incidents/:id`
- API: `GET /api/v1/incidents/:id` (full payload), `GET /api/v1/incidents/:id/timeline`
- PDF generation: server-side rendering

---

## US3.3 — Acknowledge incident

**Story ID**: `US3.3`
**Story Points**: 3 SP
**Estimated Effort**: 0.25 day

**As a** user with role `tenant_admin` OR `security_analyst`
**I want to** acknowledge an open incident
**So that** I claim responsibility for handling it and prevent duplicate work

### Acceptance Criteria

```gherkin
AC3.3.1: Acknowledge open incident
  GIVEN an incident with status "open"
  AND   the user has role tenant_admin OR security_analyst
  WHEN the user clicks "Acknowledge"
  THEN a confirmation modal asks "Acknowledge this incident as yourself?"
  AND  upon confirmation, the API call is made
  AND  the incident status changes to "acknowledged"
  AND  the assignee field is set to the current user
  AND  an audit log entry "incident_acknowledged" is created with user ID
  AND  the timeline shows the acknowledgment event

AC3.3.2: Cannot acknowledge already-acknowledged incident
  GIVEN an incident with status "acknowledged" or "closed"
  WHEN any user views it
  THEN the "Acknowledge" button is hidden
  AND  attempts via direct API call return HTTP 409 (Conflict)

AC3.3.3: read_only_auditor cannot acknowledge
  GIVEN a user with role read_only_auditor
  WHEN they view an open incident
  THEN no "Acknowledge" button is visible
  AND  direct API call returns HTTP 403

AC3.3.4: Optimistic UI update
  GIVEN the user clicks "Acknowledge"
  WHEN the API call is in flight
  THEN the UI immediately shows the new state (acknowledged)
  AND  if the API fails, the UI reverts and shows an error toast
```

### Technical Notes

- API: `POST /api/v1/incidents/:id/acknowledge`
- Optimistic update via TanStack Query mutations

---

## US3.4 — Close incident with resolution notes

**Story ID**: `US3.4`
**Story Points**: 3 SP
**Estimated Effort**: 0.25 day

**As a** user with role `tenant_admin` OR `security_analyst`
**I want to** close an acknowledged incident with resolution notes
**So that** the incident lifecycle is complete and traceable

### Acceptance Criteria

```gherkin
AC3.4.1: Close acknowledged incident
  GIVEN an incident with status "acknowledged"
  AND   the user has role tenant_admin OR security_analyst
  WHEN the user clicks "Close"
  THEN a modal prompts for resolution notes (required, 20-2000 characters)
  AND  a resolution category dropdown is required:
       - "False positive"
       - "True positive — contained"
       - "True positive — escalated"
       - "Inconclusive"
  AND  upon submission, the incident status changes to "closed"
  AND  notes and category are persisted
  AND  an audit log entry "incident_closed" is created

AC3.4.2: Cannot close non-acknowledged incident
  GIVEN an incident with status "open" or "closed"
  WHEN any user views it
  THEN the "Close" button is hidden or disabled
  AND  direct API call returns HTTP 409

AC3.4.3: Closed incidents are read-only
  GIVEN an incident with status "closed"
  WHEN any role views it
  THEN no modification actions are available
  AND  the incident detail page shows resolution notes prominently
```

### Technical Notes

- API: `POST /api/v1/incidents/:id/close` with body `{notes, category}`
- Validation: server-side authoritative

---

## US3.5 — Forensic trail (process tree, file timeline)

**Story ID**: `US3.5`
**Story Points**: 5 SP
**Estimated Effort**: 0.5 day

**As a** user with role `tenant_admin` OR `security_analyst`
**I want to** view the complete forensic trail of an incident
**So that** I can understand the attack chain and produce a report

### Acceptance Criteria

```gherkin
AC3.5.1: Process tree visualization
  GIVEN an incident has associated GENEALOGY module data
  WHEN the user navigates to /incidents/:id/forensic
  THEN a tree visualization shows the parent-child process hierarchy
  AND  each node displays: process name, PID, signed status, command line
  AND  the suspicious process is highlighted (red border)

AC3.5.2: File modification timeline
  GIVEN an incident has associated SENTINEL or ENTROPY data
  WHEN the forensic page is rendered
  THEN a chronological timeline displays file events:
       - Timestamp
       - File path
       - Action (created, modified, deleted, renamed)
       - Entropy value (if available)
       - Initiating process

AC3.5.3: Network connections
  GIVEN an incident has associated EXFIL WATCH data
  WHEN the forensic page is rendered
  THEN a list shows outbound connections:
       - Destination IP/domain
       - Volume transferred
       - Threat intel reputation
       - Connection timestamps

AC3.5.4: read_only_auditor sees forensic trail (read-only)
  GIVEN a user with role read_only_auditor
  WHEN they navigate to /incidents/:id/forensic
  THEN the same visualization is displayed
  BUT no interactive actions are available

AC3.5.5: Export forensic report
  GIVEN any role views the forensic trail
  WHEN they click "Export forensic report"
  THEN a comprehensive PDF is generated including all sections above
  AND  the PDF is digitally signed (Ed25519)
```

### Technical Notes

- Route: `/incidents/:id/forensic`
- API: `GET /api/v1/incidents/:id/forensic`
- Tree visualization: react-flow or d3-hierarchy
- PDF: server-side composition

---

# EPIC 4 — Endpoints Management

**Epic ID**: `EPIC-ENDPOINTS`
**Estimated Effort**: 1.5 days
**Story Points**: 21 SP
**Business Value**: HIGH — Daily operations
**Risk Level**: MEDIUM

### Epic Goal

Allow security_analyst and tenant_admin to manage the inventory of protected endpoints, including deployment, isolation, and bulk operations.

### User Stories in this Epic

- US4.1 — Endpoints inventory
- US4.2 — Endpoint detail view
- US4.3 — Isolate endpoint
- US4.4 — Deploy agent (MSI installer download)
- US4.5 — Bulk actions

---

## US4.1 — Endpoints inventory

**Story ID**: `US4.1`
**Story Points**: 5 SP
**Estimated Effort**: 0.5 day

**As a** authenticated user
**I want to** view a paginated, searchable list of all endpoints in my tenant
**So that** I have visibility on the fleet

### Acceptance Criteria

```gherkin
AC4.1.1: List shows endpoints with pagination
  GIVEN endpoints exist in the user's tenant
  WHEN the user navigates to /endpoints
  THEN a paginated table displays endpoints (50 per page)
  AND  columns: Hostname, OS, Agent version, Last heartbeat, Status, Group

AC4.1.2: Filters and search
  GIVEN the endpoints list is rendered
  WHEN the user applies filters
  THEN the table updates by:
       - Status: All / Online / Offline / Isolated
       - OS: Windows 7 / 10 / 11
       - Group: (configurable)
       - Last heartbeat: < 1 hour / 1-24 hours / > 24 hours

AC4.1.3: Status indicators
  GIVEN endpoints are listed
  WHEN the table is rendered
  THEN status is color-coded:
       - Green: online (heartbeat < 5 min)
       - Yellow: stale (heartbeat 5-60 min)
       - Red: offline (heartbeat > 60 min)
       - Black: isolated

AC4.1.4: Row actions depend on role
  GIVEN the user role
  WHEN viewing the list
  THEN row actions are available:
       - tenant_admin + security_analyst: [Isolate] [View details]
       - read_only_auditor: [View details] only
```

### Technical Notes

- Route: `/endpoints`
- API: `GET /api/v1/endpoints`

---

## US4.2 — Endpoint detail view

**Story ID**: `US4.2`
**Story Points**: 3 SP
**Estimated Effort**: 0.25 day

**As a** authenticated user
**I want to** view all details of a specific endpoint
**So that** I have full context for any action

### Acceptance Criteria

```gherkin
AC4.2.1: Header shows endpoint metadata
  GIVEN an endpoint exists with ID X
  WHEN the user navigates to /endpoints/X
  THEN the header displays:
       - Hostname (editable for tenant_admin)
       - Operating system + version
       - Agent version
       - Last heartbeat
       - Status

AC4.2.2: Detail tabs
  GIVEN the endpoint detail page is rendered
  WHEN data loads
  THEN tabs are available:
       - Overview (KPIs, recent incidents, configuration)
       - Modules (status of each: SENTINEL, ENTROPY, etc.)
       - History (heartbeat timeline, configuration changes)
       - Policies (applied policies)

AC4.2.3: Linked incidents
  GIVEN the endpoint has associated incidents
  WHEN the user views the Overview tab
  THEN a list of recent incidents on this endpoint is displayed
  AND  each is clickable to navigate to incident detail
```

### Technical Notes

- Route: `/endpoints/:id`
- API: `GET /api/v1/endpoints/:id`

---

## US4.3 — Isolate endpoint

**Story ID**: `US4.3`
**Story Points**: 5 SP
**Estimated Effort**: 0.5 day

**As a** user with role `tenant_admin` OR `security_analyst`
**I want to** isolate a compromised endpoint
**So that** I prevent ransomware lateral movement

### Acceptance Criteria

```gherkin
AC4.3.1: Isolate endpoint confirmation flow
  GIVEN an endpoint with status "online"
  AND   the user has role tenant_admin OR security_analyst
  WHEN they click "Isolate Endpoint"
  THEN a confirmation modal displays:
       - Warning text in red
       - Reason field (required, 20-500 chars)
       - "I understand this will disconnect the endpoint from the network" checkbox

AC4.3.2: Isolation is enacted
  GIVEN the user confirms the isolation
  WHEN the API call is made
  THEN the API returns HTTP 202 (Accepted)
  AND  the endpoint status changes to "isolating" then "isolated" within 30s
  AND  the agent on the endpoint disconnects all non-management network traffic
  AND  an audit log entry "endpoint_isolated" is created with reason and user ID

AC4.3.3: Restore isolated endpoint
  GIVEN an endpoint with status "isolated"
  AND   the user has role tenant_admin OR security_analyst
  WHEN they click "Restore Network"
  THEN a confirmation modal asks for justification (required, 20-500 chars)
  AND  upon confirmation, the endpoint status changes back to "online"
  AND  an audit log entry "endpoint_restored" is created

AC4.3.4: read_only_auditor cannot isolate
  GIVEN a user with role read_only_auditor
  WHEN they view an endpoint
  THEN no "Isolate" or "Restore" buttons are visible
  AND  direct API calls return HTTP 403
```

### Technical Notes

- API: `POST /api/v1/endpoints/:id/isolate`, `POST /api/v1/endpoints/:id/restore`
- WebSocket or polling for status transition

---

## US4.4 — Deploy agent (MSI installer download)

**Story ID**: `US4.4`
**Story Points**: 5 SP
**Estimated Effort**: 0.5 day

**As a** user with role `tenant_admin` OR `security_analyst`
**I want to** download a pre-configured MSI installer for the agent
**So that** I can deploy the agent on new endpoints

### Acceptance Criteria

```gherkin
AC4.4.1: Generate installer
  GIVEN a user with role tenant_admin OR security_analyst
  WHEN they navigate to /endpoints/deploy
  THEN a form is displayed with:
       - Group selection (or "default")
       - Initial policy selection
       - Comment field (optional)
       - "Generate Installer" button

AC4.4.2: Download MSI
  GIVEN the form is submitted
  WHEN the user clicks "Generate Installer"
  THEN the backend generates a pre-configured MSI bundled with:
       - The tenant's CA certificate
       - The agent enrollment bootstrap secret (single-use, 24h TTL)
       - The GRID server endpoint URL
  AND  the MSI is downloaded
  AND  an audit log entry "installer_generated" is created

AC4.4.3: Installation instructions displayed
  GIVEN the installer was generated
  WHEN the download completes
  THEN on-screen instructions display step-by-step deployment guide
  AND  instructions are bilingual (FR/EN based on user preference)
  AND  a "Print" button generates a PDF deployment runbook
```

### Technical Notes

- Route: `/endpoints/deploy`
- API: `POST /api/v1/endpoints/generate-installer`
- MSI bundling: server-side process

---

## US4.5 — Bulk actions

**Story ID**: `US4.5`
**Story Points**: 3 SP
**Estimated Effort**: 0.25 day

**As a** user with role `security_analyst`
**I want to** apply actions to multiple endpoints at once
**So that** I save time during large-scale operations

### Acceptance Criteria

```gherkin
AC4.5.1: Multi-select in endpoints table
  GIVEN the endpoints list is rendered
  WHEN the user with role security_analyst checks rows
  THEN a floating action bar appears at the bottom
  AND  the bar shows: count of selected + bulk action buttons

AC4.5.2: Bulk actions available
  GIVEN endpoints are selected
  WHEN the floating bar is visible
  THEN buttons are:
       - "Apply policy"
       - "Update agent"
       - "Isolate all" (with confirmation)
       - "Restore all" (with confirmation)
       - "Export to CSV"

AC4.5.3: Bulk operation feedback
  GIVEN a bulk action is in progress
  WHEN the API processes endpoints sequentially
  THEN a progress indicator shows: "Processing X / Y endpoints..."
  AND  on completion, a summary toast shows success/failure counts
  AND  audit log entries are created for each individual endpoint affected
```

### Technical Notes

- API: `POST /api/v1/endpoints/bulk-action`
- Backend processes in background, returns operation ID for polling

---

# EPIC 5 — Compliance Reporting

**Epic ID**: `EPIC-REPORTS`
**Estimated Effort**: 1.5 days
**Story Points**: 13 SP
**Business Value**: CRITICAL — Differentiator vs Kaspersky/CrowdStrike
**Risk Level**: HIGH (legal compliance)

### Epic Goal

Generate ANTIC-compliant compliance reports (PDF) signed by tenant_admin, downloadable by tenant_admin and read_only_auditor.

### User Stories in this Epic

- US5.1 — Generate monthly compliance report
- US5.2 — Sign compliance report
- US5.3 — Download compliance report PDF
- US5.4 — Submit report to ANTIC

---

## US5.1 — Generate monthly compliance report

**Story ID**: `US5.1`
**Story Points**: 5 SP
**Estimated Effort**: 0.5 day

**As a** user with role `tenant_admin`
**I want to** generate a monthly compliance report
**So that** I can submit it to ANTIC as required by Loi 2024/017

### Acceptance Criteria

```gherkin
AC5.1.1: Generate report
  GIVEN a user with role tenant_admin
  WHEN they navigate to /reports/compliance and click "Generate report"
  THEN a form prompts for:
       - Month (default: previous calendar month)
       - Year
       - Language (FR default, EN optional)
  AND  upon submission, the backend aggregates compliance data
  AND  a PDF preview is displayed in the browser
  AND  an audit log entry "report_generated" is created

AC5.1.2: Report content includes mandatory sections
  GIVEN a report is generated
  WHEN the PDF is rendered
  THEN it contains the following sections:
       - Hospital identification (name, ANTIC registration number)
       - Period covered
       - Summary of incidents (count by severity)
       - Detection performance metrics
       - Policy compliance status
       - Audit log integrity verification
       - Generated by: (user name + role + timestamp)

AC5.1.3: Cannot generate without sufficient data
  GIVEN a user attempts to generate a report for a month with < 7 days of data
  WHEN they submit the form
  THEN the API returns HTTP 422 with message "Insufficient data for the period"

AC5.1.4: read_only_auditor cannot generate
  GIVEN a user with role read_only_auditor
  WHEN they navigate to /reports/compliance
  THEN no "Generate report" button is visible
  AND  direct API call returns HTTP 403
```

### Technical Notes

- Route: `/reports/compliance`
- API: `POST /api/v1/reports/compliance/generate`
- PDF: server-side composition (ReportLab, WeasyPrint, or similar)

---

## US5.2 — Sign compliance report

**Story ID**: `US5.2`
**Story Points**: 3 SP
**Estimated Effort**: 0.25 day

**As a** user with role `tenant_admin`
**I want to** digitally sign a generated compliance report
**So that** ANTIC can verify its authenticity

### Acceptance Criteria

```gherkin
AC5.2.1: Sign report
  GIVEN a generated report in "draft" status
  AND   the user has role tenant_admin
  WHEN they click "Sign report"
  THEN a confirmation modal asks for re-authentication (password + TOTP)
  AND  upon successful re-authentication, the report is signed (Ed25519)
  AND  the signature is embedded in the PDF metadata
  AND  the report status changes to "signed"
  AND  an audit log entry "report_signed" is created

AC5.2.2: Signed report is immutable
  GIVEN a report with status "signed"
  WHEN any user attempts modification
  THEN the API returns HTTP 409 (Conflict)

AC5.2.3: Only tenant_admin can sign
  GIVEN a user with role other than tenant_admin
  WHEN they view a report
  THEN no "Sign" button is visible
  AND  direct API call returns HTTP 403
```

### Technical Notes

- API: `POST /api/v1/reports/:id/sign`
- Cryptography: Ed25519 via libsodium

---

## US5.3 — Download compliance report PDF

**Story ID**: `US5.3`
**Story Points**: 2 SP
**Estimated Effort**: 0.25 day

**As a** user with role `tenant_admin` OR `read_only_auditor`
**I want to** download a signed compliance report
**So that** I can archive it or submit it externally

### Acceptance Criteria

```gherkin
AC5.3.1: Download signed report
  GIVEN a report with status "signed"
  AND   the user has role tenant_admin OR read_only_auditor
  WHEN they click "Download PDF"
  THEN the PDF is streamed to the browser
  AND  the filename format is: "RGCM_Compliance_<HospitalCode>_<YYYY-MM>.pdf"
  AND  an audit log entry "report_downloaded" is created

AC5.3.2: Cannot download draft reports
  GIVEN a report with status "draft"
  WHEN a non-creator user attempts download
  THEN the API returns HTTP 404 (to avoid information leakage)
```

### Technical Notes

- API: `GET /api/v1/reports/:id/download`

---

## US5.4 — Submit report to ANTIC

**Story ID**: `US5.4`
**Story Points**: 3 SP
**Estimated Effort**: 0.25 day

**As a** user with role `tenant_admin`
**I want to** submit a signed report to ANTIC directly from the console
**So that** I avoid manual transmission and ensure delivery confirmation

### Acceptance Criteria

```gherkin
AC5.4.1: Submit signed report
  GIVEN a report with status "signed"
  AND   ANTIC submission endpoint is configured in tenant settings
  WHEN the user clicks "Submit to ANTIC"
  THEN the API transmits the report via the configured channel (HTTP API or SMTP)
  AND  upon successful delivery, the report status changes to "submitted"
  AND  the delivery confirmation (message ID, timestamp) is recorded
  AND  an audit log entry "report_submitted" is created

AC5.4.2: Handle submission failure
  GIVEN ANTIC endpoint is unreachable or returns error
  WHEN submission is attempted
  THEN the report status remains "signed"
  AND  an error toast displays the failure reason
  AND  a retry button is offered

AC5.4.3: ANTIC submission disabled if not configured
  GIVEN tenant settings do not have ANTIC endpoint configured
  WHEN the user views a signed report
  THEN the "Submit to ANTIC" button is disabled with tooltip
       "Configure ANTIC endpoint in Settings"
```

### Technical Notes

- API: `POST /api/v1/reports/:id/submit-antic`
- Submission protocol: configurable per tenant

---

# EPIC 6 — User & Role Management

**Epic ID**: `EPIC-USERS`
**Estimated Effort**: 1.25 days
**Story Points**: 13 SP
**Business Value**: MEDIUM — Tenant administration
**Risk Level**: HIGH (privilege management)

### Epic Goal

Allow tenant_admin to create, configure, and deactivate user accounts within their tenant, with role assignment and MFA enforcement.

### User Stories in this Epic

- US6.1 — Create user account
- US6.2 — Assign / change role
- US6.3 — Deactivate user
- US6.4 — Audit user actions

---

## US6.1 — Create user account

**Story ID**: `US6.1`
**Story Points**: 5 SP
**Estimated Effort**: 0.5 day

**As a** user with role `tenant_admin`
**I want to** create a new user account in my tenant
**So that** colleagues can access the system

### Acceptance Criteria

```gherkin
AC6.1.1: Create user via form
  GIVEN a user with role tenant_admin navigates to /users
  WHEN they click "Add User"
  THEN a form is displayed with fields:
       - Full name (required)
       - Email (required, validated format, unique within tenant)
       - Role (dropdown: tenant_admin, security_analyst, read_only_auditor)
       - Language preference (FR/EN, default FR)
       - Send invitation email checkbox (default checked)

AC6.1.2: Account is created
  GIVEN the form is submitted with valid data
  WHEN the API call succeeds
  THEN a new user record is persisted with status "pending_activation"
  AND  if "Send invitation" was checked, an email is sent with:
       - Activation link (valid 7 days)
       - Instructions for first login and MFA setup
  AND  the user appears in the users list
  AND  an audit log entry "user_created" is created

AC6.1.3: Email uniqueness within tenant
  GIVEN an email already exists in the tenant
  WHEN the form is submitted with that email
  THEN the API returns HTTP 409 (Conflict)
  AND  the form displays an inline error

AC6.1.4: Role assignment respects RBAC
  GIVEN a user with role security_analyst attempts to access /users
  WHEN they navigate to the route
  THEN they are redirected to /dashboard with HTTP 403
  AND  no user management UI is visible

AC6.1.5: Cannot create super_admin
  GIVEN a user with role tenant_admin creates a new user
  WHEN they select role
  THEN the dropdown only shows: tenant_admin, security_analyst, read_only_auditor
  AND  super_admin is NOT in the list
```

### Technical Notes

- Route: `/users`
- API: `POST /api/v1/users`

---

## US6.2 — Assign / change role

**Story ID**: `US6.2`
**Story Points**: 3 SP
**Estimated Effort**: 0.25 day

**As a** user with role `tenant_admin`
**I want to** change the role of an existing user
**So that** I can promote or demote team members as needed

### Acceptance Criteria

```gherkin
AC6.2.1: Change role
  GIVEN a user account exists in the tenant
  AND   the requester has role tenant_admin
  AND   the target is NOT the requester themselves
  WHEN the requester clicks "Change role" on the target user's row
  THEN a dropdown appears with role options
  AND  upon selection and confirmation, the role assignment is updated
  AND  an audit log entry "user_role_changed" is created with from/to values

AC6.2.2: Cannot change own role (privilege escalation prevention)
  GIVEN a user views their own account in /users
  WHEN they attempt to change their own role
  THEN the "Change role" action is disabled with tooltip "Cannot modify own role"
  AND  direct API call returns HTTP 403

AC6.2.3: Last tenant_admin protection
  GIVEN exactly one user has role tenant_admin in the tenant
  WHEN any attempt is made to change their role or deactivate them
  THEN the API returns HTTP 422 with message
       "Cannot remove the last administrator. Promote another user first."
```

### Technical Notes

- API: `PATCH /api/v1/users/:id/role`

---

## US6.3 — Deactivate user

**Story ID**: `US6.3`
**Story Points**: 3 SP
**Estimated Effort**: 0.25 day

**As a** user with role `tenant_admin`
**I want to** deactivate a user account
**So that** former employees lose access immediately

### Acceptance Criteria

```gherkin
AC6.3.1: Deactivate user
  GIVEN an active user in the tenant
  AND   the requester has role tenant_admin
  AND   the target is NOT the requester
  AND   the target is NOT the last tenant_admin
  WHEN the requester clicks "Deactivate" on the target user
  THEN a confirmation modal asks for justification (required, 20-500 chars)
  AND  upon confirmation, the user status changes to "deactivated"
  AND  all active sessions for the target are invalidated immediately
  AND  the target cannot log in anymore
  AND  an audit log entry "user_deactivated" is created

AC6.3.2: Cannot deactivate self
  GIVEN a user views their own account
  WHEN they attempt to deactivate themselves
  THEN the action is disabled
  AND  direct API call returns HTTP 403

AC6.3.3: Reactivate user
  GIVEN a user with status "deactivated"
  AND   the requester has role tenant_admin
  WHEN the requester clicks "Reactivate"
  THEN a confirmation prompt appears
  AND  upon confirmation, the user status changes to "active"
  AND  the user must complete MFA setup again on next login
```

### Technical Notes

- API: `PATCH /api/v1/users/:id/status`

---

## US6.4 — Audit user actions

**Story ID**: `US6.4`
**Story Points**: 2 SP
**Estimated Effort**: 0.25 day

**As a** user with role `tenant_admin` OR `read_only_auditor`
**I want to** see all actions performed by a specific user
**So that** I can investigate suspicious behavior or audit compliance

### Acceptance Criteria

```gherkin
AC6.4.1: User audit trail
  GIVEN a user with role tenant_admin OR read_only_auditor
  WHEN they navigate to /users/:id/audit
  THEN a paginated table shows all audit log entries for this user
  AND  columns: Timestamp, Action, Target resource, IP address

AC6.4.2: Filter audit trail
  GIVEN the audit trail is rendered
  WHEN the user applies filters (date range, action type)
  THEN the list updates accordingly

AC6.4.3: Export audit trail
  GIVEN the audit trail is displayed
  WHEN the user clicks "Export to CSV"
  THEN a CSV file is downloaded with all filtered entries
  AND  the export action is itself logged
```

### Technical Notes

- Route: `/users/:id/audit`
- API: `GET /api/v1/audit-logs?user_id=:id`

---

# EPIC 7 — Threat Intelligence Display

**Epic ID**: `EPIC-THREAT-INTEL`
**Estimated Effort**: 0.5 day
**Story Points**: 5 SP
**Business Value**: MEDIUM
**Risk Level**: LOW

### Epic Goal

Display the status and content of the five threat intelligence sources to tenant_admin and security_analyst.

### User Stories in this Epic

- US7.1 — Threat intelligence status overview
- US7.2 — Latest threat intel package details

---

## US7.1 — Threat intelligence status overview

**Story ID**: `US7.1`
**Story Points**: 3 SP
**Estimated Effort**: 0.25 day

**As a** user with role `tenant_admin` OR `security_analyst`
**I want to** see the status of the five threat intelligence sources
**So that** I know my detection capabilities are up to date

### Acceptance Criteria

```gherkin
AC7.1.1: Sources overview
  GIVEN a user with role tenant_admin OR security_analyst navigates to /threat-intel
  WHEN the page loads
  THEN a card grid shows the 5 threat intel sources:
       - Tor Project (exit nodes)
       - ThreatFox (abuse.ch)
       - AlienVault OTX
       - AWS IP ranges
       - GCP IP ranges
  AND  each card displays:
       - Source name
       - Last successful sync timestamp
       - Number of indicators retrieved
       - Status badge (synced / stale / failed)

AC7.1.2: read_only_auditor sees no threat-intel page
  GIVEN a user with role read_only_auditor
  WHEN they navigate to /threat-intel
  THEN they are redirected to /dashboard with HTTP 403
```

### Technical Notes

- Route: `/threat-intel`
- API: `GET /api/v1/threat-intel/sources`

---

## US7.2 — Latest threat intel package details

**Story ID**: `US7.2`
**Story Points**: 2 SP
**Estimated Effort**: 0.25 day

**As a** user with role `tenant_admin` OR `security_analyst`
**I want to** view details of the latest signed threat intelligence package
**So that** I can verify what is being pushed to agents

### Acceptance Criteria

```gherkin
AC7.2.1: Package details
  GIVEN a user with role tenant_admin OR security_analyst
  WHEN they navigate to /threat-intel/latest
  THEN the page shows:
       - Package version
       - Signature verification status (Ed25519)
       - Total indicators (by category)
       - Generation timestamp
       - Distribution status (how many agents received it)
```

### Technical Notes

- API: `GET /api/v1/threat-intel/latest`

---

# EPIC 8 — Audit Logs Viewer

**Epic ID**: `EPIC-AUDIT`
**Estimated Effort**: 0.75 day
**Story Points**: 8 SP
**Business Value**: HIGH — Compliance + forensics
**Risk Level**: LOW

### Epic Goal

Allow tenant_admin and read_only_auditor to search, filter, and export the hash-chained immutable audit logs.

### User Stories in this Epic

- US8.1 — Search audit logs
- US8.2 — Filter and paginate audit logs
- US8.3 — Export audit trail and verify integrity

---

## US8.1 — Search audit logs

**Story ID**: `US8.1`
**Story Points**: 3 SP
**Estimated Effort**: 0.25 day

**As a** user with role `tenant_admin` OR `read_only_auditor`
**I want to** search audit logs by free text
**So that** I can quickly find specific events

### Acceptance Criteria

```gherkin
AC8.1.1: Free text search
  GIVEN a user with role tenant_admin OR read_only_auditor navigates to /audit
  WHEN they type in the search box
  THEN audit entries are filtered (debounced 300ms)
  AND  search matches: action name, target resource, user identifier
  AND  results are paginated (50 per page)
```

### Technical Notes

- Route: `/audit`
- API: `GET /api/v1/audit-logs?search=...`

---

## US8.2 — Filter and paginate audit logs

**Story ID**: `US8.2`
**Story Points**: 3 SP
**Estimated Effort**: 0.25 day

**As a** user with role `tenant_admin` OR `read_only_auditor`
**I want to** filter audit logs by date, action type, and user
**So that** I can narrow down to relevant events

### Acceptance Criteria

```gherkin
AC8.2.1: Date range filter
  GIVEN the audit logs view is rendered
  WHEN the user selects a date range (from/to)
  THEN entries outside the range are excluded

AC8.2.2: Action type filter
  GIVEN the audit logs view is rendered
  WHEN the user selects one or more action types
       (login, logout, incident_*, endpoint_*, user_*, report_*)
  THEN entries are filtered accordingly

AC8.2.3: User filter
  GIVEN the audit logs view is rendered
  WHEN the user selects a specific user from a dropdown
  THEN entries are filtered to actions performed by that user

AC8.2.4: Pagination
  GIVEN audit logs exist
  WHEN the user navigates pagination controls
  THEN entries are loaded server-side (no full client-side dataset)
```

### Technical Notes

- API: `GET /api/v1/audit-logs?from=...&to=...&action=...&user_id=...&page=N`

---

## US8.3 — Export audit trail and verify integrity

**Story ID**: `US8.3`
**Story Points**: 2 SP
**Estimated Effort**: 0.25 day

**As a** user with role `tenant_admin` OR `read_only_auditor`
**I want to** export the audit trail and verify the hash chain integrity
**So that** I have forensic evidence for legal or compliance purposes

### Acceptance Criteria

```gherkin
AC8.3.1: Export to CSV
  GIVEN audit logs are displayed (with current filters)
  WHEN the user clicks "Export CSV"
  THEN a CSV file is downloaded with all filtered entries
  AND  the file includes columns: timestamp, action, user, target, IP, hash

AC8.3.2: Verify integrity
  GIVEN the audit logs are stored with hash-chained structure
  WHEN the user clicks "Verify chain integrity"
  THEN the backend recomputes hashes and confirms
  AND  a result is displayed:
       - "Chain intact" (green) if no tampering detected
       - "Chain broken at entry X" (red) otherwise

AC8.3.3: Export action is itself logged
  GIVEN any export of audit logs
  WHEN the export completes
  THEN a new audit log entry "audit_exported" is created
```

### Technical Notes

- API: `GET /api/v1/audit-logs/export?...`, `POST /api/v1/audit-logs/verify`

---

## 12. Technical Stack & Architecture

### 12.1 Frontend Stack

| Layer | Technology | Version | Rationale |
|---|---|---|---|
| Framework | React | 18.3+ | Industry standard, mature ecosystem |
| Language | TypeScript | 5.5+ | Type safety mandatory for security product |
| Build tool | Vite | 5.4+ | Fast HMR, modern bundling |
| Styling | TailwindCSS | 3.4+ | Utility-first, design system friendly |
| UI primitives | shadcn/ui | latest | Accessible, customizable, copy-paste model |
| State (server) | TanStack Query | 5.x | Server state caching, refetching, mutations |
| State (client) | Zustand | 4.x | Lightweight global state for auth, UI |
| Routing | React Router | 6.26+ | SPA routing with protected routes |
| Forms | React Hook Form + Zod | latest | Performant + type-safe validation |
| HTTP | Axios | 1.x | Interceptors for auth, error handling |
| i18n | react-i18next | 15.x | French/English support |
| Charts | Recharts | 2.x | Lightweight, responsive |
| Dates | date-fns | 4.x | Timezone-aware, locale-aware |
| Icons | Lucide React | latest | Tree-shakeable, consistent design |
| Tests (unit) | Vitest | latest | Fast, Vite-native |
| Tests (component) | Testing Library | latest | User-centric testing |
| Tests (E2E) | Playwright | latest | Cross-browser automation |

### 12.2 Project Structure

```
ransomguard-cm/
└── console/                          # Sprint 7 deliverable
    ├── public/
    │   ├── locales/
    │   │   ├── fr.json               # All FR translations
    │   │   └── en.json               # All EN translations
    │   └── favicon.svg
    │
    ├── src/
    │   ├── main.tsx                  # Entry point
    │   ├── App.tsx                   # Root component + router
    │   │
    │   ├── api/                      # API layer (Axios)
    │   │   ├── client.ts             # Axios instance + interceptors
    │   │   ├── auth.ts
    │   │   ├── incidents.ts
    │   │   ├── endpoints.ts
    │   │   ├── reports.ts
    │   │   ├── users.ts
    │   │   ├── audit.ts
    │   │   └── threat-intel.ts
    │   │
    │   ├── components/               # Reusable components
    │   │   ├── ui/                   # shadcn/ui primitives
    │   │   ├── layout/               # AppShell, Sidebar, Header
    │   │   ├── charts/               # KPI cards, trend lines
    │   │   ├── tables/               # DataTable generic
    │   │   ├── forms/                # Form wrappers
    │   │   └── auth/                 # Auth-related components
    │   │
    │   ├── pages/                    # Route pages
    │   │   ├── auth/
    │   │   │   ├── LoginPage.tsx
    │   │   │   ├── MFAVerifyPage.tsx
    │   │   │   ├── ForgotPasswordPage.tsx
    │   │   │   └── ResetPasswordPage.tsx
    │   │   ├── dashboard/
    │   │   │   ├── DashboardPage.tsx
    │   │   │   ├── ExecutiveDashboard.tsx
    │   │   │   ├── OperationalDashboard.tsx
    │   │   │   └── AuditorDashboard.tsx
    │   │   ├── incidents/
    │   │   │   ├── IncidentsListPage.tsx
    │   │   │   ├── IncidentDetailPage.tsx
    │   │   │   └── ForensicTrailPage.tsx
    │   │   ├── endpoints/
    │   │   │   ├── EndpointsListPage.tsx
    │   │   │   ├── EndpointDetailPage.tsx
    │   │   │   └── DeployAgentPage.tsx
    │   │   ├── reports/
    │   │   │   ├── ReportsListPage.tsx
    │   │   │   └── GenerateReportPage.tsx
    │   │   ├── users/
    │   │   │   ├── UsersListPage.tsx
    │   │   │   └── UserAuditPage.tsx
    │   │   ├── threat-intel/
    │   │   │   ├── ThreatIntelStatusPage.tsx
    │   │   │   └── LatestPackagePage.tsx
    │   │   └── audit/
    │   │       └── AuditLogsPage.tsx
    │   │
    │   ├── hooks/                    # Custom hooks
    │   │   ├── useAuth.ts
    │   │   ├── useRole.ts
    │   │   ├── usePermission.ts
    │   │   ├── useTenant.ts
    │   │   └── useIdleTimer.ts
    │   │
    │   ├── stores/                   # Zustand stores
    │   │   ├── authStore.ts
    │   │   └── uiStore.ts
    │   │
    │   ├── types/                    # TypeScript types
    │   │   ├── api.ts
    │   │   ├── auth.ts
    │   │   ├── incident.ts
    │   │   ├── endpoint.ts
    │   │   ├── user.ts
    │   │   └── audit.ts
    │   │
    │   ├── lib/                      # Utilities
    │   │   ├── i18n.ts               # react-i18next config
    │   │   ├── validators.ts         # Zod schemas
    │   │   ├── format.ts             # Date/number formatting
    │   │   ├── permissions.ts        # RBAC helpers
    │   │   └── constants.ts          # Role codes, action types
    │   │
    │   └── styles/
    │       └── globals.css           # Tailwind directives
    │
    ├── tests/
    │   ├── unit/                     # Vitest unit tests
    │   ├── integration/              # Component integration tests
    │   └── e2e/                      # Playwright E2E
    │       ├── auth.spec.ts
    │       ├── incidents.spec.ts
    │       ├── endpoints.spec.ts
    │       └── reports.spec.ts
    │
    ├── package.json
    ├── tsconfig.json
    ├── vite.config.ts
    ├── tailwind.config.ts
    ├── playwright.config.ts
    └── README.md
```

### 12.3 RBAC Implementation Pattern

```typescript
// src/lib/permissions.ts

export const ROLES = {
  SUPER_ADMIN: 'super_admin',
  TENANT_ADMIN: 'tenant_admin',
  SECURITY_ANALYST: 'security_analyst',
  READ_ONLY_AUDITOR: 'read_only_auditor',
} as const;

export type Role = typeof ROLES[keyof typeof ROLES];

export const PERMISSIONS = {
  // Incidents
  'incidents:read': [ROLES.TENANT_ADMIN, ROLES.SECURITY_ANALYST, ROLES.READ_ONLY_AUDITOR],
  'incidents:acknowledge': [ROLES.TENANT_ADMIN, ROLES.SECURITY_ANALYST],
  'incidents:close': [ROLES.TENANT_ADMIN, ROLES.SECURITY_ANALYST],

  // Endpoints
  'endpoints:read': [ROLES.TENANT_ADMIN, ROLES.SECURITY_ANALYST, ROLES.READ_ONLY_AUDITOR],
  'endpoints:isolate': [ROLES.TENANT_ADMIN, ROLES.SECURITY_ANALYST],
  'endpoints:deploy': [ROLES.TENANT_ADMIN, ROLES.SECURITY_ANALYST],

  // Reports
  'reports:read': [ROLES.TENANT_ADMIN, ROLES.READ_ONLY_AUDITOR],
  'reports:generate': [ROLES.TENANT_ADMIN],
  'reports:sign': [ROLES.TENANT_ADMIN],

  // Users
  'users:read': [ROLES.TENANT_ADMIN],
  'users:create': [ROLES.TENANT_ADMIN],
  'users:update': [ROLES.TENANT_ADMIN],

  // Audit
  'audit:read': [ROLES.TENANT_ADMIN, ROLES.READ_ONLY_AUDITOR],
  'audit:export': [ROLES.TENANT_ADMIN, ROLES.READ_ONLY_AUDITOR],

  // Settings
  'settings:read': [ROLES.TENANT_ADMIN],
  'settings:update': [ROLES.TENANT_ADMIN],
} as const;

export function hasPermission(userRoles: Role[], permission: keyof typeof PERMISSIONS): boolean {
  const allowedRoles = PERMISSIONS[permission];
  return userRoles.some(role => (allowedRoles as readonly Role[]).includes(role));
}
```

### 12.4 Route Protection Pattern

```typescript
// src/components/auth/RequirePermission.tsx

import { Navigate } from 'react-router-dom';
import { useAuth } from '@/hooks/useAuth';
import { hasPermission, type PERMISSIONS } from '@/lib/permissions';

interface Props {
  permission: keyof typeof PERMISSIONS;
  children: React.ReactNode;
}

export function RequirePermission({ permission, children }: Props) {
  const { user } = useAuth();

  if (!user) return <Navigate to="/auth/login" replace />;
  if (!hasPermission(user.roles, permission)) {
    return <Navigate to="/dashboard" replace />;
  }

  return <>{children}</>;
}
```

---

## 13. Non-Functional Requirements (NFRs)

### 13.1 Performance

| Metric | Target |
|---|---|
| Lighthouse Performance score | ≥ 90 |
| First Contentful Paint (FCP) | < 1.5 s |
| Largest Contentful Paint (LCP) | < 2.5 s |
| Time to Interactive (TTI) | < 3.5 s |
| API call response | < 500 ms p95 |
| Page navigation | < 1 s |
| Search debounce | 300 ms |

### 13.2 Security

- HTTPS only (HSTS enforced)
- All authentication endpoints behind rate limiting
- CSP (Content Security Policy) headers configured
- No localStorage for tokens (HttpOnly cookies only for refresh; memory for access)
- XSS prevention via React escaping + DOMPurify for any rich text rendering
- CSRF protection via SameSite cookies + CSRF tokens for state-changing operations

### 13.3 Accessibility

| Standard | Target |
|---|---|
| WCAG 2.1 conformance | Level AA |
| Keyboard navigation | 100% of features |
| Screen reader compatibility | NVDA, JAWS, VoiceOver |
| Color contrast | All text ≥ 4.5:1 |
| Focus indicators | Visible on all interactive elements |

### 13.4 Internationalization

- Default language: French (FR)
- Secondary language: English (EN)
- All user-facing strings externalized in `public/locales/*.json`
- Date/number/currency formatting locale-aware
- Right-to-left (RTL) support: not required for Sprint 7

### 13.5 Browser Support

- Chrome ≥ 110
- Firefox ≥ 110
- Edge ≥ 110
- Safari ≥ 16
- No IE11 support

### 13.6 Responsive Design

| Breakpoint | Min width | Notes |
|---|---|---|
| Mobile | 320 px | Critical paths only |
| Tablet | 768 px | Full functionality |
| Desktop | 1024 px | Primary target |
| Large desktop | 1440 px | Optimal experience |

### 13.7 Test Coverage

| Test Type | Coverage Target |
|---|---|
| Unit (Vitest) | ≥ 75% statements |
| Component (Testing Library) | All pages + critical components |
| E2E (Playwright) | All Epic happy paths + RBAC enforcement |

---

## 14. Sprint Backlog & Estimation

### 14.1 Summary by Epic

| Epic | Stories | Story Points | Estimated Days |
|---|:---:|:---:|:---:|
| EPIC-AUTH | 5 | 16 | 1.5 |
| EPIC-DASHBOARD | 4 | 13 | 1.5 |
| EPIC-INCIDENTS | 5 | 21 | 2.0 |
| EPIC-ENDPOINTS | 5 | 21 | 1.5 |
| EPIC-REPORTS | 4 | 13 | 1.5 |
| EPIC-USERS | 4 | 13 | 1.25 |
| EPIC-THREAT-INTEL | 2 | 5 | 0.5 |
| EPIC-AUDIT | 3 | 8 | 0.75 |
| **TOTAL** | **32** | **110** | **10.5** |

### 14.2 Sequencing Strategy

Implementation order should follow dependency chains, not Epic numbering:

1. **Foundation (Days 1-2)**: Project bootstrap, design system, i18n, auth (EPIC-AUTH)
2. **Layout & navigation (Day 2-3)**: AppShell, sidebar, header, role-based routing
3. **Core operational (Days 3-6)**: EPIC-INCIDENTS + EPIC-ENDPOINTS
4. **Dashboard (Days 6-7)**: EPIC-DASHBOARD (consumes data from previous epics)
5. **Administration (Days 7-9)**: EPIC-USERS + EPIC-REPORTS
6. **Supporting features (Day 9-10)**: EPIC-THREAT-INTEL + EPIC-AUDIT
7. **Polish & E2E (Days 10-11)**: Testing, accessibility audit, performance tuning
8. **Tag & release (Day 12)**: `v0.9.0-console`

### 14.3 Buffer & Risk Allocation

- **10% buffer** included in estimates (industry standard)
- High-risk Epics (AUTH, USERS, REPORTS) have dedicated buffer

---

## 15. Definition of Done (DoD)

A User Story is **Done** when ALL of the following are true:

- [ ] All Acceptance Criteria pass automated tests
- [ ] Unit tests written, covering ≥ 75% of new code
- [ ] At least one E2E test exercises the happy path
- [ ] RBAC enforcement verified at both UI and API call levels
- [ ] No hardcoded user names anywhere
- [ ] All user-facing strings in `fr.json` and `en.json`
- [ ] Accessibility audit passes (no critical violations)
- [ ] Lighthouse scores meet NFR thresholds
- [ ] TypeScript compiles with no errors and no `any` types in business logic
- [ ] Code reviewed (self-review with SR-1 protocol since solo developer)
- [ ] Documentation updated (component docs, README sections)
- [ ] No console errors or warnings in browser
- [ ] No security warnings from `npm audit`
- [ ] Git commit follows conventional commit format
- [ ] User Story marked as Done in tracking system

A Sprint is **Done** when ALL of the following are true:

- [ ] 32 of 32 User Stories meet Story DoD
- [ ] Full test suite passes (unit + integration + E2E)
- [ ] Lighthouse audit run on all key pages, results documented
- [ ] Accessibility audit run, results documented
- [ ] CHANGELOG.md updated with all changes
- [ ] README.md updated with new features and setup instructions
- [ ] Tag `v0.9.0-console` pushed to GitHub
- [ ] Sprint retrospective document written

---

## 16. Risk Register

| ID | Risk | Probability | Impact | Mitigation |
|---|---|:---:|:---:|---|
| R1 | Backend API does not expose all needed endpoints | MEDIUM | HIGH | Audit GRID OpenAPI spec before Day 1; add missing endpoints in micro-sprint |
| R2 | WebSocket support absent in backend | MEDIUM | MEDIUM | Fall back to polling (TanStack Query refetchInterval); confirm in API audit |
| R3 | PDF generation library incompatible with FR Unicode characters | LOW | MEDIUM | Test with sample French text including accents (à, é, ç) early |
| R4 | MFA TOTP implementation deviates from RFC 6238 | LOW | HIGH | Use battle-tested library, write test against Google Authenticator |
| R5 | Multi-tenant isolation bug exposes cross-tenant data | LOW | CRITICAL | Backend already has 10/10 isolation tests; reuse same tests at API level |
| R6 | Time estimate underrun due to undisclosed complexity | HIGH | MEDIUM | 10% buffer in estimates; descope EPIC-THREAT-INTEL US7.2 if necessary |
| R7 | Accessibility audit reveals major issues late | MEDIUM | MEDIUM | Use axe-core in development; run audit at end of each Epic |
| R8 | Bilingual UI introduces broken strings or layout issues | MEDIUM | LOW | All translations in resource files from Day 1; visual regression test for both languages |

---

## 17. Out of Scope

Explicitly NOT included in Sprint 7:

- ❌ Native mobile applications (iOS/Android)
- ❌ Multi-tenant `super_admin` console
- ❌ Cross-tenant aggregation views for read_only_auditor
- ❌ Real-time WebSocket implementation (polling acceptable for v1)
- ❌ Advanced visualization library (D3.js custom charts)
- ❌ Dashboard customization per user (saved layouts)
- ❌ Dark mode (Sprint 8 candidate)
- ❌ MSI installer build automation (handled by Sprint 8)
- ❌ ANTIC API integration (mock submission acceptable; real integration Sprint 8)
- ❌ Performance load testing at 300 concurrent users (Sprint 6.5 deliverable)
- ❌ Penetration testing of the console (post-Sprint 8)

---

**End of PRD**

*This document is the single source of truth for Sprint 7 implementation. Any deviation requires explicit decision and documentation update.*
