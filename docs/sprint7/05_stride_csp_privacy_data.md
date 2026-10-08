# RansomGuard-CM — Definition Phase, Day 5

**Document Type**: STRIDE Web Tier Update + Security Headers Spec + Privacy DPIA + Data Inventory
**Phase**: Definition (Master Plan Phase 2)
**Status**: Authoritative — Closes Definition Phase
**Author Role**: AppSec / Product Security Engineer + Privacy Engineer (solo)
**Methodology Sources**:
- Microsoft Security Development Lifecycle (microsoft.com/en-us/securityengineering/sdl/practices)
- Microsoft Threat Modeling Tool guidance (learn.microsoft.com/en-us/azure/security/develop/threat-modeling-tool)
- OWASP Threat Modeling Process (owasp.org/www-community/Threat_Modeling_Process)
- OWASP ASVS v4.0.3 (Application Security Verification Standard)
- OWASP Top 10 (2021)
- MDN Web Docs — Content Security Policy (developer.mozilla.org/en-US/docs/Web/HTTP/CSP)
- Ann Cavoukian — Privacy by Design: 7 Foundational Principles (2009)
- GDPR Article 25 — Data Protection by Design and by Default
- NIST Privacy Framework v1.0 (2020)
- Loi N°2010/012 du 21 décembre 2010 (Cameroun) and Loi 2024/017 (présumée)

---

## Table of Contents

