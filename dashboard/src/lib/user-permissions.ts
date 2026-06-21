/**
 * user-permissions.ts
 * EPIC-USERS — client-side permission guards for user management actions.
 *
 * All actions here are also protected by backend RBAC (403 if wrong role),
 * but some require additional client-side guards due to backend gaps.
 */

/**
 * SECURITY GAP (GRID-SEC-002, discovered Day 6-7 Step 0 diagnostic):
 * Unlike enable_user (which returns 422 on self-modify), disable_user
 * has NO server-side self-protection — a direct API call would succeed
 * (200) even for self-disable. This client-side guard is therefore NOT
 * just a UX convenience here (unlike the alert-permissions.ts pattern) —
 * it is the ONLY protection against accidental self-lockout until the
 * backend adds the guard.
 * TODO Sprint 8: backend must reject self-disable with 403, matching
 * the pattern already used by enable_user.
 */
export function canDisable(targetUserId: string, currentUserId: string): boolean {
  if (targetUserId === currentUserId) return false;
  return true;
}

/**
 * Backend blocks self-enable via 422 (anti-lockout safety),
 * but we also hide it in the UI for clarity.
 */
export function canEnable(targetUserId: string, currentUserId: string): boolean {
  if (targetUserId === currentUserId) return false;
  return true;
}

/**
 * BACKEND GAP (discovered Day 6-7 Step 0 diagnostic):
 * GET /dashboard/users returns UserItem without a `roles` field.
 * Only the PUT .../roles response includes roles (UserWithRolesItem).
 * Displaying current roles in the list would require N+1 calls — rejected.
 * TODO Sprint 8: backend should include `roles` in UserItem, or expose
 * GET /users/{id} with full detail.
 */
export const KNOWN_ROLE_NAMES = [
  'tenant_admin',
  'security_analyst',
  'read_only_auditor',
] as const;

export type KnownRoleName = typeof KNOWN_ROLE_NAMES[number];

/**
 * Admin cannot remove tenant_admin from themselves (backend returns 400).
 * Pre-validate client-side to show meaningful error before the API call.
 */
export function canUpdateRoles(
  targetUserId: string,
  currentUserId: string,
  newRoles: string[],
): boolean {
  if (targetUserId === currentUserId && !newRoles.includes('tenant_admin')) {
    return false;
  }
  return true;
}
