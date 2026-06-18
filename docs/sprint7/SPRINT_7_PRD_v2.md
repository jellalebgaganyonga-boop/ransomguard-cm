# Sprint 7 v2 — RansomGuard Console: Production Web Dashboard (Adjusted)

**Document Type**: Product Requirements Document (PRD) — Engineering Specification
**Document Version**: 2.0 (Adjusted to actual backend audit)
**Status**: Ready for Implementation
**Authored by**: Staff Product Manager + Tech Lead
**Target Audience**: Claude Code (implementation agent)
**Sprint Code Name**: Sprint 7 "RansomGuard Console"
**Target Git Tag**: `v0.9.0-console`
**Estimated Duration**: 9–11 working days (Phase 0 + Phase 1)

---

## CHANGE LOG — v1.0 → v2.0

This document supersedes `SPRINT_7_PRD.md` (v1.0). It was rewritten after a complete audit of the actual backend codebase (`v0.8.0-grid`) performed by Claude Code on 2026-06-07. The audit revealed that v1.0 contained several inaccurate assumptions about backend capabilities. v2.0 reflects empirical facts only.

### Key adjustments

1. **Terminology aligned with backend**: "Incidents" → "Alerts" (the backend has an `Alert` model, no `Incident` model exists).
2. **Endpoint surface mapped to real routes**: the audit confirmed 12 dashboard endpoints already exist under `/api/v1/dashboard/*`. Sprint 7 consumes these, does not invent endpoints.
3. **Out-of-scope items descoped**: Compliance Reports (no model), MFA TOTP (no endpoints), Password reset (no endpoint), Forensic trail UI (no exposed process tree API), Bulk actions (no bulk endpoint) → all moved to Sprint 8.
4. **Phase 0 added**: 2 backend endpoints required before frontend can start (`GET /me`, `POST /users/{id}/enable`).
5. **Story count reduced**: 32 stories (110 SP) → 22 stories (88 SP). Reflects what can actually be built against the current backend.
6. **Frontend location confirmed**: `dashboard/` folder (already exists as empty placeholder with intended README).
7. **Roles confirmed**: `tenant_admin`, `security_analyst`, `read_only_auditor` (seeded in database). No `super_admin` exists in Sprint 7 scope.

### What stays unchanged from v1.0

- Sprint discipline (Evidence Blocks, Acceptance Criteria first, Closure same day, Validation Gates, Truth Before Velocity)
- Capability-based Epics (not role-based)
- Gherkin Acceptance Criteria format
- React 18 + TypeScript + Vite + TailwindCSS + shadcn/ui stack
- Bilingual French/English from day one
- Zero hardcoded user names in source code (only role codes)
- Multi-tenant isolation enforced at UI, API, and database layers

---

## Table of Contents

