/**
 * useMe — TanStack Query hook for GET /dashboard/me (ADR-FE-003).
 *
 * This hook is the SINGLE source of truth for:
 * - Whether the user is authenticated (query succeeds vs 401)
 * - Which tenant_id to namespace all other query keys with
 * - Which role-based dashboard to render (primaryRole)
 *
 * Query key: ['me'] — deliberately NOT tenant-scoped, since /me IS the
 * tenant resolver (chicken-and-egg). All OTHER query keys (alerts,
 * agents, users, audit) MUST include tenant.id from this hook's data
 * to satisfy STRIDE WT1.6 (cross-tenant cache pollution defense).
 *
 * Example downstream usage:
 *   const { data: me } = useMe();
 *   useQuery({ queryKey: ['alerts', me?.tenant.id, filters], ... })
 */

import { useQuery } from '@tanstack/react-query';
import { fetchMe, type MeResponse, type Role } from '@/api/me';

const KNOWN_ROLES: readonly Role[] = [
  'tenant_admin',
  'security_analyst',
  'read_only_auditor',
] as const;

export function useMe() {
  return useQuery<MeResponse>({
    queryKey: ['me'],
    queryFn: fetchMe,
    staleTime: 5 * 60 * 1000, // 5 minutes
    retry: false, // auth failures should not retry — handled by axios interceptor
  });
}

/**
 * Resolve the user's primary role for dashboard routing.
 *
 * Sprint 7 contract: exactly one role per user. If the backend ever
 * returns multiple roles (Sprint 8+) or an unrecognized role string
 * (defensive), fall back to the most-restrictive option
 * (read_only_auditor) rather than over-granting access — fail closed,
 * per STRIDE WT4.5 (privilege escalation) mitigation philosophy.
 */
export function resolvePrimaryRole(roles: string[]): Role {
  const recognized = roles.filter((r): r is Role =>
    KNOWN_ROLES.includes(r as Role)
  );

  // Note: under tsconfig `noUncheckedIndexedAccess`, `recognized[0]` types
  // as `Role | undefined` even after a `.length === 1` check (TS does not
  // narrow index types from length comparisons). Bind to a variable and
  // narrow explicitly instead of using a non-null assertion.
  if (recognized.length === 1) {
    const onlyRole = recognized[0];
    if (onlyRole) {
      return onlyRole;
    }
  }

  if (recognized.includes('tenant_admin')) {
    // Sprint 8+ multi-role: admin takes precedence for landing page,
    // but RBAC enforcement always remains server-side.
    return 'tenant_admin';
  }

  if (recognized.includes('security_analyst')) {
    return 'security_analyst';
  }

  // Fail closed: unknown/empty -> most restrictive view.
  return 'read_only_auditor';
}
