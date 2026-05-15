# RansomGuard-CM API Reference Guide

**Version:** 1.0.0
**Author:** Jella Lebga (Kuate Abdel Yaniv)
**Date:** May 2026

---

## 1. OVERVIEW

The RansomGuard-CM platform exposes **4 distinct API surfaces**, each with its own authentication scheme and use case:

| API Surface | Auth | Consumers | Base Path |
|---|---|---|---|
| **Authentication** | None / Bearer | Dashboard frontend | `/api/v1/auth/*` |
| **Agent API** | mTLS | Windows agents | `/api/v1/agents/*`, `/api/v1/alerts` |
| **Dashboard API** | Bearer JWT + MFA | Web dashboard users | `/api/v1/incidents`, `/api/v1/endpoints`, etc. |
| **Admin API** | Bearer JWT + MFA + Role | Admin/Director only | `/api/v1/license/*`, `/api/v1/restore/*/approve` |
| **Cloud API** | Ed25519 signatures | Hospital server pulling updates | `/cloud/updates/*` |

---

## 2. AUTHENTICATION FLOW

### 2.1 Dashboard Login (2-Step MFA)

The login process is **always 2-step** — username/password alone never grants access.

```
Step 1: POST /api/v1/auth/login
  Request:  { email, password }
  Response: { mfa_required: true, mfa_token: "tmp_..." }
            ↓
            User enters TOTP code from authenticator app
            ↓
Step 2: POST /api/v1/auth/mfa/verify
  Request:  { mfa_token: "tmp_...", code: "847291" }
  Response: { access_token, refresh_token, expires_in: 900, user: {...} }
            ↓
            Use access_token in Authorization header for all subsequent requests
```

### 2.2 Token Lifecycle

- **Access token:** Valid 15 minutes — used in `Authorization: Bearer <token>` header
- **Refresh token:** Valid 24 hours, single-use — used to renew access token without re-login

```
POST /api/v1/auth/refresh
  Request:  { refresh_token: "..." }
  Response: { access_token, refresh_token (new), expires_in: 900 }
```

**Security note:** Each refresh invalidates the previous refresh token. If an attacker steals a refresh token, the legitimate user gets logged out on next refresh — detectable.

### 2.3 Agent Authentication (mTLS)

Agents do NOT use Bearer tokens. They use **mutual TLS**:

1. During enrollment, the server issues a unique X.509 certificate to each agent
2. The certificate is pinned (server stores fingerprint)
3. Every subsequent request from the agent must present this certificate
4. Server verifies signature AND fingerprint match
5. If certificate is revoked (compromised agent), all requests are rejected

This is **stronger than JWT** because:
- No token to steal in transit
- Bidirectional verification (server also proves identity)
- Hardware-friendly (certificates can live in TPM)

---

## 3. AGENT API DETAILS

### 3.1 Agent Enrollment

Called **once** during initial agent installation on a Windows endpoint.

```http
POST /api/v1/agents/enroll
Authorization: Bearer <admin_token>
Content-Type: application/json

{
  "hospital_id": "550e8400-e29b-41d4-a716-446655440000",
  "hostname": "POSTE-LAB-03",
  "mac_address": "00:1A:2B:3C:4D:5E",
  "ip_address": "192.168.10.45",
  "os_name": "Windows",
  "os_version": "10.0.19044",
  "os_architecture": "x64",
  "public_key": "MCowBQYDK2VwAyEA...",
  "agent_version": "1.0.0"
}
```

**Response (201):**
```json
{
  "agent_uuid": "660f9511-f30c-52e5-b827-557766550111",
  "certificate_pem": "-----BEGIN CERTIFICATE-----\nMII...",
  "valid_until": "2027-05-15T00:00:00Z",
  "server_ca_pem": "-----BEGIN CERTIFICATE-----\nMII..."
}
```

The agent stores the certificate and the server CA fingerprint. Future requests use mTLS.

### 3.2 Heartbeat (Critical Operation)

Called **every 30 seconds** by every agent. This is the highest-frequency endpoint.

```http
POST /api/v1/agents/heartbeat
[mTLS client certificate]
Content-Type: application/json

{
  "agent_uuid": "660f9511-f30c-52e5-b827-557766550111",
  "timestamp": "2026-05-15T14:30:00.123456Z",
  "health_status": "healthy",
  "memory_usage_mb": 124,
  "cpu_usage_percent": 2.8,
  "agent_version": "1.0.0",
  "configuration_hash": "a1b2c3d4..."
}
```

**Response (200):**
```json
{
  "config_update_available": false,
  "signatures_update_available": true
}
```

If `signatures_update_available: true`, the agent must pull `/api/v1/agents/signatures` to refresh threat intelligence.

### 3.3 Alert Submission

Called by agents whenever a detection signal fires.

