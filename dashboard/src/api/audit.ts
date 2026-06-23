/**
 * EPIC-AUDIT — API layer
 *
 * SCOPE: Ed25519 agent audit chain (AuditLog model), NOT user action log.
 * See audit-log-page.tsx for full scope note.
 *
 * Endpoint: GET /dashboard/audit-logs
 * RBAC: tenant_admin + read_only_auditor (analyst → 403)
 *
 * Response: PaginatedAuditLogResponse
 *   items: AuditLogItem[]
 *   total: int
 *   offset: int
 *   limit: int
 *
 * Filter params (exact backend names):
 *   agent_id        — string  | undefined
 *   received_after  — ISO 8601 datetime string | undefined
 *   received_before — ISO 8601 datetime string | undefined
 *   sequence_after  — integer | undefined
 *   offset          — integer (default: 0)
 *   limit           — integer (default: 100, max: 200)
 *
 * No CSV export endpoint — export is frontend-only (see audit-log-page.tsx).
 */

import { z } from 'zod';
import { apiClient } from './client';

// ── Schemas ─────────────────────────────────────────────────

export const AuditLogItemSchema = z.object({
  id: z.string(),
  agent_id: z.string(),
  sequence_number: z.number().int().nonnegative(),
  signing_key_id: z.string(),
  received_at: z.string(),
});
export type AuditLogItem = z.infer<typeof AuditLogItemSchema>;

export const PaginatedAuditLogResponseSchema = z.object({
  items: z.array(AuditLogItemSchema),
  total: z.number().int().nonnegative(),
  offset: z.number().int().nonnegative(),
  limit: z.number().int().positive(),
});
export type PaginatedAuditLogResponse = z.infer<typeof PaginatedAuditLogResponseSchema>;

// ── Request params ───────────────────────────────────────────

export interface AuditLogParams {
  agent_id?: string;
  received_after?: string;
  received_before?: string;
  sequence_after?: number;
  offset?: number;
  limit?: number;
}

// ── API function ─────────────────────────────────────────────

export async function fetchAuditLogs(
  params: AuditLogParams = {},
): Promise<PaginatedAuditLogResponse> {
  // Strip undefined values so axios doesn't send empty query params
  const clean = Object.fromEntries(
    Object.entries(params).filter(([, v]) => v !== undefined && v !== ''),
  );
  const res = await apiClient.get('/dashboard/audit-logs', { params: clean });
  return PaginatedAuditLogResponseSchema.parse(res.data);
}
