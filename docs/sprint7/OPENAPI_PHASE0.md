# Phase 0 — OpenAPI Specification

**Contract documentation for the two endpoints added to the RansomGuard-CM GRID backend in Phase 0.**

This document is the single source of truth for what the frontend Sprint 7 will consume. It is generated automatically from FastAPI route decorators + Pydantic schemas, but documented here in human-readable form for review and integration testing.

---

## Endpoint 1: `GET /api/v1/dashboard/me`

### Overview

Returns the authenticated user's complete identity, tenant context, roles, and preferences. Called by the frontend Console on:
- App boot (after refresh token validation)
- After explicit token refresh
- After language switch (to confirm server-acknowledged preference change — Sprint 8)

### Why This Endpoint Exists

The frontend needs to know:
1. **Which dashboard to render** — drives `tenant_admin` → Executive Dashboard, `security_analyst` → Operational Dashboard, `read_only_auditor` → Read-Only Dashboard.
2. **Tenant context** — namespace TanStack Query cache keys (`['alerts', tenant_id, ...]`) to prevent cross-tenant cache pollution (STRIDE WT1.6).
3. **User identity** — display in header (avatar initials, full name).
4. **Preferences** — initial language and timezone.

### Authentication

```
Authorization: Bearer <access_token>
```

The JWT must:
- Be signed with the GRID JWT secret (validated by `get_current_user` dependency)
- Not be in the Redis blacklist (revoked sessions)
- Have `exp` claim in the future (15-min TTL default)

### Response 200 — Success

```json
{
  "user_id": "550e8400-e29b-41d4-a716-446655440000",
  "email": "marc.tchoumi@hopital-yde.cm",
  "full_name": "Marc Tchoumi",
  "is_active": true,
  "tenant": {
    "id": "650e8400-e29b-41d4-a716-446655440001",
    "name": "Hôpital Régional de Yaoundé",
    "status": "active"
  },
  "roles": ["security_analyst"],
  "last_login_at": "2026-06-08T05:42:31Z",
  "preferences": {
    "language": "fr",
    "timezone": "Africa/Douala"
  }
}
```

#### Field Reference

| Field | Type | Required | Description |
|---|---|:-:|---|
| `user_id` | UUID v4 | ✅ | Authenticated user's UUID |
| `email` | string (EmailStr) | ✅ | User's email address |
| `full_name` | string (1-255) | ✅ | User's full display name |
| `is_active` | boolean | ✅ | Account status (always true if 200) |
| `tenant.id` | UUID v4 | ✅ | Tenant UUID — used by frontend for cache namespacing |
| `tenant.name` | string (1-255) | ✅ | Human-readable tenant name |
| `tenant.status` | enum (active\|suspended\|archived) | ✅ | Current tenant lifecycle status |
| `roles` | array of strings | ✅ | Exactly one role in Sprint 7: `tenant_admin`, `security_analyst`, or `read_only_auditor` |
| `last_login_at` | datetime (ISO 8601 UTC) \| null | ✅ | Previous login timestamp; null for first login |
| `preferences.language` | enum (fr\|en) | ✅ | Preferred UI language (default: `fr`) |
| `preferences.timezone` | string (IANA TZ) | ✅ | IANA timezone (default: `Africa/Douala`) |

### Response 401 — Unauthorized

```json
{
  "detail": "Token manquant ou invalide"
}
```

Triggered by:
- Missing `Authorization` header
- Malformed JWT (parse error)
- Expired JWT (exp < now)
- Revoked JWT (jti in Redis blacklist)
- JWT for deleted user (referential integrity defense)

### Response 403 — Forbidden

```json
{
  "detail": "Compte utilisateur désactivé"
}
```

OR

```json
{
  "detail": "Tenant inactif ou suspendu"
}
```

Triggered by:
- User was disabled between JWT issuance and request
- Tenant was suspended/archived between JWT issuance and request

(These are rare — should not occur with a valid recent JWT, but defense-in-depth.)

### Response 500 — Server Error

```json
{
  "detail": "Configuration tenant invalide"
}
```

Triggered by:
- Referential integrity violation (user has tenant_id but tenant deleted) — should never happen in normal operation

### Performance Target

- p50 latency: < 30 ms
- p95 latency: < 80 ms
- p99 latency: < 200 ms

Single DB query with `selectinload` for roles + tenant (avoids N+1).

### Frontend Integration Notes

