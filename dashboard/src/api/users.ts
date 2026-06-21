/**
 * EPIC-USERS — API layer
 *
 * Endpoints:
 *   GET    /dashboard/users                → PaginatedUserResponse
 *   POST   /dashboard/users                → UserItem (201)
 *   POST   /dashboard/users/{id}/disable   → UserItem
 *   POST   /dashboard/users/{id}/enable    → UserItem
 *   PUT    /dashboard/users/{id}/roles     → UserWithRolesItem
 *
 * Backend gaps noted here:
 *   - UserItem has NO roles field (GET /users list) — BACKEND GAP (GRID-SEC-002)
 *   - No GET /users/{id} single-user detail endpoint exists
 *   - disable_user has no server-side self-protection (GRID-SEC-002)
 *   - enable_user returns 422 on self-modify (correct)
 */

import { z } from 'zod';
import { apiClient } from './client';

// ── Schemas ────────────────────────────────────────────────

export const UserItemSchema = z.object({
  id: z.string(),
  email: z.string(),
  full_name: z.string(),
  is_active: z.boolean(),
  last_login_at: z.string().datetime({ offset: true }).nullable(),
});
export type UserItem = z.infer<typeof UserItemSchema>;

/** Only returned by PUT /users/{id}/roles */
export const UserWithRolesItemSchema = UserItemSchema.extend({
  roles: z.array(z.string()),
});
export type UserWithRolesItem = z.infer<typeof UserWithRolesItemSchema>;

export const PaginatedUserResponseSchema = z.object({
  items: z.array(UserItemSchema),
  total: z.number().int().nonnegative(),
  offset: z.number().int().nonnegative(),
  limit: z.number().int().positive(),
});
export type PaginatedUserResponse = z.infer<typeof PaginatedUserResponseSchema>;

// ── Request payloads ───────────────────────────────────────

export interface UserListParams {
  offset?: number;
  limit?: number;
}

export interface CreateUserPayload {
  email: string;
  full_name: string;
  password: string;
  roles?: string[];
}

export interface UpdateRolesPayload {
  role_names: string[];
}

// ── API functions ──────────────────────────────────────────

export async function fetchUserList(params: UserListParams = {}): Promise<PaginatedUserResponse> {
  const res = await apiClient.get('/dashboard/users', { params });
  return PaginatedUserResponseSchema.parse(res.data);
}

export async function createUser(payload: CreateUserPayload): Promise<UserItem> {
  const res = await apiClient.post('/dashboard/users', payload);
  return UserItemSchema.parse(res.data);
}

export async function disableUser(userId: string): Promise<UserItem> {
  const res = await apiClient.post(`/dashboard/users/${userId}/disable`);
  return UserItemSchema.parse(res.data);
}

export async function enableUser(userId: string): Promise<UserItem> {
  const res = await apiClient.post(`/dashboard/users/${userId}/enable`);
  return UserItemSchema.parse(res.data);
}

export async function updateUserRoles(
  userId: string,
  payload: UpdateRolesPayload,
): Promise<UserWithRolesItem> {
  const res = await apiClient.put(`/dashboard/users/${userId}/roles`, payload);
  return UserWithRolesItemSchema.parse(res.data);
}