```http
POST /api/v1/alerts
[mTLS client certificate]
Content-Type: application/json

{
  "endpoint_id": "770a0622-040d-63f6-c938-668877660222",
  "alert_type": "canary_file_modified",
  "severity": "critical",
  "signal_source": "canary",
  "signal_value": 1.0,
  "threshold": 1.0,
  "process_name": "suspicious.exe",
  "process_pid": 4521,
  "file_path_hash": "sha256:e3b0c44298fc1c149afbf4c8996fb92427...",
  "occurred_at": "2026-05-15T14:29:58.123456Z",
  "payload": {
    "canary_path_hash": "sha256:bc8d2891...",
    "entropy_observed": 7.82
  }
}
```

**Critical privacy note:** The agent NEVER sends actual file paths or content. Only hashes. This complies with Loi 2024/017 Article 7 (data minimization).

**Server correlation logic:**
1. Alert is stored in `alerts` table
2. Server checks last 60 seconds for other alerts from same endpoint
3. If ≥3 signals from different sources converge → an Incident is created
4. Incident triggers automated response: PDF generation, email to director, etc.

---

## 4. DASHBOARD API DETAILS

### 4.1 List Incidents (Most Used Endpoint)

```http
GET /api/v1/incidents?page=1&page_size=20&severity=critical&status=detected
Authorization: Bearer <jwt>
```

**Response (200):**
```json
{
  "data": [
    {
      "id": "abc123...",
      "incident_number": 8472,
      "hospital_id": "550e8400...",
      "endpoint_id": "770a0622...",
      "severity": "critical",
      "category": "ransomware",
      "title": "Ransomware Akira detected on POSTE-LAB-03",
      "detected_at": "2026-05-15T14:30:00Z",
      "contained_at": "2026-05-15T14:30:03Z",
      "signal_count": 4,
      "status": "contained",
      "files_affected": 0
    }
  ],
  "pagination": {
    "page": 1,
    "page_size": 20,
    "total": 1,
    "total_pages": 1
  }
}
```

### 4.2 Acknowledge Incident

After reviewing an incident, the technician marks it as acknowledged.

```http
POST /api/v1/incidents/abc123/acknowledge
Authorization: Bearer <jwt>
Content-Type: application/json

{
  "notes": "Verified false positive. Whitelisted process scan_lab.exe."
}
```

### 4.3 Generate Incident Report (PDF)

```http
GET /api/v1/reports/incidents/abc123?locale=fr-CM
Authorization: Bearer <jwt>
```

**Response (200):**
- Content-Type: `application/pdf`
- Body: Bilingual PDF binary content (downloadable)

The PDF includes:
- Incident summary in plain language
- Timeline of events
- Files affected (anonymized — counts only)
- Actions taken automatically
- Recommendations
- ANTIC compliance section

---

## 5. RESTORE WORKFLOW (Dual Authorization)

The most sensitive operation in the platform. Requires two distinct users to authorize.

### Step 1 — Technician Requests Restore

```http
POST /api/v1/restore/request
Authorization: Bearer <technician_jwt>
Content-Type: application/json

{
  "snapshot_id": "snap_abc...",
  "endpoint_id": "770a0622...",
  "request_scope": "full"
}
```

**Response (201):**
```json
{
  "id": "req_xyz...",
  "status": "pending",
  "requested_by": "user_technician_id",
  "requested_at": "2026-05-15T15:00:00Z"
}
```

### Step 2 — Director Approves (Different Session)

The director receives a notification, opens the dashboard in their own authenticated session, and approves:

```http
POST /api/v1/restore/req_xyz/approve
Authorization: Bearer <director_jwt>
```

**Response (200):**
```json
{
  "id": "req_xyz...",
  "status": "approved",
  "approved_by": "user_director_id",
  "approved_at": "2026-05-15T15:05:00Z"
}
```

**Authorization check (server-side):**
- The user calling `/approve` MUST have role = `director`
- The user calling `/approve` MUST be DIFFERENT from the user who called `/request`
- Both actions are logged in the immutable audit log

### Step 3 — Automatic Execution

Once approved, the restore service:
1. Activates IRONCLAD relay to connect backup disk
2. Reads encrypted snapshot from disk
3. Verifies AES-256-GCM authentication tags
4. Pushes files to target endpoint over mTLS
5. Reconnects endpoint to network
6. Disconnects IRONCLAD relay

The technician can monitor progress in real-time via WebSocket.

---

## 6. LICENSE MANAGEMENT

### 6.1 First-Time Activation

```http
POST /api/v1/license/activate
Authorization: Bearer <admin_jwt>
Content-Type: application/json

{
  "license_key": "RG-CM-2026-XXXX-YYYY-ZZZZ-AAAA",
  "server_fingerprint": "sha256:b2c3d4e5f6..."
}
```

**Response (200):**
```json
{
  "is_valid": true,
  "status": "active",
  "plan_type": "standard",
  "max_endpoints": 100,
  "current_endpoints": 0,
  "expires_at": "2027-05-15T00:00:00Z",
  "days_remaining": 365,
  "signed_token": "eyJhbGciOiJFZERTQSIsInR5cCI6Ikp..."
}
```

