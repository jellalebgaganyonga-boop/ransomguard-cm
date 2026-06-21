/**
 * GET /dashboard/me — typed client + Zod schema.
 *
 * Per ADR-FE-005 (React Hook Form + Zod): Zod schemas serve double duty
 * as runtime validators AND TypeScript types (z.infer). Validating the
 * server response defends against backend/frontend contract drift
 * (Phase 0 schema — see phase0_backend/schemas/me.py).
 */

import { z } from 'zod';
import { apiClient } from './client';

export const RoleSchema = z.enum([
  'tenant_admin',
  'security_analyst',
  'read_only_auditor',
]);
export type Role = z.infer<typeof RoleSchema>;

export const TenantContextSchema = z.object({
  id: z.string().uuid(),
  name: z.string().min(1).max(255),
  status: z.enum(['active', 'suspended', 'deleted']),
});

export const UserPreferencesSchema = z.object({
  language: z.enum(['fr', 'en']),
  timezone: z.string().min(1),
});

export const MeResponseSchema = z.object({
  user_id: z.string().uuid(),
  email: z.string().email(),
  full_name: z.string().min(1).max(255),
  is_active: z.boolean(),
  tenant: TenantContextSchema,
  // Backend returns array of role strings; we validate each is a known
  // role but allow unknown roles to pass through as plain strings so
  // an unexpected backend addition doesn't hard-crash the frontend
  // (graceful degradation -> falls back to read-only dashboard, see
  // use-me.ts `primaryRole` resolution).
  roles: z.array(z.string()).min(1),
  last_login_at: z.string().datetime({ offset: true }).nullable(),
  preferences: UserPreferencesSchema,
});

export type MeResponse = z.infer<typeof MeResponseSchema>;

export async function fetchMe(): Promise<MeResponse> {
  const response = await apiClient.get('/dashboard/me');
  return MeResponseSchema.parse(response.data);
}
