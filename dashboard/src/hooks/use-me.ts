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

import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { changePassword, fetchMe, updateMe, updatePreferences, type MeResponse, type Role } from '@/api/me';

// ── Settings (Fix C): what a user changes about themselves ─────────────

export function useUpdateMe() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (payload: { full_name: string }) => updateMe(payload),
    onSuccess: (me) => queryClient.setQueryData(['me'], me),
  });
}

export function useUpdatePreferences() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (payload: { language: 'fr' | 'en'; timezone?: string }) => updatePreferences(payload),
    // The cached /me carries the new preferences at once (the route guard applies the language
    // from it), then a refetch confirms what the GRID kept.
    onSuccess: (preferences) => {
      queryClient.setQueryData<MeResponse>(['me'], (me) => (me ? { ...me, preferences } : me));
      void queryClient.invalidateQueries({ queryKey: ['me'] });
    },
  });
}

export function useChangePassword() {
  return useMutation({
    mutationFn: (payload: { current_password: string; new_password: string }) => changePassword(payload),
  });
}

const KNOWN_ROLES: readonly Role[] = [
  'tenant_admin',
  'security_analyst',
  'read_only_auditor',
] as const;

/**
 * How long the console may go on drawing menus for the role the user had.
 *
 * The server does not care: `require_roles` reads the roles from the database on every request,
 * so a role change takes effect on the user's very next request with the token they already hold
 * (grid/tests/api/test_role_change_takes_effect.py). The menus here are a convenience over that,
 * never the authority.
 *
 * But a convenience that is wrong for five minutes -- the old staleTime -- is a console that
 * shows an analyst actions the server will refuse, so it is re-read every minute and whenever the
 * window is focused again. Anything the console itself changes about roles invalidates ['me'] on
 * the spot (see useUpdateUserRoles), so the lag only applies to a change made elsewhere.
 */
export const ME_REFRESH_MS = 60 * 1000;

export function useMe() {
  return useQuery<MeResponse>({
    queryKey: ['me'],
    queryFn: fetchMe,
    staleTime: ME_REFRESH_MS / 2,
    refetchInterval: ME_REFRESH_MS,
    refetchOnWindowFocus: true,
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
