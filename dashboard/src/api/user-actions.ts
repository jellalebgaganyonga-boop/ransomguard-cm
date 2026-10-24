/**
 * USER ACTIONS — API layer (Sprint 8)
 *
 * Endpoint: GET /dashboard/user-actions
 * RBAC: tenant_admin + read_only_auditor
 *
 * User action log tracks WHO did WHAT on the dashboard:
 * login, logout, alert_status_change, command_issued, user_create, etc.
 */

import { z } from 'zod';
import { apiClient } from './client';

export const UserActionLogItemSchema = z.object({
  id: z.string(),
  actor_user_id: z.string(),
  actor_email: z.string().nullable().optional(),
  action_type: z.string(),
  target_type: z.string().nullable(),
  target_id: z.string().nullable(),
  details_json: z.record(z.string(), z.unknown()).nullable(),
  ip_address: z.string().nullable(),
  created_at: z.string(),
});
export type UserActionLogItem = z.infer<typeof UserActionLogItemSchema>;

export const PaginatedUserActionLogResponseSchema = z.object({
  items: z.array(UserActionLogItemSchema),
  total: z.number().int().nonnegative(),
  offset: z.number().int().nonnegative(),
  limit: z.number().int().positive(),
});
export type PaginatedUserActionLogResponse = z.infer<typeof PaginatedUserActionLogResponseSchema>;

export interface UserActionLogParams {
  action_type?: string | undefined;
  actor_user_id?: string | undefined;
  after?: string | undefined;
  before?: string | undefined;
  offset?: number | undefined;
  limit?: number | undefined;
}

export async function fetchUserActionLogs(
  params: UserActionLogParams = {},
): Promise<PaginatedUserActionLogResponse> {
  const clean = Object.fromEntries(
    Object.entries(params).filter(([, v]) => v !== undefined && v !== ''),
  );
  const res = await apiClient.get('/dashboard/user-actions', { params: clean });
  return PaginatedUserActionLogResponseSchema.parse(res.data);
}