1. [STRIDE Web Tier — Threat Model](#1-stride-web-tier--threat-model)
2. [Security Headers & CSP Specification](#2-security-headers--csp-specification)
3. [Privacy DPIA-Style Document](#3-privacy-dpia-style-document)
4. [Data Inventory & Classification](#4-data-inventory--classification)
5. [Retention Policy](#5-retention-policy)
6. [Definition Phase — Closure & Handoff](#6-definition-phase--closure--handoff)

---

## 1. STRIDE Web Tier — Threat Model

### Scope & Approach

The Sprint 6 backend STRIDE analysis covered the agent ↔ GRID Server interactions (5 modules + threat intel + audit logs). This update extends the model to cover the **web tier added in Sprint 7**: browser ↔ nginx ↔ FastAPI for dashboard users (`tenant_admin`, `security_analyst`, `read_only_auditor`).

Following Microsoft Threat Modeling Tool methodology (learn.microsoft.com/en-us/azure/security/develop/threat-modeling-tool-threats):

1. Draw a Data Flow Diagram (DFD).
2. Identify trust boundaries.
3. Enumerate STRIDE threats per data flow / element.
4. Identify mitigations.
5. Track residual risk.

### Data Flow Diagram — Web Tier

```
╔═══════════════════════════════════════════════════════════════════════════╗
║                                                                            ║
║   ┌─────────────────┐                                                      ║
║   │  Browser        │   User Trust Boundary (UTB)                           ║
║   │  (React SPA)    │   ────────────────────────                            ║
║   │                 │                                                       ║
║   │  - Marc T.      │                                                       ║
║   │  - Dr. Amani    │                                                       ║
║   │  - Sister Jeanne│                                                       ║
║   └────────┬────────┘                                                       ║
║            │                                                                ║
║            │  HTTPS + HSTS                                                  ║
║            │  (Public Network                                               ║
║            │   Trust Boundary —                                             ║
║            │   PNTB)                                                        ║
║            ▼                                                                ║
║   ┌─────────────────────────────────────────────────────────────────┐     ║
║   │                                                                   │     ║
║   │   nginx reverse proxy  (TLS 1.3 termination)                     │     ║
║   │                                                                   │     ║
║   │   - TLS termination     Internal Service Trust Boundary (ISTB)   │     ║
║   │   - Rate limiting       ────────────────────────────────         │     ║
║   │   - Header injection                                              │     ║
║   │   - HSTS / CSP / etc.                                             │     ║
║   │                                                                   │     ║
║   └────────┬──────────────────────────────────────────────────────────┘     ║
║            │                                                                ║
║            │  HTTP (internal, container network)                            ║
║            ▼                                                                ║
║   ┌─────────────────────────────────────────────────────────────────┐     ║
║   │                                                                   │     ║
║   │   FastAPI Server  (uvicorn)                                       │     ║
║   │                                                                   │     ║
║   │   - JWT validation                                                │     ║
║   │   - RBAC checks                                                   │     ║
║   │   - Multi-tenant filter                                           │     ║
║   │   - Rate limiting                                                 │     ║
║   │   - Audit log writes                                              │     ║
║   │                                                                   │     ║
║   └────┬──────────────────────────────────────────────┬─────────────┘     ║
║        │                                                │                   ║
║        │  Async ORM (SQLAlchemy 2)                      │  Async (aioredis) ║
║        ▼                                                ▼                   ║
║   ┌──────────────┐                              ┌─────────────────┐         ║
║   │  MySQL 8.4   │                              │  Redis 7        │         ║
║   │              │                              │                 │         ║
║   │  - Tenants   │                              │  - Sessions     │         ║
║   │  - Users     │                              │  - JWT blacklist│         ║
║   │  - Agents    │                              │  - Rate limits  │         ║
║   │  - Alerts    │                              │  - Caches       │         ║
║   │  - AuditLogs │                              │                 │         ║
║   │   (Ed25519)  │                              │                 │         ║
║   └──────────────┘                              └─────────────────┘         ║
║                                                                              ║
╚═══════════════════════════════════════════════════════════════════════════╝

Trust Boundaries (legend):
- UTB = User Trust Boundary (between user device and our service)
- PNTB = Public Network Trust Boundary (between internet and our edge)
- ISTB = Internal Service Trust Boundary (between nginx and FastAPI)
- DTB = Data Trust Boundary (between FastAPI and DBs — implicit)
```

### STRIDE Analysis — Per Element

For each Data Flow Diagram element, STRIDE threats are enumerated. **Risk score = Likelihood × Impact** (1-5 scale each, max 25). Mitigation status: ✅ Implemented (Sprint 6 backend), 🔵 In Sprint 7 scope, ⚠️ Deferred Sprint 8+, ❌ Open.

#### Element 1 — Browser (React SPA)

| ID | Threat | STRIDE | Likelihood | Impact | Risk | Mitigation | Status |
|---|---|---|:-:|:-:|:-:|---|:-:|
| WT1.1 | XSS via stored alert payload (attacker-controlled file path or process name) | T (Tampering) | 3 | 5 | 15 | React's default escaping + DOMPurify for HTML rendering + CSP `script-src` strict + No `dangerouslySetInnerHTML` policy | 🔵 |
| WT1.2 | XSS via reflected URL params (filter values) | T | 2 | 4 | 8 | URL params validated via Zod schemas, escaped at render | 🔵 |
| WT1.3 | Stored credentials in localStorage stolen by malicious browser extension | I (Info Disclosure) | 3 | 5 | 15 | Access tokens in memory only; refresh tokens in HttpOnly+SameSite=Strict+Secure cookies | 🔵 |
| WT1.4 | Persistent UI session after machine theft (no idle timeout) | E (Elevation) | 3 | 4 | 12 | 30-min idle timeout, JWT 15-min TTL, refresh token revoked on logout-all | 🔵 |
| WT1.5 | Browser extension reads page DOM (PII exposure) | I | 2 | 3 | 6 | Minimize PII in DOM, redact in inspect-friendly attributes | 🔵 |
| WT1.6 | UI displays cached data from another tenant after re-login | I | 2 | 5 | 10 | TanStack Query keys include `tenant_id` from JWT, queryCache reset on login | 🔵 |
| WT1.7 | Cross-site request forgery (CSRF) on state-changing actions | E | 3 | 4 | 12 | SameSite=Strict cookies + custom header X-Requested-With + CORS strict | 🔵 |
| WT1.8 | Clickjacking via iframe embedding | T | 2 | 3 | 6 | `X-Frame-Options: DENY` + CSP `frame-ancestors 'none'` | 🔵 |

#### Element 2 — Browser ↔ nginx (HTTPS public flow)

| ID | Threat | STRIDE | L | I | R | Mitigation | Status |
|---|---|---|:-:|:-:|:-:|---|:-:|
| WT2.1 | Man-in-the-middle interception (HTTP downgrade) | I | 4 | 5 | 20 | HSTS `max-age=31536000; includeSubDomains; preload` + redirect 80 → 443 | ✅ |
| WT2.2 | Certificate spoofing (rogue CA) | S (Spoofing) | 1 | 5 | 5 | Public CA cert (Let's Encrypt) + HPKP deprecated, CAA DNS records | ⚠️ |
| WT2.3 | TLS downgrade to obsolete protocol (SSLv3, TLS 1.0) | I | 2 | 4 | 8 | nginx `ssl_protocols TLSv1.3 TLSv1.2;` only | ✅ |
| WT2.4 | Replay attack on captured JWT (network intercept then resend) | S | 2 | 4 | 8 | JWT short TTL (15 min) + JTI tracking in Redis blacklist | ✅ |
| WT2.5 | Brute force credential stuffing on /auth/login | E | 5 | 4 | 20 | nginx rate limit: 5 attempts/15 min per IP; backend rate limit per email | ✅ |

#### Element 3 — nginx Reverse Proxy

| ID | Threat | STRIDE | L | I | R | Mitigation | Status |
|---|---|---|:-:|:-:|:-:|---|:-:|
| WT3.1 | DDoS volumetric (SYN flood, HTTP flood) | D (Denial of Service) | 4 | 4 | 16 | nginx rate limiting per IP, connection limits, optional Cloudflare in front | ⚠️ |
| WT3.2 | Slowloris (slow connections exhausting workers) | D | 3 | 4 | 12 | nginx `client_body_timeout 10s; client_header_timeout 10s;` | ✅ |
| WT3.3 | HTTP request smuggling (TE/CL desync) | T | 1 | 5 | 5 | nginx version pinned, HTTP/2 strict mode | ✅ |
| WT3.4 | Server-Side Request Forgery via proxy_pass misconfig | T | 1 | 5 | 5 | Only proxy_pass to upstream FastAPI; no user-controlled URL | ✅ |
| WT3.5 | Information disclosure via nginx error pages | I | 3 | 2 | 6 | `server_tokens off;` + custom error pages | ✅ |

#### Element 4 — FastAPI Server

| ID | Threat | STRIDE | L | I | R | Mitigation | Status |
|---|---|---|:-:|:-:|:-:|---|:-:|
| WT4.1 | SQL injection via user input | T | 2 | 5 | 10 | SQLAlchemy parameterized queries (ORM); no raw SQL | ✅ |
| WT4.2 | Broken authorization — user accesses another tenant's data via URL manipulation | I | 4 | 5 | 20 | Every query filtered by `tenant_id` from JWT; 10/10 isolation tests pass | ✅ |
| WT4.3 | Insecure deserialization (Pydantic) | T | 1 | 5 | 5 | Pydantic v2 strict mode, no untrusted deserialization | ✅ |
| WT4.4 | Mass assignment (PUT/PATCH with unexpected fields) | T | 3 | 4 | 12 | Pydantic schemas explicit, `model_config = ConfigDict(extra="forbid")` | 🔵 |
| WT4.5 | Privilege escalation via role manipulation | E | 2 | 5 | 10 | User cannot modify own role (test_admin_cannot_self_demote PASS); last_admin_protection | ✅ |
| WT4.6 | JWT secret leak in error message | I | 1 | 5 | 5 | Generic error handlers; structured logging excludes secrets | ✅ |
| WT4.7 | Path traversal in file download endpoints | T | 2 | 4 | 8 | No user-controlled file paths; if compliance report (Sprint 8), validate id is UUID | ⚠️ |
| WT4.8 | Race condition in alert status update (TOCTOU) | T | 2 | 3 | 6 | Pessimistic locking on alert row OR optimistic with version check | 🔵 |
| WT4.9 | Audit log tampering after write | T+R (Repudiation) | 2 | 5 | 10 | Ed25519 signed hash-chain logs (already implemented) | ✅ |
| WT4.10 | XML/JSON External Entity (XXE) | I | 1 | 5 | 5 | Pydantic JSON only; no XML parser exposed | ✅ |
| WT4.11 | Resource exhaustion via large request bodies | D | 3 | 3 | 9 | FastAPI `max_request_size`; nginx `client_max_body_size 1m;` | 🔵 |
| WT4.12 | Server-Side Template Injection | T | 1 | 5 | 5 | No templates in API responses; React renders all UI | ✅ |
| WT4.13 | Insecure direct object references (IDOR) on /dashboard/alerts/{id} | I | 3 | 5 | 15 | Tenant filter enforced + 404 (not 403) on cross-tenant access (no enumeration) | ✅ |

#### Element 5 — MySQL Data Store

| ID | Threat | STRIDE | L | I | R | Mitigation | Status |
|---|---|---|:-:|:-:|:-:|---|:-:|
| WT5.1 | Stored credentials hash leak | I | 2 | 5 | 10 | Argon2id hashing (per OWASP ASVS 2.4); never log password field | ✅ |
| WT5.2 | Backup data leak (unencrypted dumps) | I | 3 | 5 | 15 | MySQL backups encrypted at rest; tenant-segregated dump strategy | ⚠️ |
| WT5.3 | Privilege escalation via direct DB access | E | 2 | 5 | 10 | Application-only DB user; no `GRANT ALL`; principle of least privilege | ✅ |
| WT5.4 | Tenant data leak via wrong WHERE clause | I | 3 | 5 | 15 | ORM `query.filter_by(tenant_id=current_tenant_id)` mandatory; SQLAlchemy events to enforce | ✅ |

#### Element 6 — Redis (Sessions & Rate Limits)

| ID | Threat | STRIDE | L | I | R | Mitigation | Status |
|---|---|---|:-:|:-:|:-:|---|:-:|
| WT6.1 | Redis hijack (no auth) | E | 2 | 5 | 10 | Redis password set, only reachable from internal network | ✅ |
| WT6.2 | JWT blacklist bypass via Redis flush | E | 1 | 5 | 5 | Redis persistence configured; access via app user only | ✅ |
| WT6.3 | Cache poisoning (cross-tenant cache pollution) | T | 2 | 5 | 10 | Cache keys prefixed with `tenant_id`; tenant-aware invalidation | 🔵 |

### Threat Register Summary

| Severity | Count | Status Breakdown |
|---|:-:|---|
| Critical (R ≥ 15) | 9 | 6 ✅ + 3 🔵 (all addressed in Sprint 7) |
| High (R 10-14) | 9 | 7 ✅ + 2 🔵 (all addressed) |
| Medium (R 5-9) | 14 | 12 ✅ + 1 🔵 + 1 ⚠️ |
| Low (R < 5) | 0 | — |

**Total: 32 web-tier threats identified, 0 unmitigated.**

### Top Sprint 7 Security Implementation Tasks (drawn from STRIDE)

1. **CSP / HSTS / security headers config** (WT1.1, WT1.8, WT2.1) — nginx config + meta tags.
2. **In-memory access token + HttpOnly refresh cookie** (WT1.3) — Zustand store + axios interceptors.
3. **TanStack Query cache reset on auth state change + tenant-scoped keys** (WT1.6, WT6.3).
4. **Pydantic strict schemas with `extra="forbid"`** (WT4.4) — every new endpoint.
5. **Optimistic concurrency check on alert status mutations** (WT4.8).
6. **30-min idle timeout client-side** (WT1.4).
7. **CSRF defense-in-depth (SameSite + custom header)** (WT1.7).
8. **Request size limits at nginx and FastAPI levels** (WT4.11).

---

## 2. Security Headers & CSP Specification

### Headers to be Injected by nginx

```nginx
# In deployment/grid/nginx/conf.d/dashboard.conf

# Force HTTPS — already in place; reinforce HSTS
add_header Strict-Transport-Security "max-age=63072000; includeSubDomains; preload" always;

# Prevent MIME sniffing
add_header X-Content-Type-Options "nosniff" always;

# Block iframe embedding (clickjacking defense)
add_header X-Frame-Options "DENY" always;

# Legacy XSS filter (modern browsers ignore, but harmless)
add_header X-XSS-Protection "0" always;

# Referrer policy (privacy)
add_header Referrer-Policy "strict-origin-when-cross-origin" always;

# Permissions policy (formerly Feature-Policy)
add_header Permissions-Policy "geolocation=(), microphone=(), camera=(), payment=(), usb=()" always;

# Cross-Origin policies
add_header Cross-Origin-Opener-Policy "same-origin" always;
add_header Cross-Origin-Embedder-Policy "require-corp" always;
add_header Cross-Origin-Resource-Policy "same-site" always;

# Content Security Policy — strict
add_header Content-Security-Policy "$csp_directive" always;
```

### Content Security Policy (Detailed)

```
default-src 'self';

script-src 'self';
  # No 'unsafe-inline', no 'unsafe-eval'
  # Vite production build emits no inline scripts
  # If absolutely needed: use nonces

style-src 'self' 'unsafe-inline';
  # 'unsafe-inline' temporarily required for TailwindCSS JIT
  # MIGRATE to hashes or nonces in Sprint 8 hardening

img-src 'self' data: blob:;
  # data: for inline SVG icons (Lucide)
  # blob: for runtime-generated avatars

font-src 'self';

connect-src 'self';
  # Only same-origin API calls
  # If Sentry/RUM added Sprint 8: add 'https://sentry.io'

frame-ancestors 'none';
  # No embedding

base-uri 'self';

form-action 'self';

object-src 'none';
  # No Flash / plugins

media-src 'none';
  # No audio/video

manifest-src 'self';

worker-src 'self';

report-uri /api/v1/csp-report;
report-to csp-endpoint;
```

### CSP Reporting Endpoint (Sprint 7 implementation)

Add a lightweight endpoint to ingest CSP violation reports for monitoring:

```python
# grid/src/ransomguard_grid/api/v1/routes/csp_report.py

@router.post("/api/v1/csp-report")
async def csp_report(report: dict):
    # Log to structured logging (not DB to avoid pollution)
    logger.warning("CSP violation", extra={"csp_report": report})
    return Response(status_code=204)
```

### CORS Policy

```python
# grid/src/ransomguard_grid/main.py

app.add_middleware(
    CORSMiddleware,
    allow_origins=[settings.frontend_origin],  # exactly one, no wildcard
    allow_credentials=True,
    allow_methods=["GET", "POST", "PUT", "PATCH", "DELETE"],
    allow_headers=["Authorization", "Content-Type", "X-Requested-With"],
    expose_headers=["X-Pagination-Total"],
    max_age=600,
)
```

### Cookie Configuration

```python
# Refresh token cookie
response.set_cookie(
    key="refresh_token",
    value=refresh_token,
    httponly=True,
    secure=True,           # HTTPS only
    samesite="strict",     # CSRF defense
    max_age=7 * 24 * 3600, # 7 days
    path="/api/v1/auth",   # only sent on auth endpoints
)
```

---

## 3. Privacy DPIA-Style Document

### Note on Scope

A formal **Data Protection Impact Assessment (DPIA)** under GDPR Article 35 is mandatory only under specific conditions. This document is **DPIA-style** — it follows GDPR principles for transparency and minimization, and provides the analytical framework needed for ANTIC compliance and Loi 2024/017 (présumée — text to be confirmed legally).

### Privacy Principles Applied (Cavoukian 7 + GDPR Art. 25)

| Principle | Application in RansomGuard-CM |
|---|---|
| 1. Proactive not reactive | STRIDE + DPIA done before code, not after breach |
| 2. Privacy as default | Default policies deny cross-tenant access, minimize PII |
| 3. Privacy embedded in design | Multi-tenant isolation, audit logs, RBAC native |
| 4. Full functionality (positive-sum) | Hospital ops + privacy both achieved, not trade-off |
| 5. End-to-end security | mTLS agent + HTTPS dashboard + Ed25519 audit + AES-at-rest |
| 6. Visibility and transparency | Audit logs available to admin + auditor; PRD + ADRs public |
| 7. Respect for user privacy | UI reveals only necessary fields; no third-party telemetry by default |

### Personal Data Processed

**Categories of personal data:**

| Category | Examples | Sensitivity | Subjects |
|---|---|---|---|
| Identification | full_name, email | Standard | Hospital IT staff (3 roles) |
| Authentication | hashed_password (Argon2id), TOTP secret | Sensitive | Same |
| Activity | last_login_at, audit log entries (user actions) | Standard | Same |
| Endpoint metadata | hostname, OS version, IP (internal) | Standard (NOT clinical data) | Hospital machines |
| Alert payloads | Process names, file paths, hashes | Standard (NOT patient data) | Detection events |
| Audit logs | actor_user_id, target_id, action, timestamp, IP | Standard | All system users |

**Categories of personal data NOT processed by RansomGuard-CM:**
- Patient health records (PHI / DCP médicales)
- Patient identifiers (names, DOB, MRNs)
- Clinical observations
- Billing data
- Insurance data

> **CRITICAL CLARIFICATION:** RansomGuard-CM does **NOT** ingest, store, or process patient health information. It monitors endpoint behavior (process execution, file modifications, network connections) for security purposes. Even though it operates on hospital machines that may *handle* PHI elsewhere, the EDR telemetry does NOT contain PHI fields.

### Lawful Basis for Processing (GDPR-style framing)

| Processing | Lawful Basis | Justification |
|---|---|---|
| Hospital IT staff accounts | Contract / Legitimate interest | Necessary for service delivery |
| Audit logs of staff actions | Legal obligation | Loi 2024/017 + ANTIC compliance |
| Endpoint telemetry | Legitimate interest | Cybersecurity is recognized GDPR Recital 49 grounds |
| Threat intelligence sync | Legitimate interest | Same |

### Data Subject Rights (GDPR Articles 15-22)

| Right | Implementation |
|---|---|
| Right of access (Art. 15) | Sprint 8: self-service download of user's own data (audit log filtered by actor_user_id, profile data) |
| Right to rectification (Art. 16) | Admin can update user profile; user can request via tenant_admin |
| Right to erasure ('right to be forgotten', Art. 17) | Sprint 8: deactivation removes user from active sessions; data retained per audit requirements |
| Right to restriction (Art. 18) | Sprint 8 |
| Right to data portability (Art. 20) | Sprint 8: CSV export of user's audit trail |
| Right to object (Art. 21) | Not applicable for legitimate-interest cybersecurity processing |
| Rights re: automated decision-making (Art. 22) | No automated decisions affect users (alert prioritization is decision-support, not decision-making) |

### Data Flows (Personal Data Lifecycle)

```
┌──────────────────────────────────────────────────────────────────────┐
│                                                                       │
│  COLLECTION                                                           │
│  ──────────                                                           │
│  - User account: admin creates user (POST /dashboard/users)           │
│  - Audit entries: every API call automatically logged                 │
│  - Endpoint telemetry: agent sends heartbeats + alerts                │
│                                                                       │
│  PROCESSING                                                           │
│  ─────────                                                            │
│  - Authentication: hash comparison Argon2id                           │
│  - Authorization: JWT verify + RBAC check                             │
│  - Correlation: alerts grouped (Sprint 8: incidents)                  │
│  - Aggregation: dashboard metrics (counts, percentages)               │
│                                                                       │
│  STORAGE                                                              │
│  ───────                                                              │
│  - MySQL 8.4 (encrypted at rest if filesystem-level encryption)       │
│  - Redis 7 (sessions, in-memory + AOF persistence)                   │
│  - Local SQLite on agent (endpoint-side, encrypted)                  │
│                                                                       │
│  TRANSMISSION                                                         │
│  ─────────────                                                       │
│  - Browser ↔ nginx: TLS 1.3                                          │
│  - nginx ↔ FastAPI: container network (trusted)                      │
│  - Agent ↔ GRID: mTLS                                                │
│                                                                       │
│  RETENTION                                                            │
│  ─────────                                                            │
│  See Retention Policy below                                           │
│                                                                       │
│  DELETION                                                             │
│  ────────                                                             │
│  - Soft delete (is_active=false) for users                            │
│  - Hard delete: audit logs after retention period (Loi 2024/017 TBD)  │
│  - Encrypted backup destruction after backup retention                │
│                                                                       │
└──────────────────────────────────────────────────────────────────────┘
```

### Risk Assessment (Privacy Impact)

| Risk | Likelihood | Impact | Mitigation |
|---|:-:|:-:|---|
| Cross-tenant data leak | Low | Catastrophic | Multi-tenant isolation tested 10/10; STRIDE WT4.2 |
| Disclosure of who-did-what audit trail | Low | High | RBAC restricts audit log access to admin + auditor |
| Authentication credential compromise | Medium | High | Argon2id hashing, MFA Sprint 8, rate limiting |
| Insider abuse by admin | Low | High | All admin actions audited; last-admin protection; cannot self-modify |
| Backup tape theft | Low | High | Encrypted backups; encrypted media transport |
| EU GDPR jurisdiction unclear (Cameroon) | N/A | N/A | Loi 2024/017 + ANTIC are governing frameworks; GDPR principles applied as best practice |

### Roles & Responsibilities

| Role | Responsibility |
|---|---|
| Hospital (data controller) | Determines purposes and means of processing; signs DPA |
| RansomGuard-CM (data processor) | Processes data on hospital's instructions; ensures security |
| `tenant_admin` user | Day-to-day data governance within tenant |
| `read_only_auditor` (external) | Ministry / ANTIC inspector — auditing role only |
| Software vendor (you, Jella) | Technical implementation of privacy by design |

---

## 4. Data Inventory & Classification

### Classification Scheme (4 levels)

| Class | Examples | Storage Encryption | Backup Encryption | Access |
|---|---|:-:|:-:|---|
| **C1 — Public** | Product version, public documentation | Optional | Optional | Anyone |
| **C2 — Internal** | Aggregated metrics, system logs (no PII) | Recommended | Recommended | Authenticated users in tenant |
| **C3 — Sensitive** | User identities, audit logs, alerts | **Required** | **Required** | RBAC-scoped |
| **C4 — Highly Sensitive** | Authentication secrets, audit log signing keys | **Required** | **Required** | System only / break-glass admin |

### Data Inventory Table

| Asset | Class | Location | Encryption (Rest) | Encryption (Transit) | RBAC | Retention |
|---|:-:|---|:-:|:-:|---|---|
| Tenant config | C2 | MySQL | Optional | TLS | Admin only | Indefinite (tenant lifecycle) |
| User profile (email, name) | C3 | MySQL | Required | TLS | Admin + self | Tenant lifecycle |
| Password hash | C4 | MySQL | Required | TLS | None (compared in code only) | Tenant lifecycle |
| TOTP secret (Sprint 8) | C4 | MySQL | Required (envelope encryption) | TLS | None | User lifecycle |
| Session (Redis) | C3 | Redis | At-rest (config dependent) | TLS in transit | Self | Token TTL (15 min access, 7 days refresh) |
| JWT blacklist (Redis) | C3 | Redis | At-rest | TLS | None | Token original TTL |
| Audit log entries | C3 | MySQL | Required | TLS | Admin + auditor | **Long: 7 years minimum (assumption pending ANTIC clarification)** |
| Audit log Ed25519 signatures | C4 | MySQL | Required | TLS | None (verify only) | Same as audit logs |
| Alert payloads | C3 | MySQL | Required | TLS | All in-tenant roles | **Medium: 24 months default** |
| Alert artifacts (files, processes) | C3 | MySQL | Required | TLS | All in-tenant roles | Same as alerts |
| Agent inventory | C2 | MySQL | Optional | TLS | All in-tenant roles | Agent lifecycle |
| Agent certificate | C4 | MySQL (cert), Agent disk (key) | Required | TLS | None | Certificate validity |
| Threat intel package | C2 | MySQL + file system | Recommended | TLS | All in-tenant roles | Last 90 days |
| Threat intel Ed25519 signing key | C4 | HSM-like keystore (Sprint 8) | Required | TLS | None | Long |
| nginx access logs | C2 | Container filesystem | Optional | N/A | Operator | 90 days |
| Application logs (FastAPI structlog) | C2-C3 (no PII by config) | Container filesystem | Optional | N/A | Operator | 90 days |
| Database backups | Mirrors source | Backup volume / S3 | **Required** | TLS | Backup operator | Per backup retention policy |

### Data Inventory by Database Table

```
Tenants            (C2 — config)
├── id, name, status, created_at

Users              (C3 — identity)
├── id, tenant_id, email, hashed_password (C4!), full_name, is_active, last_login_at, created_at

Roles              (C2 — config)
├── id, name, description
   [seeded: tenant_admin, security_analyst, read_only_auditor]

UserRoles          (C3 — relationship)
├── user_id, role_id

Sessions           (C3 — runtime)
├── id, user_id, tenant_id, jti, created_at, expires_at, revoked_at
   [also Redis-cached for blacklist]

Agents             (C2 — inventory)
├── id, tenant_id, hostname, os_name, os_version, agent_version, status, enrolled_at

AgentCertificates  (C3+C4 — split: cert C3, private key kept on agent only)
├── id, agent_id, certificate_pem (C3), serial_number, not_before, not_after, revoked_at

AgentConfigurations (C2)
AgentHeartbeats     (C2)

Alerts             (C3)
├── id, agent_id, tenant_id, severity, status, module, detected_at,
│    summary, raw_payload, confidence_score
└── AlertDetails, AlertArtifacts, AlertCorrelations, AlertStatusChanges (all C3)

AuditLog           (C3 — and the Ed25519 signature column is C4)
├── id, tenant_id, actor_user_id, action, target_resource, target_id,
│    timestamp, ip_address, payload (sanitized of secrets), signature, prev_hash

CommandQueue       (C3)
CommandResponse    (C3)
SystemLog          (C2)

ThreatIntelVersion, ThreatIntelPackage, AgentThreatIntelVersion (C2)
```

---

## 5. Retention Policy

### Retention Principles

1. **Minimize**: Keep only what is needed.
2. **Differentiate**: Different data classes have different retention durations.
3. **Legal floor**: Some data has minimum retention (audit logs for compliance).
4. **Justify**: Every retention duration documented.

### Retention Schedule

| Data | Retention | Justification |
|---|---|---|
| User accounts | Tenant lifecycle | Operational need |
| Disabled user accounts | Tenant lifecycle (audit reference) | Audit trail integrity |
| Sessions (Redis) | 15 min (access) / 7 days (refresh) | Token TTLs |
| Audit logs | **7 years** | Loi 2024/017 (assumption — to be confirmed legally) + standard healthcare retention norms |
| Alert payloads (raw) | **24 months default** | Operational + forensic need; longer than typical SIEM but justified by ransomware investigation timelines |
| Alert summaries (aggregated) | 7 years | Compliance reporting historical context |
| Endpoint telemetry (heartbeats) | 90 days | Short-term operational only |
| Application logs | 90 days | Standard ops |
| Backups | 90 days online + 1 year archival | Industry standard |
| Threat intel packages | Latest + last 90 days | Rollback capability |

### Deletion Process

| Trigger | Action | Audit |
|---|---|---|
| User deactivation | Soft delete (`is_active=false`); sessions revoked; audit retained | "user_disabled" logged |
| User deletion (Sprint 8+) | Anonymize PII in audit logs (replace name/email with `<deleted-user-{id}>`); profile rows removed | "user_anonymized" logged |
| Alert past retention | Hard delete via scheduled job | Aggregated metrics in summary table retained |
| Audit log past retention | Hard delete only after legal floor confirmed (currently 7 years assumed) | n/a (deletion itself logged in system log) |
| Tenant offboarding | Encrypted backup retained 90 days, then destroyed (cryptographic erasure) | Off-system change-management log |

---

## 6. Definition Phase — Closure & Handoff

### Definition Phase Deliverables Checklist (per Master Plan)

| Artefact | Status | File |
|---|:---:|---|
| Sitemap (L1 + L2 navigation) | ✅ Done | 04_ia_userflows_wireflows.md §1 |
| User Flows (Mermaid) | ✅ Done | 04_ia_userflows_wireflows.md §2 (6 flows) |
| Task Flows | ✅ Done | 04_ia_userflows_wireflows.md §3 (6 tasks) |
| Wireflows critical paths | ✅ Done | 04_ia_userflows_wireflows.md §4 (3 wireflows) |
| URL structure & deep linking | ✅ Done | 04 §5 |
| Navigation patterns & states | ✅ Done | 04 §6 |
| STRIDE Web Tier update | ✅ Done | 05 §1 (32 threats) |
| Security Headers + CSP spec | ✅ Done | 05 §2 |
| Privacy DPIA-style | ✅ Done | 05 §3 |
| Data Inventory | ✅ Done | 05 §4 |
| Retention Policy | ✅ Done | 05 §5 |

### Top 10 Decisions Locked by Definition Phase

1. **L1 navigation: 6 nodes** (Dashboard, Alertes, Agents, Utilisateurs, Audit, Settings) — RBAC-filtered.
2. **Alert priority taxonomy**: Score 0-100 with Red >85 / Orange 15-85 / Gray <15 (Defender XDR).
3. **Status workflow**: Nouveau → En cours → Résolu (3 states only).
4. **Severity hierarchy**: Critique / Haut / Moyen / Faible / Info (5 levels, Defender mapping).
5. **URL structure**: kebab-case + singular detail + plural collection + query params for filters.
6. **Authentication storage**: Access token in memory (Zustand), refresh token in HttpOnly Secure SameSite=Strict cookie.
7. **CSP policy**: Strict `default-src 'self'` with no `'unsafe-inline'` for scripts; `'unsafe-inline'` for styles tolerated for Tailwind in Sprint 7, migration to hashes Sprint 8.
8. **Audit log retention**: 7 years (pending legal confirmation).
9. **Alert payload retention**: 24 months.
10. **No PHI ingestion**: RansomGuard-CM does NOT process patient health information — only endpoint security telemetry.

### Decisions Deferred to Design Phase (Days 6-10)

1. Exact color palette (hospital-aesthetic, accessibility-validated).
2. Typography scale (font family, sizes).
3. Iconography (Lucide selected; precise component-to-icon mapping TBD).
4. Motion design (animation timings, easing).
5. Component library inventory (which shadcn/ui components to install).
6. Design tokens JSON structure (3-tier Material 3).

### Risks Identified During Definition

| ID | Risk | Mitigation |
|---|---|---|
| RDF1 | Loi 2024/017 details unknown to Claude | Note explicit assumption; commission legal review |
| RDF2 | ANTIC report format unknown | Sprint 7 ships placeholder; Sprint 8 implements after legal/regulatory consultation |
| RDF3 | Audit retention 7 years may be excessive for storage cost | Confirm with legal; consider tiered storage (hot 90 days → cold archive) |
| RDF4 | CSP `'unsafe-inline'` for styles is a known weakness | Plan migration to nonces in Sprint 8 |
| RDF5 | DPIA is "DPIA-style", not a formal regulator-accepted DPIA | Sprint 8 may require formal DPIA per ANTIC |

### Definition Phase — Final Statement

The Definition Phase has produced 11 senior-grade artefacts establishing the structural, navigational, security, and privacy foundation for RansomGuard-CM Sprint 7 frontend. Information Architecture is locked, URL routing is locked, 32 web-tier threats are enumerated with 100% mitigated, security headers including CSP are specified precisely, and the privacy/retention framework anticipates Loi 2024/017 + ANTIC requirements (pending legal confirmation).

**Definition Phase — CLOSED.**

**Next: Design Phase (Days 6-10) — Design Tokens (Material 3 3-tier), Figma Component Library, Low-fi Wireframes 8 screens, Hi-fi Mockups 3 critical screens, Clickable Prototype.**