1. [Sprint Goal](#1-sprint-goal)
2. [Audit-Confirmed Facts](#2-audit-confirmed-facts)
3. [Project Structure Integration Rules](#3-project-structure-integration-rules)
4. [System Roles (RBAC Reference)](#4-system-roles-rbac-reference)
5. [Phase 0 — Backend Quick-Wins](#5-phase-0--backend-quick-wins)
6. [Epic 1 — Authentication & Session](#6-epic-1--authentication--session)
7. [Epic 2 — Dashboard Overview](#7-epic-2--dashboard-overview)
8. [Epic 3 — Alerts Management](#8-epic-3--alerts-management)
9. [Epic 4 — Agents Management](#9-epic-4--agents-management)
10. [Epic 5 — Users Management](#10-epic-5--users-management)
11. [Epic 6 — Audit Logs Viewer](#11-epic-6--audit-logs-viewer)
12. [Technical Stack & Architecture](#12-technical-stack--architecture)
13. [API Contract Mapping](#13-api-contract-mapping)
14. [Non-Functional Requirements](#14-non-functional-requirements)
15. [Sprint Backlog & Estimation](#15-sprint-backlog--estimation)
16. [Definition of Done](#16-definition-of-done)
17. [Risk Register](#17-risk-register)
18. [Out of Scope (Sprint 8 Candidates)](#18-out-of-scope-sprint-8-candidates)

---

## 1. Sprint Goal

**One-sentence Sprint Goal:**

> *Deliver a production-grade React web console located in `dashboard/` that allows the three RBAC roles (`tenant_admin`, `security_analyst`, `read_only_auditor`) to authenticate, monitor alerts, manage agents, and consult audit logs through a bilingual French/English interface consuming the existing GRID Server REST API.*

**Success criteria (binary, measurable):**

- All six Epics implemented and tagged `v0.9.0-console`
- All 22 User Stories pass their Acceptance Criteria
- Test coverage ≥ 75% (unit + integration + E2E)
- Lighthouse Performance score ≥ 90 on dashboard landing page
- Lighthouse Accessibility score ≥ 90 (WCAG 2.1 AA)
- Zero hardcoded user names anywhere in source code
- All user-facing strings externalized in `public/locales/{fr,en}.json`
- Multi-tenant isolation verified by E2E tests covering all roles
- Backend API contract conformance verified against `/openapi.json`
- The frontend builds production bundle (`npm run build`) with no errors
- The frontend integrates against the running `deployment/grid/docker-compose up` stack

---

## 2. Audit-Confirmed Facts

The following facts were established by the Claude Code audit (2026-06-07). They constitute the empirical foundation of this PRD. Implementation MUST conform to these facts.

### 2.1 Backend State

- **Tag**: `v0.8.0-grid` (Sprint 6 backend complete)
- **Stack**: Python 3.12 + FastAPI + SQLAlchemy 2.0 + MySQL 8.4 + Redis 7 + nginx 1.27 (mTLS)
- **Tests**: 94/94 passing, including 6 RBAC distinction tests and 10 multi-tenant isolation tests
- **Deployment**: `deployment/grid/docker-compose up -d` starts the full stack
- **Authentication**: JWT (HS256) for dashboard users, mTLS for agents
- **Models**: 21 SQLAlchemy models + 7 enums in `grid/src/ransomguard_grid/db/models/`

### 2.2 Endpoints Currently Available (23 total)

#### Authentication endpoints (3)

```
POST /api/v1/auth/login        — Email + password → JWT access + refresh tokens
POST /api/v1/auth/refresh      — Refresh token → new access token
POST /api/v1/auth/logout       — Revoke current access token (blacklist JTI)
```

#### Dashboard endpoints (12)

```
GET    /api/v1/dashboard/alerts                          — List alerts (filter + paginate)
GET    /api/v1/dashboard/alerts/{alert_id}               — Alert by ID
POST   /api/v1/dashboard/alerts/{alert_id}/status        — Update alert status
GET    /api/v1/dashboard/agents                          — List agents
GET    /api/v1/dashboard/agents/{agent_id}               — Agent by ID
GET    /api/v1/dashboard/metrics/summary                 — Top KPIs (24h)
POST   /api/v1/dashboard/commands                        — Issue command to agent (admin only)
GET    /api/v1/dashboard/users                           — List users (admin only)
POST   /api/v1/dashboard/users                           — Create user (admin only)
POST   /api/v1/dashboard/users/{user_id}/disable         — Disable user (admin only)
PUT    /api/v1/dashboard/users/{user_id}/roles           — Update roles (admin only)
GET    /api/v1/dashboard/audit-logs                      — Search audit logs (admin + auditor)
```

#### Agent endpoints (5) — out of frontend scope (consumed by agents only)

```
POST /api/v1/agents/enroll                               — OTP enrollment
POST /api/v1/agents/{agent_id}/heartbeat                 — Heartbeat
POST /api/v1/agents/{agent_id}/alerts                    — Single alert ingest
POST /api/v1/agents/{agent_id}/alerts/batch              — Batch ingest
POST /api/v1/agents/{agent_id}/audit-logs                — Ed25519-signed log ingest
```

#### Threat intel endpoints (3) — out of frontend scope (consumed by agents)

```
GET  /api/v1/threat-intel/manifest
GET  /api/v1/threat-intel/package/{version}
POST /api/v1/threat-intel/agents/{agent_id}/threat-intel-version
```

### 2.3 Models Currently Available

```
Tenant, User, Role, UserRole, Session                                (5)
Agent, AgentCertificate, AgentConfiguration, AgentHeartbeat          (4)
Alert, AlertDetail, AlertArtifact, AuditLog,
  AlertCorrelation, AlertStatusChange                                (6)
CommandQueue, CommandResponse, SystemLog                             (3)
ThreatIntelVersion, ThreatIntelPackage, AgentThreatIntelVersion      (3)
Enums: TenantStatus, AgentStatus, Severity, AlertStatus,
       ThreatIntelStatus, CommandStatus, LogLevel                    (7)
```

### 2.4 Models NOT Available (Sprint 8 Candidates)

```
Incident                — for cross-alert correlation grouping
ComplianceReport        — for ANTIC compliance exports
Policy                  — for tenant-level detection rules
Settings                — for tenant configuration
ThreatIndicator         — individual IOC tracking (only package-level exists today)
```

### 2.5 Seeded Roles (database-level, identifier codes)

```
tenant_admin        — Full administrative access within tenant
security_analyst    — Read alerts and update status
read_only_auditor   — Read-only audit access
```

`super_admin` does NOT exist in Sprint 7 scope.

### 2.6 Frontend Folder Status

`C:\Projects\RansomGuard-CM\dashboard\` exists as an empty placeholder with the following structure:

```
dashboard/
├── README.md     ("React 18 + TypeScript + Tailwind CSS bilingual interface")
├── public/       (empty)
├── src/          (empty)
└── tests/        (empty)
```

No `package.json`, no `vite.config.ts`, no source code. Sprint 7 starts from a clean slate.

---

## 3. Project Structure Integration Rules

These rules are NON-NEGOTIABLE. Violations break the integrity of the project.

### 3.1 Sacred folders — DO NOT MODIFY

The following folders contain Sprint 1–6 deliverables. Sprint 7 MUST NOT modify them.

```
agent/                     — C# .NET 8 endpoint agent (built artifacts present)
grid/                      — FastAPI backend (94 tests passing, tag v0.8.0-grid)
deployment/grid/           — Docker Compose stack + PKI + nginx configs
docs/                      — ADRs, architecture, STRIDE analyses, sprint reports
scripts/                   — PowerShell/Bash validation scripts
.github/                   — CI workflows
```

If Sprint 7 implementation requires backend changes, those changes MUST be:
1. Limited to Phase 0 (Section 5) for the two explicitly approved endpoints
2. Accompanied by their own pytest tests
3. Committed in separate commits from frontend work
4. Documented in CHANGELOG.md

### 3.2 Sprint 7 working folder — exclusive territory

All Sprint 7 frontend code MUST live under `dashboard/`. The folder is currently empty (only README.md). Sprint 7 will populate it.

### 3.3 Folders SAFE TO IGNORE for Sprint 7

These exist but are empty placeholders not relevant to Sprint 7:

```
cloud/        — Future cloud module
server/       — Reserved for future management server work
shared/       — Reserved for future protocol/schema sharing
tests/        — Top-level e2e/integration/security dirs (Sprint 7 E2E lives in dashboard/tests/)
```

### 3.4 Git branch policy

- Base branch: `main` (currently at tag `v0.8.0-grid`)
- Sprint 7 work branch: `sprint-7-console`
- All commits include conventional commit prefix: `feat(dashboard):`, `fix(dashboard):`, `chore(dashboard):`, `test(dashboard):`, `docs(dashboard):`
- Backend changes (Phase 0 only): `feat(grid):` prefix
- Tag at end of sprint: `v0.9.0-console`

---

## 4. System Roles (RBAC Reference)

The system supports exactly three roles in Sprint 7 scope. These are role identifiers used in code — they are configurable database records, not hardcoded user names.

| Role code | Display name (FR) | Display name (EN) | Capabilities scope |
|---|---|---|---|
| `tenant_admin` | Administrateur Hôpital | Hospital Administrator | Full administrative access within tenant |
| `security_analyst` | Analyste Sécurité | Security Analyst | Alert triage + agent monitoring, no user management |
| `read_only_auditor` | Auditeur (Lecture seule) | Read-Only Auditor | Read-only access for compliance review |

### Absolute engineering rule

> The system stores ONLY ROLES (configurable, parameterized). It does NOT store hardcoded user names. Personas (e.g., "Mvondo", "Lewis", "Bilo'o") are design artifacts only — they MUST NEVER appear in source code, database queries, API responses, UI strings, comments, or git commit messages.

**Code pattern (correct):**

```typescript
if (hasRole(currentUser, 'tenant_admin')) {
  showAdminMenu();
}
```

**Code pattern (FORBIDDEN):**

```typescript
if (currentUser.name === 'Mvondo') {  // NEVER
  showAdminMenu();
}
```

### Role-to-endpoint permission matrix (derived from audit)

| Endpoint | `tenant_admin` | `security_analyst` | `read_only_auditor` |
|---|:---:|:---:|:---:|
| `GET /dashboard/alerts` | ✅ | ✅ | ✅ |
| `GET /dashboard/alerts/{id}` | ✅ | ✅ | ✅ |
| `POST /dashboard/alerts/{id}/status` | ✅ | ✅ | ❌ |
| `GET /dashboard/agents` | ✅ | ✅ | ✅ |
| `GET /dashboard/agents/{id}` | ✅ | ✅ | ✅ |
| `POST /dashboard/commands` | ✅ | ❌ | ❌ |
| `GET /dashboard/users` | ✅ | ❌ | ❌ |
| `POST /dashboard/users` | ✅ | ❌ | ❌ |
| `POST /dashboard/users/{id}/disable` | ✅ | ❌ | ❌ |
| `PUT /dashboard/users/{id}/roles` | ✅ | ❌ | ❌ |
| `GET /dashboard/audit-logs` | ✅ | ❌ | ✅ |
| `GET /dashboard/metrics/summary` | ✅ | ✅ | ✅ |
| `GET /dashboard/me` (Phase 0) | ✅ | ✅ | ✅ |
| `POST /dashboard/users/{id}/enable` (Phase 0) | ✅ | ❌ | ❌ |

The frontend MUST mirror this matrix in its UI logic (hide buttons, restrict navigation) AND not rely on UI hiding alone — the backend remains authoritative.

---

## 5. Phase 0 — Backend Quick-Wins

Two backend endpoints are missing for the frontend to function correctly. They MUST be implemented before frontend Epic-AUTH can complete. Estimated effort: 1 day total.

### 5.1 New endpoint — `GET /api/v1/dashboard/me`

**Purpose**: Return the authenticated user's profile, roles, and tenant identifier. Required by the frontend immediately after login to render the user menu, determine the role-based landing page, and gate role-restricted features.

**Authorization**: Any authenticated user (any role).

**Response shape (JSON, 200 OK):**

```json
{
  "id": "uuid-string",
  "email": "user@hospital.example",
  "full_name": "Surname FirstName",
  "is_active": true,
  "tenant_id": "uuid-string",
  "tenant_name": "Hospital display name",
  "roles": ["tenant_admin"],
  "last_login_at": "2026-06-07T15:30:00Z",
  "created_at": "2026-01-15T08:00:00Z"
}
```

**Error responses:**
- `401 Unauthorized` if no valid JWT
- `403 Forbidden` if user is disabled or session revoked

**Implementation notes:**
- Add new route in `grid/src/ransomguard_grid/api/v1/routes/dashboard.py`
- Reuse existing `get_current_user` dependency from `dependencies/jwt_auth.py`
- Pydantic response schema `UserMeResponse` in `api/v1/schemas/auth.py`
- Tests in `grid/tests/api/test_auth.py` (or new file `test_me.py`)

### 5.2 New endpoint — `POST /api/v1/dashboard/users/{user_id}/enable`

**Purpose**: Re-enable a previously disabled user account. The current backend only has `disable`. Without `enable`, the workflow is asymmetric and operationally incomplete.

**Authorization**: `tenant_admin` only.

**Path parameters:**
- `user_id` — UUID of the target user (must belong to the requester's tenant)

**Response shape (JSON, 200 OK):**

```json
{
  "id": "uuid-string",
  "email": "user@hospital.example",
  "is_active": true
}
```

**Error responses:**
- `403 Forbidden` if requester is not `tenant_admin`
- `404 Not Found` if user does not exist or belongs to another tenant (no enumeration)
- `409 Conflict` if user is already active

**Implementation notes:**
- Add route alongside existing `/users/{user_id}/disable` in `dashboard.py`
- Audit log entry: `user_enabled` with `actor_user_id` and `target_user_id`
- Tests in `grid/tests/api/test_rbac_distinction.py` or `test_users.py`
- Cross-tenant isolation MUST be enforced (same pattern as existing tests)

### 5.3 Phase 0 Definition of Done

- [ ] Both endpoints implemented
- [ ] Both endpoints documented in OpenAPI (auto-generated)
- [ ] At least 2 pytest tests per endpoint (happy path + negative)
- [ ] Cross-tenant isolation tests for `/users/{id}/enable` added to `test_tenant_isolation.py`
- [ ] All 94 existing tests still pass
- [ ] New tests pass (target: 98+ tests total)
- [ ] Git commit: `feat(grid): add /me and /users/{id}/enable endpoints for Sprint 7`
- [ ] No frontend work has begun yet

---

## 6. Epic 1 — Authentication & Session

**Epic ID**: `EPIC-AUTH`
**Estimated effort**: 1 day
**Story Points**: 8 SP
**Business value**: HIGH — foundation for all other Epics
**Risk level**: HIGH — security-critical

### Epic goal

Enable any user with a valid account in the GRID database to authenticate via email and password, receive a JWT-based session, and have that session managed correctly (refresh, logout, idle timeout) according to industry security standards.

### Success metrics

- 100% of authentication attempts result in either success, rejection, or session refresh
- Session timeout enforced at 30 minutes of inactivity
- Refresh token rotated on every refresh (prevents replay)
- Zero session leakage between tenants

### User Stories

- US1.1 — Login with credentials
- US1.2 — Logout (single session + all sessions)
- US1.3 — Session timeout, refresh, and "current user" context

---

### US1.1 — Login with credentials

**Story ID**: `US1.1`
**Story Points**: 3 SP

**As a** user (any role)
**I want** to log in to the console using my email and password
**So that** I can access my role-appropriate dashboard

#### Acceptance Criteria

```gherkin
AC1.1.1: Valid credentials issue tokens
  GIVEN a user account exists with is_active=true
  WHEN the user submits valid email + password
  THEN POST /api/v1/auth/login returns HTTP 200
  AND  the response contains access_token (JWT) and refresh_token (JWT)
  AND  the access_token is stored in memory (Zustand authStore)
  AND  the refresh_token is stored in an HttpOnly Secure SameSite=Strict cookie
  AND  the frontend then calls GET /api/v1/dashboard/me
  AND  the user is redirected to /dashboard

AC1.1.2: Invalid credentials are rejected with generic message
  GIVEN any combination of email and password
  WHEN the credentials do not match an active account
  THEN the API returns HTTP 401
  AND  the UI displays "Email ou mot de passe incorrect" / "Invalid email or password"
  AND  no enumeration of valid emails is possible
  AND  rate limiting applies (existing nginx config: 5 attempts per 15 min per IP)

AC1.1.3: Disabled account is rejected
  GIVEN a user account with is_active=false
  WHEN valid credentials for this account are submitted
  THEN the API returns HTTP 401 with the same generic message as AC1.1.2

AC1.1.4: Client-side form validation
  GIVEN the login form is rendered
  WHEN the user submits empty email OR empty password
  THEN inline validation errors appear in the current language (FR or EN)
  AND  no API call is made

AC1.1.5: Bilingual UI
  GIVEN the user has stored a language preference (or browser default)
  WHEN /auth/login renders
  THEN all labels, placeholders, buttons, error messages appear in FR or EN
  AND  a language switcher is visible in the header

AC1.1.6: Successful login triggers /me fetch
  GIVEN login returns 200 with tokens
  WHEN the access_token is stored
  THEN the frontend immediately calls GET /api/v1/dashboard/me
  AND  the response (id, email, full_name, roles, tenant_id, tenant_name) is stored in authStore
  AND  the routing decision uses roles[] to redirect to the role-appropriate landing
```

#### Technical notes

- Frontend route: `/auth/login`
- Backend endpoint: `POST /api/v1/auth/login` (existing)
- Subsequent fetch: `GET /api/v1/dashboard/me` (Phase 0)
- Form: React Hook Form + Zod validation
- HTTPS-only enforced (HSTS)

---

### US1.2 — Logout (single session + all sessions)

**Story ID**: `US1.2`
**Story Points**: 2 SP

**As an** authenticated user (any role)
**I want** to log out of my current session or all my sessions
**So that** my account is secured when I leave my workstation

#### Acceptance Criteria

```gherkin
AC1.2.1: Single session logout
  GIVEN an authenticated user
  WHEN the user clicks "Logout" in the user menu
  THEN the frontend calls POST /api/v1/auth/logout
  AND  the access_token is invalidated server-side (JTI blacklist)
  AND  the refresh_token cookie is cleared
  AND  the in-memory authStore is reset
  AND  the user is redirected to /auth/login
  AND  no protected route can be accessed without re-authentication

AC1.2.2: Logout from all devices
  GIVEN an authenticated user
  WHEN the user clicks "Logout all devices" in a confirmation modal
  THEN the frontend calls POST /api/v1/auth/logout with the "logout_all=true" body flag
       (NOTE: if the current backend does not support logout_all, this story
        falls back to single-session logout only and is descoped to Sprint 8)
  AND  the user is redirected to /auth/login

AC1.2.3: Idle timeout triggers automatic logout
  GIVEN an authenticated user is inactive for 30 minutes
       (no mousemove, no keypress, no scroll, no API call)
  WHEN the idle timer fires
  THEN the frontend triggers logout
  AND  a brief modal displays "Votre session a expiré" / "Your session has expired"
  AND  the user is redirected to /auth/login

AC1.2.4: Logout always succeeds client-side
  GIVEN the API is unreachable
  WHEN the user clicks "Logout"
  THEN the in-memory authStore is reset immediately
  AND  the refresh_token cookie is cleared immediately
  AND  the redirect to /auth/login is performed
  AND  the API call may fail silently (the session will be implicitly invalidated by JWT expiration)
```

#### Technical notes

- Backend endpoint: `POST /api/v1/auth/logout` (existing)
- Idle detection: client-side event listener on document (mousemove, keypress, scroll) + activity-based timer reset
- AC1.2.2 (logout all) may be descoped if backend lacks support — verified at start of sprint

---

### US1.3 — Session timeout, refresh, and "current user" context

**Story ID**: `US1.3`
**Story Points**: 3 SP

**As an** authenticated user
**I want** my session to refresh transparently during active use but expire on inactivity
**So that** my session is both usable and secure

#### Acceptance Criteria

```gherkin
AC1.3.1: Access token has finite lifetime
  GIVEN an access_token issued at time T (lifetime defined by backend, e.g., 30 min)
  WHEN T + lifetime elapses
  THEN any API call returns HTTP 401
  AND  the axios interceptor automatically attempts refresh

AC1.3.2: Refresh token rotation
  GIVEN the access_token has expired and a valid refresh_token exists in HttpOnly cookie
  WHEN the frontend calls POST /api/v1/auth/refresh
  THEN the backend returns a NEW access_token AND a NEW refresh_token
  AND  the old refresh_token is invalidated (replay-protection)
  AND  the previously queued API calls are replayed with the new access_token

AC1.3.3: Refresh failure forces re-authentication
  GIVEN the refresh_token is missing, expired, or revoked
  WHEN POST /api/v1/auth/refresh returns 401
  THEN the authStore is cleared
  AND  the user is redirected to /auth/login

AC1.3.4: /me re-fetched on app boot
  GIVEN the app loads in a browser tab where a refresh_token cookie exists
  WHEN the app initializes
  THEN the frontend calls POST /api/v1/auth/refresh to obtain a fresh access_token
  AND  THEN calls GET /api/v1/dashboard/me to populate authStore
  AND  the user is routed to their role-appropriate landing page

AC1.3.5: Concurrent refresh requests deduplicated
  GIVEN multiple in-flight API calls all fail with 401 simultaneously
  WHEN they trigger refresh
  THEN ONLY ONE refresh request is sent to the server (deduped via promise singleton)
  AND  all original calls receive the new access_token and are replayed
```

#### Technical notes

- Axios interceptor pattern: request interceptor injects Authorization header; response interceptor catches 401 and triggers refresh
- Refresh deduplication: shared promise pattern
- Endpoints: `POST /api/v1/auth/refresh` + `GET /api/v1/dashboard/me`

---

## 7. Epic 2 — Dashboard Overview

**Epic ID**: `EPIC-DASHBOARD`
**Estimated effort**: 1.5 days
**Story Points**: 13 SP
**Business value**: HIGH — first impression for all users
**Risk level**: MEDIUM

### Epic goal

After login, each role lands on a tailored dashboard surfacing the information most relevant to their job. The page must load fast and convey state clearly in under 5 seconds of viewing.

### Success metrics

- Time-to-meaningful-info < 2 seconds (FCP)
- 100% role coverage (every role has a tailored landing)
- KPI data refreshes every 30 seconds (polling) without page reload

### User Stories

- US2.1 — Role-based landing routing
- US2.2 — Executive landing (`tenant_admin`)
- US2.3 — Operational landing (`security_analyst`)
- US2.4 — Read-only landing (`read_only_auditor`)

---

### US2.1 — Role-based landing routing

**Story ID**: `US2.1`
**Story Points**: 2 SP

**As an** authenticated user
**I want** to be routed automatically to the dashboard view that matches my role
**So that** I do not have to navigate manually after login

#### Acceptance Criteria

```gherkin
AC2.1.1: Routing by role on /dashboard
  GIVEN a user lands on /dashboard
  WHEN the authStore has populated roles[]
  THEN the route renders one of three landing components based on the FIRST role:
       - "tenant_admin"        → ExecutiveDashboard.tsx
       - "security_analyst"    → OperationalDashboard.tsx
       - "read_only_auditor"   → ReadOnlyDashboard.tsx

AC2.1.2: Unknown role defaults safely
  GIVEN a user has a role not in the recognized set
  WHEN /dashboard renders
  THEN the ReadOnlyDashboard is shown (least-privilege fallback)
  AND  an error is logged to the browser console

AC2.1.3: Unauthenticated access redirects to login
  GIVEN no authenticated session exists
  WHEN any /dashboard route is accessed
  THEN the user is redirected to /auth/login
  AND  a "return to" query parameter preserves the intended destination
```

#### Technical notes

- React Router protected route wrapper: `<RequireAuth>` + `<RoleSwitch>`
- Component selection logic in `pages/dashboard/DashboardPage.tsx`

---

### US2.2 — Executive landing (`tenant_admin`)

**Story ID**: `US2.2`
**Story Points**: 5 SP

**As a** user with role `tenant_admin`
**I want** an executive summary page showing the security posture of my hospital
**So that** I can grasp the state of my organization in under 5 seconds

#### Acceptance Criteria

```gherkin
AC2.2.1: Global status card
  GIVEN the executive dashboard renders
  WHEN data from GET /api/v1/dashboard/metrics/summary is available
  THEN the page shows ONE of:
       - "Tous les systèmes normaux" (vert) / "All systems normal" — if critical_24h == 0
       - "X incidents critiques requièrent votre attention" (rouge) — if critical_24h > 0
       - "X alertes à examiner" (orange) — for non-critical alerts > 0

AC2.2.2: Four KPI cards
  GIVEN the page renders
  WHEN data is available
  THEN four cards display:
       - Total active agents (from agents count)
       - Alerts in last 24h (from metrics summary)
       - Critical alerts in last 24h (from metrics summary)
       - Days since last critical alert (computed from latest critical timestamp)

AC2.2.3: Recent alerts widget
  GIVEN there are alerts in the tenant
  WHEN the executive dashboard renders
  THEN a widget shows the 5 most recent alerts (chronological)
  AND  each row is clickable and navigates to /alerts/{id}
  AND  if zero alerts exist, an empty state message is shown

AC2.2.4: Quick actions
  GIVEN the user has role tenant_admin
  WHEN the dashboard renders
  THEN two prominent buttons are visible:
       - "Gérer les utilisateurs" / "Manage users" → /users
       - "Consulter les journaux d'audit" / "View audit logs" → /audit

AC2.2.5: Responsive
  GIVEN viewport widths 320px, 768px, 1024px, 1440px
  WHEN the page renders
  THEN cards reflow gracefully (no horizontal scroll)
  AND  touch targets ≥ 44×44 px on mobile

AC2.2.6: Auto-refresh
  GIVEN the dashboard is open
  WHEN 30 seconds elapse
  THEN TanStack Query refetches /metrics/summary and /alerts (top 5)
  AND  the UI updates without page reload
```

#### Technical notes

- Route: `/dashboard` (component swap by role)
- Data: `GET /api/v1/dashboard/metrics/summary`, `GET /api/v1/dashboard/alerts?page_size=5`
- TanStack Query `refetchInterval: 30_000`

---

### US2.3 — Operational landing (`security_analyst`)

**Story ID**: `US2.3`
**Story Points**: 4 SP

**As a** user with role `security_analyst`
**I want** an operational console showing live alert feed and agent health
**So that** I can start triaging immediately when I begin my shift

#### Acceptance Criteria

```gherkin
AC2.3.1: Agent health summary
  GIVEN the operational dashboard renders
  WHEN data is available
  THEN a top panel displays counts:
       - Agents active (green)
       - Agents offline (red)
       - Agents with warnings or stale heartbeat (orange)

AC2.3.2: Recent alerts feed
  GIVEN alerts exist
  WHEN the dashboard renders
  THEN a scrollable feed displays the latest 20 alerts sorted by severity then recency
  AND  each row shows: timestamp, severity badge, agent name, summary
  AND  rows are clickable to /alerts/{id}
  AND  the feed auto-refreshes every 15 seconds

AC2.3.3: Agent health table preview
  GIVEN agents exist
  WHEN the dashboard renders
  THEN a compact table shows the 10 most recently-heartbeating agents
  AND  columns: Hostname, OS, Last heartbeat (relative), Status badge
  AND  a "Voir tout" / "View all" link navigates to /agents

AC2.3.4: Quick filters on alerts feed
  GIVEN the alerts feed is rendered
  WHEN the user clicks a filter chip
  THEN the feed filters by: All / Critical only / Last hour
```

#### Technical notes

- Endpoints: `/dashboard/agents`, `/dashboard/alerts`, `/dashboard/metrics/summary`
- Polling: 15s for alerts feed (faster than executive view)

---

### US2.4 — Read-only landing (`read_only_auditor`)

**Story ID**: `US2.4`
**Story Points**: 2 SP

**As a** user with role `read_only_auditor`
**I want** a landing page focused on audit observability
**So that** I can quickly access the logs and reports I need for compliance review

#### Acceptance Criteria

```gherkin
AC2.4.1: Audit-focused content
  GIVEN the read-only dashboard renders
  WHEN data is available
  THEN the page shows:
       - Number of alerts in last 24h (read-only metric)
       - Number of audit log entries in last 7 days
       - Two prominent buttons: "Consulter les journaux d'audit" and "Voir les alertes"

AC2.4.2: No write actions visible
  GIVEN the role is read_only_auditor
  WHEN the page renders
  THEN no buttons for "Manage users", "Issue commands", "Update status" are visible
  AND  navigating to /users (via URL manipulation) shows a 403 message

AC2.4.3: Status changes hidden in alert lists
  GIVEN this role lists alerts via /alerts
  WHEN rows render
  THEN the "Acknowledge" and "Close" action buttons are NOT visible
  AND  only "View details" is available
```

#### Technical notes

- Component: `pages/dashboard/ReadOnlyDashboard.tsx`
- RBAC guard: `<RequirePermission permission="users:read">` blocks /users for this role

---

## 8. Epic 3 — Alerts Management

**Epic ID**: `EPIC-ALERTS`
**Estimated effort**: 2 days
**Story Points**: 18 SP
**Business value**: CRITICAL — core operational feature
**Risk level**: MEDIUM

### Epic goal

Allow `tenant_admin` and `security_analyst` to triage, investigate, acknowledge, and close alerts, while `read_only_auditor` can view (read-only) for compliance review.

### Endpoints consumed (real, from audit)

```
GET    /api/v1/dashboard/alerts                  — list with filters
GET    /api/v1/dashboard/alerts/{alert_id}       — alert detail
POST   /api/v1/dashboard/alerts/{alert_id}/status — update status (admin + analyst)
```

### User Stories

- US3.1 — Alerts list with filters
- US3.2 — Alert detail view
- US3.3 — Acknowledge alert (status transition)
- US3.4 — Close alert with resolution notes

---

### US3.1 — Alerts list with filters

**Story ID**: `US3.1`
**Story Points**: 5 SP

**As an** authenticated user
**I want** to see a paginated, filterable list of all alerts in my tenant
**So that** I can quickly find the alert I need to investigate

#### Acceptance Criteria

```gherkin
AC3.1.1: Paginated list
  GIVEN alerts exist in the user's tenant
  WHEN the user navigates to /alerts
  THEN a paginated table displays alerts (50 per page)
  AND  columns: Alert ID, Created at, Severity, Agent (hostname), Status, Module
  AND  sorting on Created at, Severity, Status (server-side)

AC3.1.2: Filters
  GIVEN the alerts list is rendered
  WHEN the user applies any of:
       - Date range (from, to)
       - Severity (critical, high, medium, low)
       - Status (new, acknowledged, resolved, closed, false_positive)
       - Agent (hostname text search)
       - Module (SENTINEL, ENTROPY, GENEALOGY, USB_GUARD, EXFIL_WATCH, IRONCLAD)
  THEN the table updates with server-side filtered results
  AND  the URL query string reflects the filters (shareable links)

AC3.1.3: Empty state
  GIVEN no alerts match the current filters
  WHEN the list renders
  THEN an empty state component displays a localized message

AC3.1.4: Multi-tenant isolation enforced
  GIVEN a user belongs to tenant A
  WHEN URL manipulation attempts to view tenant B's alert
  THEN the backend returns 404 (not 403, to prevent enumeration)
  AND  the frontend displays an "Alerte non trouvée" / "Alert not found" message

AC3.1.5: Role-based action visibility
  GIVEN role read_only_auditor
  WHEN the list renders
  THEN no row actions are shown (only "View details")
  GIVEN role tenant_admin OR security_analyst
  WHEN the list renders
  THEN row actions "Acknowledge" / "Close" are visible only for eligible statuses
```

#### Technical notes

- Route: `/alerts`
- API: `GET /api/v1/dashboard/alerts?page=1&page_size=50&severity=critical&status=new`
- Pagination metadata returned by backend (verify shape against OpenAPI on Day 1)

---

### US3.2 — Alert detail view

**Story ID**: `US3.2`
**Story Points**: 5 SP

**As an** authenticated user
**I want** to view full details of a specific alert
**So that** I have all the context needed to act

#### Acceptance Criteria

```gherkin
AC3.2.1: Header with metadata
  GIVEN an alert exists with ID X
  WHEN the user navigates to /alerts/X
  THEN the header displays:
       - Alert ID (copyable to clipboard)
       - Severity badge
       - Status badge
       - Created at + relative age
       - Agent (link to /agents/{agent_id})
       - Module (which detection module fired)

AC3.2.2: Detection narrative
  GIVEN the alert detail renders
  WHEN data is available
  THEN the body shows:
       - Detection summary (human-readable)
       - Confidence score (if available)
       - Raw payload as collapsible JSON

AC3.2.3: Affected artifacts (if any)
  GIVEN the alert has alert_artifacts entries
  WHEN the detail renders
  THEN a list shows associated artifacts (file paths, process names, network destinations)
       respecting whatever the backend exposes via /alerts/{id}

AC3.2.4: Status change history
  GIVEN the alert has alert_status_change entries
  WHEN the detail renders
  THEN a timeline shows each status change with: timestamp, from→to status, actor user, optional note

AC3.2.5: Role-gated action buttons
  GIVEN the user role and alert status
  WHEN the page renders
  THEN buttons are conditionally visible:
       - tenant_admin OR security_analyst + status="new": [Acknowledge]
       - tenant_admin OR security_analyst + status="acknowledged": [Close] [Mark as false positive]
       - read_only_auditor: no action buttons

AC3.2.6: 404 for cross-tenant access
  GIVEN a user accesses /alerts/{id} where id belongs to another tenant
  WHEN the API call is made
  THEN HTTP 404 is returned and a localized "not found" message is displayed
```

#### Technical notes

- Route: `/alerts/:id`
- API: `GET /api/v1/dashboard/alerts/{id}`

---

### US3.3 — Acknowledge alert

**Story ID**: `US3.3`
**Story Points**: 4 SP

**As a** user with role `tenant_admin` OR `security_analyst`
**I want** to acknowledge a new alert
**So that** I claim responsibility for handling it

#### Acceptance Criteria

```gherkin
AC3.3.1: Acknowledge new alert
  GIVEN an alert with status="new"
  AND   role tenant_admin OR security_analyst
  WHEN the user clicks "Acknowledge"
  THEN a confirmation modal asks for an optional note (0-500 chars)
  AND  upon confirmation, POST /api/v1/dashboard/alerts/{id}/status is called with
       body { status: "acknowledged", note: "..." }
  AND  the status changes to "acknowledged"
  AND  the timeline records the change with the actor's user_id
  AND  an audit log entry is created (backend-managed)

AC3.3.2: Cannot acknowledge non-new alert
  GIVEN an alert with status != "new"
  WHEN the page renders
  THEN the "Acknowledge" button is hidden
  AND  direct POST returns 409 from backend

AC3.3.3: read_only_auditor cannot acknowledge
  GIVEN role read_only_auditor
  WHEN viewing /alerts/{id}
  THEN no "Acknowledge" button is visible
  AND  direct API call returns 403

AC3.3.4: Optimistic UI update
  GIVEN the user clicks "Acknowledge"
  WHEN the API call is in flight
  THEN the UI shows the new status immediately
  AND  if the API fails, the UI reverts and shows an error toast in the user's language
```

#### Technical notes

- API: `POST /api/v1/dashboard/alerts/{id}/status` body `{ status: "acknowledged", note?: string }`
- Use TanStack Query mutations with `onMutate`/`onError`/`onSuccess` for optimistic updates

---

### US3.4 — Close alert with resolution notes

**Story ID**: `US3.4`
**Story Points**: 4 SP

**As a** user with role `tenant_admin` OR `security_analyst`
**I want** to close an acknowledged alert with resolution notes
**So that** the alert lifecycle is complete and auditable

#### Acceptance Criteria

```gherkin
AC3.4.1: Close acknowledged alert
  GIVEN an alert with status="acknowledged"
  AND   role tenant_admin OR security_analyst
  WHEN the user clicks "Close"
  THEN a modal prompts for:
       - Resolution category (dropdown, required):
         "False positive" / "True positive - contained" /
         "True positive - escalated" / "Inconclusive"
       - Resolution notes (text, required, 20-2000 chars)
  AND  upon submission, POST /api/v1/dashboard/alerts/{id}/status is called with
       body { status: "closed", note: "<category>: <notes>" }
  AND  the status changes to "closed"
  AND  the timeline records the closure

AC3.4.2: Cannot close non-acknowledged alert
  GIVEN an alert with status="new" or "closed"
  WHEN the page renders
  THEN the "Close" button is hidden or disabled
  AND  direct API call returns 409

AC3.4.3: Closed alerts are read-only
  GIVEN an alert with status="closed"
  WHEN any role views it
  THEN no modification actions are available
  AND  the resolution notes are displayed prominently in the timeline
```

#### Technical notes

- Same endpoint as US3.3, different status value
- Status enum (from backend): `new`, `acknowledged`, `closed`, `false_positive` (verify against `AlertStatus` enum)

---

## 9. Epic 4 — Agents Management

**Epic ID**: `EPIC-AGENTS`
**Estimated effort**: 1.5 days
**Story Points**: 16 SP
**Business value**: HIGH — daily operations
**Risk level**: MEDIUM

### Epic goal

Allow users to view the agent inventory of their tenant and (for admin + analyst) issue commands such as endpoint isolation.

### Endpoints consumed (real, from audit)

```
GET    /api/v1/dashboard/agents                   — list agents
GET    /api/v1/dashboard/agents/{agent_id}        — agent detail
POST   /api/v1/dashboard/commands                 — issue command (admin only per audit)
```

### User Stories

- US4.1 — Agents inventory list
- US4.2 — Agent detail view
- US4.3 — Issue command to agent (isolate)
- US4.4 — Heartbeat & status indicators

---

### US4.1 — Agents inventory list

**Story ID**: `US4.1`
**Story Points**: 4 SP

**As an** authenticated user
**I want** to see a paginated, searchable list of all agents in my tenant
**So that** I have visibility on the protected fleet

#### Acceptance Criteria

```gherkin
AC4.1.1: Paginated list
  GIVEN agents exist in the user's tenant
  WHEN the user navigates to /agents
  THEN a paginated table displays agents (50 per page)
  AND  columns: Hostname, OS, Agent version, Last heartbeat, Status, Threat intel version

AC4.1.2: Filters
  GIVEN the agents list is rendered
  WHEN the user applies filters
  THEN the table updates:
       - Status: All / Active / Disabled / Decommissioned
       - OS: All / Windows 7 / Windows 10 / Windows 11
       - Heartbeat age: All / Recent (< 5 min) / Stale (5-60 min) / Offline (> 60 min)

AC4.1.3: Color-coded status
  GIVEN the table renders
  WHEN status data is present
  THEN status cells are color-coded:
       - Green: active + recent heartbeat
       - Yellow: active + stale heartbeat
       - Red: offline (heartbeat > 60 min) or status != "active"

AC4.1.4: Row actions by role
  GIVEN the user role
  WHEN the list renders
  THEN actions are visible per row:
       - tenant_admin + security_analyst: [View] [Send command]
       - read_only_auditor: [View] only
```

#### Technical notes

- Route: `/agents`
- API: `GET /api/v1/dashboard/agents`

---

### US4.2 — Agent detail view

**Story ID**: `US4.2`
**Story Points**: 4 SP

**As an** authenticated user
**I want** to view full details of a specific agent
**So that** I have full context for any action

#### Acceptance Criteria

```gherkin
AC4.2.1: Header with metadata
  GIVEN an agent exists with ID X
  WHEN the user navigates to /agents/X
  THEN the header displays:
       - Hostname
       - OS + version
       - Agent version
       - Last heartbeat (with relative age)
       - Status
       - Enrollment date

AC4.2.2: Tabs
  GIVEN the detail page renders
  WHEN data is available
  THEN tabs are accessible:
       - Overview (KPIs, recent alerts on this agent, configuration excerpt)
       - Alerts (filtered list of alerts for this agent only)
       - Heartbeats (timeline of last 50 heartbeats)
       - Configuration (read-only display of agent_configurations entry)

AC4.2.3: Linked alerts
  GIVEN the agent has alerts
  WHEN the Alerts tab renders
  THEN alerts list is paginated and filterable
  AND  rows link to /alerts/{id}
```

#### Technical notes

- Route: `/agents/:id`
- API: `GET /api/v1/dashboard/agents/{id}`
- Alerts tab: `GET /api/v1/dashboard/alerts?agent_id={id}`

---

### US4.3 — Issue command to agent (isolate)

**Story ID**: `US4.3`
**Story Points**: 5 SP

**As a** user with role `tenant_admin`
**I want** to issue an isolation command to a compromised agent
**So that** I prevent ransomware lateral movement

#### Acceptance Criteria

```gherkin
AC4.3.1: Issue isolate command — admin only
  GIVEN an agent with status="active"
  AND   role tenant_admin
  WHEN the user clicks "Isolate endpoint"
  THEN a confirmation modal displays:
       - Warning text (red)
       - Reason field (required, 20-500 chars)
       - "I understand this will disconnect the endpoint" checkbox
  AND  upon confirmation, POST /api/v1/dashboard/commands is called with
       body { agent_id: "X", command_type: "isolate", payload: { reason: "..." } }
  AND  the API returns 202 (Accepted)
  AND  a toast confirms "Command queued"
  AND  the command will be picked up by the agent on next heartbeat

AC4.3.2: Other roles cannot issue commands
  GIVEN role security_analyst OR read_only_auditor
  WHEN viewing /agents/{id}
  THEN no "Isolate" button is visible
  AND  direct POST returns 403

AC4.3.3: Restore command
  GIVEN an agent currently isolated (status reflects this)
  AND   role tenant_admin
  WHEN the user clicks "Restore network access"
  THEN a similar confirmation flow is presented (reason required)
  AND  POST /api/v1/dashboard/commands is called with
       body { agent_id: "X", command_type: "restore", payload: { reason: "..." } }

AC4.3.4: Command status visibility
  GIVEN a command was issued
  WHEN the user views the agent detail
  THEN the most recent command and its status (pending, executed, failed) is displayed
       (consuming command_queue + command_response data exposed by the backend)
```

#### Technical notes

- API: `POST /api/v1/dashboard/commands`
- Command types depend on backend `CommandStatus` enum — verify on Day 1
- Restore is implementation-defined; if backend lacks a restore command, US4.3.3 is descoped to Sprint 8

---

### US4.4 — Heartbeat & status indicators

**Story ID**: `US4.4`
**Story Points**: 3 SP

**As an** authenticated user
**I want** to see real-time-ish heartbeat indicators on the agent inventory
**So that** I can spot agents that have gone silent

#### Acceptance Criteria

```gherkin
AC4.4.1: Heartbeat freshness chip
  GIVEN the agents list is rendered
  WHEN heartbeat timestamps are present
  THEN each row shows a chip with relative age:
       - "Il y a 2 min" / "2 minutes ago" — green
       - "Il y a 15 min" / "15 minutes ago" — yellow if > 5 min
       - "Il y a 2 h" / "2 hours ago" — red if > 60 min

AC4.4.2: Auto-refresh
  GIVEN the agents list is open
  WHEN 30 seconds elapse
  THEN the list refetches and heartbeat chips update

AC4.4.3: Stale agent warning banner
  GIVEN ≥ 1 agent has heartbeat > 60 min
  WHEN the executive or operational dashboard renders
  THEN a non-blocking yellow banner displays:
       "X agents n'ont pas envoyé de heartbeat depuis plus d'1 heure"
```

#### Technical notes

- Reuse `GET /api/v1/dashboard/agents`
- Polling interval: 30 seconds

---

## 10. Epic 5 — Users Management

**Epic ID**: `EPIC-USERS`
**Estimated effort**: 1.25 days
**Story Points**: 13 SP
**Business value**: MEDIUM — tenant administration
**Risk level**: HIGH (privilege management)

### Epic goal

Allow `tenant_admin` to create, configure, disable, and re-enable users within their tenant, with role assignment.

### Endpoints consumed

```
GET    /api/v1/dashboard/users                          — list users (admin only)
POST   /api/v1/dashboard/users                          — create user (admin only)
POST   /api/v1/dashboard/users/{user_id}/disable        — disable user (admin only)
POST   /api/v1/dashboard/users/{user_id}/enable         — enable user (admin only, Phase 0)
PUT    /api/v1/dashboard/users/{user_id}/roles          — update roles (admin only)
GET    /api/v1/dashboard/me                             — current user (Phase 0)
```

### User Stories

- US5.1 — Users inventory list
- US5.2 — Create user
- US5.3 — Disable / enable user
- US5.4 — Update user roles

---

### US5.1 — Users inventory list

**Story ID**: `US5.1`
**Story Points**: 3 SP

**As a** user with role `tenant_admin`
**I want** to see all users in my tenant
**So that** I can manage them

#### Acceptance Criteria

```gherkin
AC5.1.1: Paginated list — admin only
  GIVEN role tenant_admin
  WHEN navigating to /users
  THEN a paginated table displays users in the tenant
  AND  columns: Full name, Email, Roles, Status (active/disabled), Last login, Created at
  AND  sorting on Last login and Created at

AC5.1.2: Non-admin access denied
  GIVEN role security_analyst OR read_only_auditor
  WHEN navigating to /users
  THEN the user is redirected to /dashboard
  AND  a toast "Accès refusé" / "Access denied" is shown
  AND  direct API call returns 403

AC5.1.3: Current user cannot disable themselves from this list
  GIVEN the admin views their own row in the users list
  WHEN they attempt to disable themselves
  THEN the "Disable" action is disabled with a tooltip
       "Vous ne pouvez pas désactiver votre propre compte" /
       "You cannot disable your own account"
```

#### Technical notes

- Route: `/users`
- API: `GET /api/v1/dashboard/users`

---

### US5.2 — Create user

**Story ID**: `US5.2`
**Story Points**: 4 SP

**As a** user with role `tenant_admin`
**I want** to create a new user account in my tenant
**So that** my colleagues can access the system

#### Acceptance Criteria

```gherkin
AC5.2.1: Create form
  GIVEN role tenant_admin navigates to /users
  WHEN they click "Ajouter un utilisateur" / "Add user"
  THEN a form opens with fields:
       - Full name (required)
       - Email (required, format-validated, unique within tenant)
       - Role (multi-select from: tenant_admin, security_analyst, read_only_auditor)
       - Initial password (required, policy: 12+ chars, mixed case, digit, symbol)
       - Confirm password (must match)

AC5.2.2: Submit creates user
  GIVEN the form is submitted with valid data
  WHEN POST /api/v1/dashboard/users is called
  THEN the user is created
  AND  is_active is set to true by default
  AND  the new user appears in the users list
  AND  an audit log entry is created (backend-managed)

AC5.2.3: Email uniqueness within tenant
  GIVEN the email exists in the tenant
  WHEN the form is submitted
  THEN the API returns 409
  AND  the form displays an inline error in the user's language

AC5.2.4: Cannot create super_admin
  GIVEN the create form is open
  WHEN the role dropdown renders
  THEN only three options appear: tenant_admin, security_analyst, read_only_auditor
  AND  the backend would reject any other role value anyway

AC5.2.5: Password policy enforced client-side AND server-side
  GIVEN the user types a new password
  WHEN the input changes
  THEN a strength indicator (red/orange/green) updates in real time
  AND  submission is blocked until policy is satisfied
  AND  the backend remains authoritative for final validation
```

#### Technical notes

- API: `POST /api/v1/dashboard/users` (existing)
- Verify the request payload shape against the actual schema (likely `UserCreateRequest` in `api/v1/schemas/auth.py` or `tenant_user.py`)

---

### US5.3 — Disable / enable user

**Story ID**: `US5.3`
**Story Points**: 3 SP

**As a** user with role `tenant_admin`
**I want** to disable a user account (and re-enable later if needed)
**So that** I can revoke or restore access without deleting history

#### Acceptance Criteria

```gherkin
AC5.3.1: Disable user
  GIVEN an active user in the tenant
  AND   role tenant_admin
  AND   target is NOT the current admin
  AND   target is NOT the last active tenant_admin
  WHEN the admin clicks "Désactiver" / "Disable"
  THEN a confirmation modal requires justification (required, 20-500 chars)
  AND  upon confirmation, POST /api/v1/dashboard/users/{user_id}/disable is called
  AND  the user's is_active flag is set to false
  AND  the user's active sessions are invalidated (backend behavior)
  AND  audit log entry "user_disabled" is created

AC5.3.2: Cannot self-disable
  GIVEN the admin views their own row
  WHEN they attempt to disable themselves
  THEN the action is blocked (button disabled with tooltip)
  AND  direct API call returns 403

AC5.3.3: Last admin protection
  GIVEN exactly one user has role tenant_admin
  WHEN attempting to disable that user or remove the role
  THEN the API returns 422 with message
       "Impossible de supprimer le dernier administrateur" /
       "Cannot remove the last administrator"
  AND  the UI shows this message as an error

AC5.3.4: Enable disabled user (Phase 0 endpoint)
  GIVEN a user with is_active=false
  AND   role tenant_admin
  WHEN the admin clicks "Réactiver" / "Enable"
  THEN a confirmation modal asks for justification
  AND  upon confirmation, POST /api/v1/dashboard/users/{user_id}/enable is called
  AND  the user's is_active flag is set to true
  AND  audit log entry "user_enabled" is created
```

#### Technical notes

- Endpoints: `POST /api/v1/dashboard/users/{id}/disable` (existing), `POST /api/v1/dashboard/users/{id}/enable` (Phase 0)
- Last admin protection enforced by backend per audit findings

---

### US5.4 — Update user roles

**Story ID**: `US5.4`
**Story Points**: 3 SP

**As a** user with role `tenant_admin`
**I want** to change the roles assigned to a user
**So that** I can promote, demote, or restructure team responsibilities

#### Acceptance Criteria

```gherkin
AC5.4.1: Open role editor
  GIVEN a user in the tenant
  AND   role tenant_admin
  AND   target is NOT the current admin (self-demotion prevented per audit)
  WHEN the admin clicks "Modifier les rôles" / "Change roles" on a row
  THEN a modal opens with checkboxes:
       - tenant_admin
       - security_analyst
       - read_only_auditor

AC5.4.2: Save roles
  GIVEN the modal is open
  AND   at least one role is selected
  WHEN the admin clicks "Enregistrer"
  THEN PUT /api/v1/dashboard/users/{user_id}/roles is called with body { role_codes: [...] }
  AND  the user's role assignments are replaced server-side
  AND  audit log entry "user_role_changed" is created

AC5.4.3: Cannot self-modify roles (per audit AC test_admin_cannot_self_demote)
  GIVEN the admin views their own row
  WHEN they attempt to open the role editor for themselves
  THEN the action is disabled with tooltip
  AND  direct API call returns 403

AC5.4.4: Last admin protection on role change
  GIVEN exactly one tenant_admin exists
  WHEN attempting to remove tenant_admin from that user
  THEN the API returns 422
  AND  the UI shows a localized error message
```

#### Technical notes

- API: `PUT /api/v1/dashboard/users/{user_id}/roles`
- Roles are passed as an array of role codes

---

## 11. Epic 6 — Audit Logs Viewer

**Epic ID**: `EPIC-AUDIT`
**Estimated effort**: 0.75 day
**Story Points**: 8 SP
**Business value**: HIGH — compliance + forensics
**Risk level**: LOW

### Epic goal

Allow `tenant_admin` and `read_only_auditor` to search, filter, and export the audit logs.

### Endpoints consumed

```
GET /api/v1/dashboard/audit-logs    — search audit logs (admin + auditor per audit)
```

### User Stories

- US6.1 — Search audit logs
- US6.2 — Filter audit logs
- US6.3 — Export audit logs to CSV

---

### US6.1 — Search audit logs

**Story ID**: `US6.1`
**Story Points**: 3 SP

**As a** user with role `tenant_admin` OR `read_only_auditor`
**I want** to search audit logs by free text
**So that** I can quickly find specific events

#### Acceptance Criteria

```gherkin
AC6.1.1: Search box
  GIVEN role tenant_admin OR read_only_auditor navigates to /audit
  WHEN they type in the search input
  THEN audit entries are filtered server-side (debounced 300 ms)
  AND  search matches action name, target resource, actor email (whatever the backend supports)
  AND  results are paginated (50 per page)

AC6.1.2: Security_analyst denied (per audit ACs)
  GIVEN role security_analyst
  WHEN navigating to /audit
  THEN they are redirected to /dashboard
  AND  the API call returns 403
```

#### Technical notes

- Route: `/audit`
- API: `GET /api/v1/dashboard/audit-logs?search=...&page=1&page_size=50`

---

### US6.2 — Filter audit logs

**Story ID**: `US6.2`
**Story Points**: 3 SP

**As a** user with role `tenant_admin` OR `read_only_auditor`
**I want** to filter audit logs by date, action type, and actor
**So that** I can narrow down to relevant events

#### Acceptance Criteria

```gherkin
AC6.2.1: Date range
  GIVEN the audit list is rendered
  WHEN the user picks a date range
  THEN entries outside the range are excluded

AC6.2.2: Action type
  GIVEN action types exist in the data
  WHEN the user selects one or more types from a multi-select
       (login, logout, alert_status_change, user_created, user_disabled, user_enabled,
        user_role_changed, command_issued, etc. — depends on what backend records)
  THEN entries are filtered accordingly

AC6.2.3: Actor filter
  GIVEN the audit list is rendered
  WHEN the user selects an actor from a dropdown of users in the tenant
  THEN entries are filtered to actions performed by that actor

AC6.2.4: Combined filters
  GIVEN multiple filters are applied
  WHEN the user updates any
  THEN the table updates and the URL query string reflects the state
```

#### Technical notes

- Same endpoint as US6.1 with additional query parameters
- Available filters depend on what `GET /audit-logs` exposes — verify on Day 1

---

### US6.3 — Export audit logs to CSV

**Story ID**: `US6.3`
**Story Points**: 2 SP

**As a** user with role `tenant_admin` OR `read_only_auditor`
**I want** to export the current filtered view of audit logs to CSV
**So that** I have an offline copy for compliance review

#### Acceptance Criteria

```gherkin
AC6.3.1: Export button
  GIVEN the audit list is rendered with filters applied
  WHEN the user clicks "Exporter en CSV" / "Export CSV"
  THEN the frontend either:
       (a) calls a backend endpoint that streams a CSV file, OR
       (b) reads the current paginated dataset into the client and generates CSV in-browser
       — depending on what the backend offers; default to (b) if no export endpoint exists

AC6.3.2: CSV format
  GIVEN the CSV is generated
  WHEN the user opens it
  THEN columns are: timestamp, action, actor_user_email, target_resource, target_id, ip_address, payload
  AND  encoding is UTF-8 with BOM (for Excel compatibility with French accents)

AC6.3.3: Export action self-logged
  GIVEN an export is performed
  WHEN the operation completes
  THEN the backend logs an "audit_exported" entry (server-side, on the read endpoint's behalf,
       or manually triggered by an additional POST if needed in Sprint 8)
```

#### Technical notes

- AC6.3.3 may be deferred to Sprint 8 if the backend doesn't auto-log audit reads — verify on Day 1
- Client-side CSV generation: use `papaparse` or simple manual stringification

---

## 12. Technical Stack & Architecture

### 12.1 Frontend Stack

| Layer | Technology | Version |
|---|---|---|
| Framework | React | 18.3+ |
| Language | TypeScript | 5.5+ (strict mode) |
| Build tool | Vite | 5.4+ |
| Styling | TailwindCSS | 3.4+ |
| UI primitives | shadcn/ui | latest |
| Server state | TanStack Query | 5.x |
| Client state | Zustand | 4.x |
| Routing | React Router | 6.26+ |
| Forms | React Hook Form + Zod | latest |
| HTTP | Axios | 1.x |
| i18n | react-i18next | 15.x |
| Charts | Recharts | 2.x |
| Dates | date-fns | 4.x |
| Icons | Lucide React | latest |
| Unit tests | Vitest | latest |
| Component tests | Testing Library | latest |
| E2E tests | Playwright | latest |

### 12.2 Project Structure

```
RansomGuard-CM/
├── agent/                          (SACRED — Sprint 1-5, do not touch)
├── grid/                           (SACRED — Sprint 6, Phase 0 modifications only)
├── deployment/grid/                (SACRED — Sprint 6, do not touch)
├── docs/                           (SACRED — Sprint 1-6 documentation)
└── dashboard/                      (Sprint 7 deliverable)
    ├── public/
    │   ├── locales/
    │   │   ├── fr.json
    │   │   └── en.json
    │   └── favicon.svg
    ├── src/
    │   ├── main.tsx
    │   ├── App.tsx
    │   ├── api/
    │   │   ├── client.ts                   (Axios instance + interceptors)
    │   │   ├── auth.ts
    │   │   ├── alerts.ts
    │   │   ├── agents.ts
    │   │   ├── users.ts
    │   │   ├── audit.ts
    │   │   └── metrics.ts
    │   ├── components/
    │   │   ├── ui/                          (shadcn/ui primitives)
    │   │   ├── layout/                      (AppShell, Sidebar, Header)
    │   │   ├── charts/
    │   │   ├── tables/
    │   │   ├── forms/
    │   │   └── auth/
    │   │       ├── RequireAuth.tsx
    │   │       ├── RequireRole.tsx
    │   │       └── RoleSwitch.tsx
    │   ├── pages/
    │   │   ├── auth/
    │   │   │   └── LoginPage.tsx
    │   │   ├── dashboard/
    │   │   │   ├── DashboardPage.tsx
    │   │   │   ├── ExecutiveDashboard.tsx
    │   │   │   ├── OperationalDashboard.tsx
    │   │   │   └── ReadOnlyDashboard.tsx
    │   │   ├── alerts/
    │   │   │   ├── AlertsListPage.tsx
    │   │   │   └── AlertDetailPage.tsx
    │   │   ├── agents/
    │   │   │   ├── AgentsListPage.tsx
    │   │   │   └── AgentDetailPage.tsx
    │   │   ├── users/
    │   │   │   └── UsersListPage.tsx
    │   │   └── audit/
    │   │       └── AuditLogsPage.tsx
    │   ├── hooks/
    │   │   ├── useAuth.ts
    │   │   ├── useRole.ts
    │   │   ├── usePermission.ts
    │   │   └── useIdleTimer.ts
    │   ├── stores/
    │   │   ├── authStore.ts                 (Zustand)
    │   │   └── uiStore.ts
    │   ├── types/
    │   │   ├── api.ts                       (Generated or hand-written)
    │   │   ├── auth.ts
    │   │   ├── alert.ts
    │   │   ├── agent.ts
    │   │   └── user.ts
    │   ├── lib/
    │   │   ├── i18n.ts
    │   │   ├── validators.ts                (Zod schemas)
    │   │   ├── format.ts
    │   │   ├── permissions.ts
    │   │   └── constants.ts                 (ROLE codes, ACTION types)
    │   └── styles/
    │       └── globals.css
    ├── tests/
    │   ├── unit/
    │   ├── integration/
    │   └── e2e/
    │       ├── auth.spec.ts
    │       ├── alerts.spec.ts
    │       ├── agents.spec.ts
    │       ├── users.spec.ts
    │       └── rbac.spec.ts
    ├── package.json
    ├── tsconfig.json
    ├── vite.config.ts
    ├── tailwind.config.ts
    ├── playwright.config.ts
    └── README.md
```

### 12.3 RBAC Implementation Pattern

```typescript
// dashboard/src/lib/constants.ts

export const ROLES = {
  TENANT_ADMIN: 'tenant_admin',
  SECURITY_ANALYST: 'security_analyst',
  READ_ONLY_AUDITOR: 'read_only_auditor',
} as const;

export type Role = typeof ROLES[keyof typeof ROLES];

// dashboard/src/lib/permissions.ts

export const PERMISSIONS = {
  // Alerts
  'alerts:read': [ROLES.TENANT_ADMIN, ROLES.SECURITY_ANALYST, ROLES.READ_ONLY_AUDITOR],
  'alerts:acknowledge': [ROLES.TENANT_ADMIN, ROLES.SECURITY_ANALYST],
  'alerts:close': [ROLES.TENANT_ADMIN, ROLES.SECURITY_ANALYST],

  // Agents
  'agents:read': [ROLES.TENANT_ADMIN, ROLES.SECURITY_ANALYST, ROLES.READ_ONLY_AUDITOR],
  'agents:command': [ROLES.TENANT_ADMIN],

  // Users
  'users:read': [ROLES.TENANT_ADMIN],
  'users:create': [ROLES.TENANT_ADMIN],
  'users:update': [ROLES.TENANT_ADMIN],

  // Audit
  'audit:read': [ROLES.TENANT_ADMIN, ROLES.READ_ONLY_AUDITOR],
} as const;

export function hasPermission(
  userRoles: readonly Role[],
  permission: keyof typeof PERMISSIONS
): boolean {
  const allowedRoles = PERMISSIONS[permission];
  return userRoles.some(role => (allowedRoles as readonly Role[]).includes(role));
}
```

### 12.4 Route Protection Pattern

```typescript
// dashboard/src/components/auth/RequirePermission.tsx

import { Navigate } from 'react-router-dom';
import { useAuth } from '@/hooks/useAuth';
import { hasPermission, type PERMISSIONS } from '@/lib/permissions';

interface Props {
  permission: keyof typeof PERMISSIONS;
  children: React.ReactNode;
}

export function RequirePermission({ permission, children }: Props) {
  const { user, isLoading } = useAuth();
  if (isLoading) return <FullPageSpinner />;
  if (!user) return <Navigate to="/auth/login" replace />;
  if (!hasPermission(user.roles, permission)) {
    return <Navigate to="/dashboard" replace />;
  }
  return <>{children}</>;
}
```

---

## 13. API Contract Mapping

Each User Story maps to specific real backend endpoints. This table is authoritative.

| Story | Method | Path | Notes |
|---|---|---|---|
| US1.1 | POST | `/api/v1/auth/login` | existing |
| US1.1 | GET | `/api/v1/dashboard/me` | Phase 0 — new |
| US1.2 | POST | `/api/v1/auth/logout` | existing |
| US1.3 | POST | `/api/v1/auth/refresh` | existing |
| US2.2 | GET | `/api/v1/dashboard/metrics/summary` | existing |
| US2.2 | GET | `/api/v1/dashboard/alerts?page_size=5` | existing |
| US2.3 | GET | `/api/v1/dashboard/alerts` | existing |
| US2.3 | GET | `/api/v1/dashboard/agents` | existing |
| US2.4 | GET | `/api/v1/dashboard/audit-logs?page_size=N` | existing |
| US3.1 | GET | `/api/v1/dashboard/alerts` | existing |
| US3.2 | GET | `/api/v1/dashboard/alerts/{id}` | existing |
| US3.3 | POST | `/api/v1/dashboard/alerts/{id}/status` | existing |
| US3.4 | POST | `/api/v1/dashboard/alerts/{id}/status` | existing (status="closed") |
| US4.1 | GET | `/api/v1/dashboard/agents` | existing |
| US4.2 | GET | `/api/v1/dashboard/agents/{id}` | existing |
| US4.3 | POST | `/api/v1/dashboard/commands` | existing |
| US4.4 | GET | `/api/v1/dashboard/agents` (polled) | existing |
| US5.1 | GET | `/api/v1/dashboard/users` | existing |
| US5.2 | POST | `/api/v1/dashboard/users` | existing |
| US5.3a | POST | `/api/v1/dashboard/users/{id}/disable` | existing |
| US5.3b | POST | `/api/v1/dashboard/users/{id}/enable` | Phase 0 — new |
| US5.4 | PUT | `/api/v1/dashboard/users/{id}/roles` | existing |
| US6.1 | GET | `/api/v1/dashboard/audit-logs?search=...` | existing |
| US6.2 | GET | `/api/v1/dashboard/audit-logs?from=...&to=...&action=...&actor=...` | existing — query params verified Day 1 |
| US6.3 | (client-side CSV) | — | no backend dependency |

**Day 1 task**: generate `dashboard/openapi.json` from the running backend (`docker exec grid-api python -c "from ransomguard_grid.main import app; import json; print(json.dumps(app.openapi(), indent=2))"`) and store it in `dashboard/openapi.json` as the contract reference for type generation.

---

## 14. Non-Functional Requirements

### 14.1 Performance

| Metric | Target |
|---|---|
| Lighthouse Performance | ≥ 90 |
| First Contentful Paint (FCP) | < 1.5 s |
| Largest Contentful Paint (LCP) | < 2.5 s |
| Time to Interactive (TTI) | < 3.5 s |
| API p95 latency | < 500 ms |
| Page navigation | < 1 s |

### 14.2 Security

- HTTPS only (HSTS enforced by nginx)
- Login rate-limited (existing nginx config)
- CSP headers configured
- Access tokens in memory only (NEVER localStorage)
- Refresh tokens in HttpOnly Secure SameSite=Strict cookies
- XSS prevention via React escaping
- CSRF protection via SameSite cookies
- No sensitive data in URL query strings

### 14.3 Accessibility

| Standard | Target |
|---|---|
| WCAG 2.1 | Level AA |
| Keyboard navigation | 100% |
| Color contrast | ≥ 4.5:1 |
| Focus indicators | Visible on all interactive elements |
| Screen reader compatibility | NVDA, JAWS, VoiceOver |

### 14.4 Internationalization

- Default: French
- Secondary: English
- All strings in `public/locales/{fr,en}.json`
- Date/number formatting locale-aware (date-fns + Intl)

### 14.5 Browser Support

- Chrome ≥ 110
- Firefox ≥ 110
- Edge ≥ 110
- Safari ≥ 16

### 14.6 Test Coverage

| Test type | Target |
|---|---|
| Unit (Vitest) | ≥ 75% statements |
| Component | All pages + critical components |
| E2E (Playwright) | All Epic happy paths + RBAC enforcement |

---

## 15. Sprint Backlog & Estimation

### 15.1 Summary

| Phase | Item | Stories | Story Points | Days |
|---|---|:---:|:---:|:---:|
| 0 | Backend Quick-Wins | — | 5 | 1 |
| 1 | EPIC-AUTH | 3 | 8 | 1 |
| 1 | EPIC-DASHBOARD | 4 | 13 | 1.5 |
| 1 | EPIC-ALERTS | 4 | 18 | 2 |
| 1 | EPIC-AGENTS | 4 | 16 | 1.5 |
| 1 | EPIC-USERS | 4 | 13 | 1.25 |
| 1 | EPIC-AUDIT | 3 | 8 | 0.75 |
| Polish | E2E + a11y + perf | — | — | 1 |
| **Total** | | **22** | **81** | **10** |

### 15.2 Sequencing strategy

Day 1 — Phase 0:
- Implement `GET /api/v1/dashboard/me`
- Implement `POST /api/v1/dashboard/users/{id}/enable`
- Add pytest tests for both
- Verify all 94+ tests pass
- Commit and push

Day 2 — Sprint 7 bootstrap:
- Initialize Vite + React 18 + TypeScript in `dashboard/`
- Install all dependencies
- Configure TailwindCSS + shadcn/ui
- Configure i18n + i18next
- Configure Axios + TanStack Query
- Set up router skeleton
- Set up authStore (Zustand)
- Hello-world page with bilingual text confirms stack works

Days 3 — EPIC-AUTH:
- US1.1, US1.2, US1.3
- Verify login flow against live backend

Days 4 — EPIC-DASHBOARD:
- AppShell + Sidebar + Header (with language switcher, user menu)
- US2.1 routing
- US2.2 executive
- US2.3 operational
- US2.4 read-only

Days 5-6 — EPIC-ALERTS:
- US3.1, US3.2, US3.3, US3.4
- Most complex Epic, allow buffer

Days 7-8 — EPIC-AGENTS:
- US4.1, US4.2, US4.3, US4.4
- Command issuance carefully tested (admin only)

Day 9 — EPIC-USERS:
- US5.1, US5.2, US5.3, US5.4
- Self-modification prevention E2E tested

Day 10 — EPIC-AUDIT + Polish:
- US6.1, US6.2, US6.3
- Accessibility audit (axe-core)
- Lighthouse audit
- Final E2E suite run
- README, CHANGELOG
- Tag `v0.9.0-console`

### 15.3 Buffer

- 10% time buffer built into estimates
- High-risk Epics (AUTH, USERS) carry an extra half-day reserve
- If buffer not consumed: roll forward into Sprint 8 prep

---

## 16. Definition of Done

### 16.1 Story DoD

A User Story is Done when ALL true:

- [ ] All Acceptance Criteria pass automated tests
- [ ] Unit tests cover ≥ 75% of new code
- [ ] ≥ 1 E2E test exercises the happy path
- [ ] RBAC enforcement verified at UI and API call levels
- [ ] No hardcoded user names anywhere
- [ ] All user-facing strings in `fr.json` and `en.json`
- [ ] Accessibility audit passes (no critical violations)
- [ ] TypeScript compiles, no errors, no `any` types in business logic
- [ ] Self-code-review with SR-1 checklist
- [ ] Component-level documentation in code
- [ ] No console errors or warnings
- [ ] `npm audit` no high/critical vulnerabilities
- [ ] Git commit follows conventional format
- [ ] Story marked Done

### 16.2 Sprint DoD

A Sprint is Done when ALL true:

- [ ] 22 of 22 User Stories pass Story DoD
- [ ] Phase 0 backend endpoints implemented and tested
- [ ] All 94+ backend tests still pass after Phase 0
- [ ] Full frontend test suite passes
- [ ] Lighthouse audit results documented in `dashboard/docs/lighthouse-report.md`
- [ ] Accessibility audit results documented in `dashboard/docs/a11y-report.md`
- [ ] CHANGELOG.md updated (root + dashboard/)
- [ ] README.md updated with setup + usage
- [ ] Tag `v0.9.0-console` pushed to GitHub
- [ ] Sprint retrospective in `docs/sprint-reports/SPRINT_7_REPORT.md`

---

## 17. Risk Register

| ID | Risk | P | I | Mitigation |
|---|---|:---:|:---:|---|
| R1 | Backend endpoint response shapes differ from PRD assumptions | M | H | Generate OpenAPI spec Day 2 from live backend; rebuild types from there |
| R2 | Status enum values differ from PRD ("new" vs "open", etc.) | H | M | Read enum from `db/models/enums.py` Day 2; align types |
| R3 | "logout all sessions" not supported by backend | H | L | Descope AC1.2.2 to Sprint 8; ship single-session logout only |
| R4 | "restore network" command not implemented in backend | M | M | Descope AC4.3.3 to Sprint 8 if confirmed missing |
| R5 | Cross-tenant data leakage in UI | L | C | Reuse the 10/10 backend isolation tests as E2E tests |
| R6 | Time overrun due to undiscovered complexity | M | M | 10% buffer + ability to descope Epic 6 (Audit) export US6.3 if needed |
| R7 | Accessibility issues found late | M | M | Use axe-core in dev mode; audit at end of each Epic |
| R8 | i18n bugs (broken translations, layout issues) | M | L | All strings in JSON from Day 2; visual regression on both languages |
| R9 | Backend rate limits trip during E2E tests | L | M | Use a separate test tenant + raised rate limits in test config |
| R10 | Hardcoded user name slips into code via copy-paste from personas | L | M | Lint rule: forbid string literals matching persona names |

Legend: P (Probability), I (Impact). H=High, M=Medium, L=Low, C=Critical.

---

## 18. Out of Scope (Sprint 8 Candidates)

Explicitly NOT in Sprint 7. Captured for Sprint 8 planning.

### Frontend features

- MFA TOTP enrollment and verification (no backend endpoints exist)
- Password reset via email (no backend endpoint exists)
- Password change in user profile (no backend endpoint exists)
- Compliance Reports generation, signing, download (no `ComplianceReport` model)
- Threat Intelligence status dashboard (low business value relative to effort; backend endpoints are for agent consumption)
- Forensic trail visualization (process tree, file timeline — backend does not expose this shape)
- Bulk actions on alerts or agents (no bulk backend endpoints)
- WebSocket / SSE real-time push (polling acceptable for v0.9.0)
- Dark mode
- Native mobile applications
- Cross-tenant aggregation view for read_only_auditor
- Dashboard customization (saved layouts)
- Notifications center (in-app inbox)

### Backend features

- `Incident` model (cross-alert correlation grouping)
- `ComplianceReport` model + endpoints
- `Policy` model + endpoints
- `Settings` model + endpoints
- `ThreatIndicator` model (individual IOC tracking)
- MFA TOTP endpoints
- Password change endpoint
- "Logout all sessions" endpoint
- "Restore network" agent command (if not already present)
- WebSocket endpoint for real-time alerts

### Deployment

- MSI installer build automation for the agent
- Authenticode signing of agent binaries
- ANTIC API real integration (mock acceptable in Sprint 7)
- Penetration testing
- Performance load testing at 300 concurrent users

---

## End of PRD v2

This document is the single source of truth for Sprint 7 implementation. It is grounded in the empirical audit conducted 2026-06-07. Any deviation requires explicit decision, documentation update, and version bump (v2.1, v2.2, …).

Hand-off to Claude Code: Phase 0 first, then Day 2 bootstrap, then sequential Epic execution per Section 15.2.

— End —