The `signed_token` is an Ed25519-signed activation token used for **offline validation** during internet outages. Valid for 60 days.

### 6.2 Periodic Validation

Every 24 hours, the server attempts online validation. If cloud is unreachable, it falls back to verifying the locally-stored signed_token. If both fail beyond grace period, the system enters degraded mode.

```http
GET /api/v1/license/status
Authorization: Bearer <admin_jwt>
```

---

## 7. CLOUD UPDATES (Ed25519 Signature)

### 7.1 Check for Updates

The hospital server polls every hour:

```http
GET /cloud/updates/check?current_version=1.0.0&server_id=server_xyz
X-Signature-Ed25519: <signature_of_request>
```

**Response (200):**
```json
{
  "current_version": "1.0.0",
  "latest_version": "1.1.0",
  "update_available": true,
  "is_critical": false,
  "released_at": "2026-05-20T10:00:00Z",
  "download_url": "https://cloud.ransomguard-cm.com/updates/1.1.0",
  "primary_signature": "ed25519:abc123...",
  "cosigner_signature": "ed25519:def456...",
  "package_hash": "sha256:789abc..."
}
```

### 7.2 Download and Verify

```http
GET /cloud/updates/1.1.0
X-Signature-Ed25519: <signature_of_request>
```

**Response (200):**
- Content-Type: `application/octet-stream`
- Body: Encrypted update package

**Critical client-side verification:**
1. Compute SHA-256 of downloaded package
2. Verify it matches `package_hash` from check response
3. Verify `primary_signature` against pinned author public key
4. Verify `cosigner_signature` against pinned cosigner public key
5. **Only if all checks pass** → apply update via canary deployment

---

## 8. ERROR HANDLING

### 8.1 Standardized Error Response

All errors follow this format:

```json
{
  "code": "VALIDATION_ERROR",
  "message": "The 'severity' field must be one of: info, warning, error, critical",
  "details": {
    "field": "severity",
    "value_received": "very_high"
  },
  "request_id": "req_550e8400-e29b-41d4-a716-446655440000",
  "timestamp": "2026-05-15T14:30:00.123456Z"
}
```

### 8.2 Error Code Catalog

| HTTP | Code | Meaning |
|---|---|---|
| 400 | `BAD_REQUEST` | Malformed request |
| 401 | `UNAUTHENTICATED` | No valid token/cert |
| 401 | `TOKEN_EXPIRED` | JWT expired — use refresh |
| 401 | `INVALID_MFA` | TOTP code incorrect |
| 403 | `INSUFFICIENT_ROLE` | Role doesn't allow this action |
| 403 | `MFA_REQUIRED` | Operation requires MFA re-auth |
| 403 | `DUAL_AUTH_REQUIRED` | Director approval needed |
| 404 | `RESOURCE_NOT_FOUND` | ID doesn't exist |
| 409 | `CONFLICT` | Resource already exists |
| 422 | `VALIDATION_ERROR` | Payload schema violation |
| 423 | `LICENSE_EXPIRED` | License invalid or expired |
| 429 | `RATE_LIMITED` | Too many requests |
| 500 | `INTERNAL_ERROR` | Unexpected server error |
| 503 | `SERVICE_UNAVAILABLE` | Dependency down (DB, etc.) |

---

## 9. RATE LIMITING

All endpoints enforce rate limits to prevent abuse.

| Endpoint Pattern | Limit |
|---|---|
| `POST /api/v1/auth/login` | 5 requests / 15 min / IP |
| `POST /api/v1/auth/mfa/verify` | 5 requests / 15 min / user |
| `POST /api/v1/agents/heartbeat` | 120 requests / min / agent |
| `POST /api/v1/alerts` | 60 requests / min / agent |
| `GET /api/v1/incidents` | 60 requests / min / user |
| All others | 600 requests / hour / user |

Response headers on every request:
```
X-RateLimit-Limit: 60
X-RateLimit-Remaining: 47
X-RateLimit-Reset: 1715784000
```

When limit is hit (429):
```
Retry-After: 60
```

---

## 10. SECURITY POSTURE SUMMARY

| Layer | Protection |
|---|---|
| Transport | TLS 1.3 minimum, certificate pinning |
| Authentication | OAuth2 + JWT + MFA TOTP for users, mTLS for agents |
| Authorization | RBAC with 5 roles (admin, technician, director, auditor, support) |
| Critical operations | Dual authorization (technician + director) |
| Cryptography | AES-256-GCM, Ed25519, Argon2id, SHA-256 |
| Update integrity | Double Ed25519 signature, canary deployment |
| Data minimization | No plain file paths/content in transit or logs |
| Audit | Append-only hash-chained logs (SHA-256 chain) |
| Rate limiting | Per-endpoint and per-user |
| Standards | OWASP API Top 10, NIST SP 800-53, ISO 27001, RFC 6749, RFC 8446 |

---

## END OF API REFERENCE GUIDE