```typescript
// dashboard/src/api/me.ts
import { z } from 'zod';

export const MeResponseSchema = z.object({
  user_id: z.string().uuid(),
  email: z.string().email(),
  full_name: z.string().min(1).max(255),
  is_active: z.boolean(),
  tenant: z.object({
    id: z.string().uuid(),
    name: z.string().min(1).max(255),
    status: z.enum(['active', 'suspended', 'archived']),
  }),
  roles: z.array(z.string()).min(1),
  last_login_at: z.string().datetime().nullable(),
  preferences: z.object({
    language: z.enum(['fr', 'en']),
    timezone: z.string(),
  }),
});

export type MeResponse = z.infer<typeof MeResponseSchema>;

export async function fetchMe(): Promise<MeResponse> {
  const response = await apiClient.get('/dashboard/me');
  return MeResponseSchema.parse(response.data); // Defense in depth
}
```

```typescript
// dashboard/src/hooks/use-me.ts
import { useQuery } from '@tanstack/react-query';

export function useMe() {
  return useQuery({
    queryKey: ['me'],  // No tenant scope here — /me IS the tenant resolver
    queryFn: fetchMe,
    staleTime: 5 * 60 * 1000,  // 5 min
    retry: false,  // Auth failures should not retry
  });
}
```

---

## Endpoint 2: `POST /api/v1/dashboard/users/{user_id}/enable`

### Overview

Reactivates a previously-disabled user account. Symmetric counterpart to the existing `POST /api/v1/dashboard/users/{user_id}/disable` endpoint.

### Why This Endpoint Exists

Without it, admins cannot reactivate users — a real operational gap. The frontend Sprint 7 Users management page shows a `Réactiver` menu option for disabled users that calls this endpoint.

### Authentication & Authorization

```
Authorization: Bearer <access_token>
```

**RBAC:** Caller MUST have role `tenant_admin`. Other roles receive 403.

### Path Parameters

| Param | Type | Description |
|---|---|---|
| `user_id` | UUID v4 | UUID of the user to enable |

### Request Body

None.

### Response 200 — Success

```json
{
  "id": "750e8400-e29b-41d4-a716-446655440002",
  "email": "paul.kamga@hopital-yde.cm",
  "full_name": "Paul Kamga",
  "is_active": true,
  "tenant_id": "650e8400-e29b-41d4-a716-446655440001",
  "roles": ["security_analyst"],
  "created_at": "2026-04-15T10:00:00Z",
  "updated_at": "2026-06-08T05:42:31Z",
  "last_login_at": "2026-05-30T14:22:15Z"
}
```

(Schema is the existing `UserResponse` — no new schema introduced.)

### Idempotency

| Initial state | Effect | Response | Audit action |
|---|---|---|---|
| `is_active = false` | Set to `true` | 200 + updated user | `user_enabled` |
| `is_active = true` | No change (NOOP) | 200 + current user | `user_enable_noop` |

The endpoint is idempotent: calling it multiple times has the same effect as calling it once. Each call still produces an audit log entry (with the NOOP variant if no state change occurred).

### Response 401 — Unauthorized

Missing or invalid JWT.

### Response 403 — Forbidden

```json
{
  "detail": "Rôle 'tenant_admin' requis pour cette action"
}
```

Triggered by:
- Caller has `security_analyst` role
- Caller has `read_only_auditor` role
- Caller has any role other than `tenant_admin`

### Response 404 — Not Found

```json
{
  "detail": "Utilisateur introuvable"
}
```

Triggered by:
- `user_id` does not exist in the database
- `user_id` exists BUT belongs to a different tenant (returned as 404, NOT 403, to prevent cross-tenant enumeration)

### Response 422 — Unprocessable Entity

```json
{
  "detail": "Vous ne pouvez pas modifier votre propre statut d'activation"
}
```

Triggered by:
- `user_id` equals the authenticated user's own ID (self-modification prevention)

OR (FastAPI validation):
```json
{
  "detail": [
    {
      "loc": ["path", "user_id"],
      "msg": "value is not a valid uuid",
      "type": "type_error.uuid"
    }
  ]
}
```

Triggered by:
- Malformed UUID in path (handled automatically by FastAPI path validation)

### Audit Log Entries Created

Every call produces exactly one audit log entry. The entry is signed with Ed25519 and linked via hash-chain (existing architecture).

#### When state changes (disabled → active)

