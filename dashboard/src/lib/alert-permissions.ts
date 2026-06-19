/**
 * Alert action permissions — pure function, isolated for unit testing.
 *
 * This is THE single source of truth for "which buttons does this
 * user see on this alert". Per AC3.2.5:
 *
 *   tenant_admin OR security_analyst + status="new"          → [Acknowledge]
 *   tenant_admin OR security_analyst + status="acknowledged" → [Close] [Mark FP]
 *   read_only_auditor                                        → no action buttons
 *   status="closed" (any role)                                → no action buttons (AC3.4.3)
 *
 * IMPORTANT: this is a CLIENT-SIDE UX CONVENIENCE ONLY. The backend
 * independently enforces every rule here (AC3.3.2-3, AC3.4.2 — 403/409
 * on illegal transitions). Hiding a button does not grant security;
 * removing this file would not create a vulnerability, only a UX bug
 * where users see buttons that 403 on click.
 */

import type { Role } from '@/api/me';
import type { AlertStatus } from '@/api/alerts';

export interface AlertActionPermissions {
  canAcknowledge: boolean;
  canClose: boolean;
}

const WRITE_ROLES: readonly Role[] = ['tenant_admin', 'security_analyst'] as const;

export function getAlertActionPermissions(
  role: Role,
  status: AlertStatus
): AlertActionPermissions {
  // AC3.3.3: read_only_auditor never gets write actions
  // AC3.4.3: closed alerts are read-only for every role
  if (!WRITE_ROLES.includes(role) || status === 'closed') {
    return { canAcknowledge: false, canClose: false };
  }

  // AC3.2.5 + AC3.3.2: Acknowledge only valid from "new"
  // AC3.2.5 + AC3.4.2: Close only valid from "acknowledged"
  return {
    canAcknowledge: status === 'new',
    canClose: status === 'acknowledged',
  };
}

/**
 * AC3.1.5: row-level action visibility in the alerts list.
 * Same underlying rule as detail view — kept as a thin alias so call
 * sites read intent-clearly (list vs detail) while sharing one source
 * of truth.
 */
export const getAlertRowActionPermissions = getAlertActionPermissions;