```json
{
  "action": "user_enabled",
  "actor_user_id": "<admin UUID>",
  "tenant_id": "<tenant UUID>",
  "target_resource": "user",
  "target_id": "<target user UUID>",
  "timestamp": "2026-06-08T05:42:31Z",
  "ip_address": "<actor IP>",
  "payload": {
    "target_email": "paul.kamga@hopital-yde.cm",
    "previous_state": "disabled",
    "new_state": "active"
  },
  "signature": "<Ed25519 signature>",
  "prev_hash": "<previous entry hash>"
}
```

#### When already active (NOOP)

```json
{
  "action": "user_enable_noop",
  "actor_user_id": "<admin UUID>",
  "tenant_id": "<tenant UUID>",
  "target_resource": "user",
  "target_id": "<target user UUID>",
  "timestamp": "2026-06-08T05:42:31Z",
  "ip_address": "<actor IP>",
  "payload": {
    "target_email": "paul.kamga@hopital-yde.cm",
    "previous_state": "active",
    "new_state": "active"
  },
  "signature": "<Ed25519 signature>",
  "prev_hash": "<previous entry hash>"
}
```

### Performance Target

- p50 latency: < 50 ms
- p95 latency: < 150 ms
- p99 latency: < 300 ms

Operations:
- 1 SELECT (user with selectinload of roles)
- 1 UPDATE (if state change)
- 1 INSERT (audit log)
- 1 COMMIT

### Security Threat Mitigations (STRIDE Reference)

| Threat | Mitigation in this endpoint |
|---|---|
| WT4.2 — Broken authorization | Tenant filter on User query |
| WT4.5 — Privilege escalation | `require_role("tenant_admin")` + self-modify prevention |
| WT4.9 — Audit log tampering | Ed25519 signed entry, hash-chained |
| WT4.13 — IDOR | Tenant scope + 404 not 403 prevents enumeration |
| WT1.6 — Cache poisoning | TanStack Query mutation invalidates `['users', tenant_id]` |

### Frontend Integration Notes

```typescript
// dashboard/src/api/users.ts
export async function enableUser(userId: string): Promise<UserResponse> {
  const response = await apiClient.post(`/dashboard/users/${userId}/enable`);
  return UserResponseSchema.parse(response.data);
}
```

```typescript
// dashboard/src/hooks/use-enable-user.ts
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useMe } from './use-me';

export function useEnableUser() {
  const queryClient = useQueryClient();
  const { data: me } = useMe();

  return useMutation({
    mutationFn: (userId: string) => enableUser(userId),
    onMutate: async (userId) => {
      // Optimistic update
      await queryClient.cancelQueries({ queryKey: ['users', me?.tenant.id] });
      const previousUsers = queryClient.getQueryData(['users', me?.tenant.id]);
      queryClient.setQueryData(['users', me?.tenant.id], (old: any) =>
        old?.map((u: any) =>
          u.id === userId ? { ...u, is_active: true } : u
        )
      );
      return { previousUsers };
    },
    onError: (_, __, context) => {
      // Revert on failure
      if (context?.previousUsers) {
        queryClient.setQueryData(['users', me?.tenant.id], context.previousUsers);
      }
    },
    onSettled: () => {
      queryClient.invalidateQueries({ queryKey: ['users', me?.tenant.id] });
    },
  });
}
```

---

## Summary — Phase 0 Test Coverage

| Test File | Test Count | Coverage Aspect |
|---|:-:|---|
| `test_me.py` | 12 | Happy path × 3 roles, auth failures × 3, state changes × 2, isolation × 1, schema strictness × 3 |
| `test_user_enable.py` | 14 | Happy path, idempotency × 2, RBAC × 3, cross-tenant × 2, self-mod × 1, validation × 2, audit × 2, schema × 1 |
| **TOTAL** | **26** | All STRIDE web-tier threats relevant to these endpoints covered |

After Phase 0 merge: **94 existing + 26 new = 120 tests passing.**

---

## Final Acceptance Gate

Phase 0 is shippable when:

- ✅ All 26 new tests pass green on local + CI
- ✅ All 94 existing tests still pass (zero regressions)
- ✅ OpenAPI `/docs` shows both endpoints with full request/response schemas
- ✅ Mypy strict: 0 errors on new files
- ✅ Ruff: 0 warnings on new files
- ✅ Cross-tenant attack test: returns 404, no audit entry created on target
- ✅ Self-modification attack test: returns 422
- ✅ RBAC attack tests: 403 for analyst, 403 for auditor
- ✅ Tag created: `v0.8.1-backend-phase0`
- ✅ PR merged via SR-1 protocol (4 passes + attack tests + formal markdown artifact)
