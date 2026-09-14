/**
 * Auth API.
 *
 * Contract (grid/ auth endpoints):
 *   POST /api/v1/auth/login   { email, password, tenant_code } -> { access_token }
 *   POST /api/v1/auth/refresh  (HttpOnly cookie sent automatically) -> { access_token }
 *   POST /api/v1/auth/logout  (Bearer-authenticated) -> 200
 *
 * GRID-SEC-001 (Sprint 8): Refresh token is now an HttpOnly cookie.
 * The browser sends it automatically with withCredentials: true.
 */

import { z } from 'zod';
import { apiClient } from './client';

export const LoginRequestSchema = z.object({
  tenant_code: z.string().min(1).max(50),
  email: z.string().email(),
  password: z.string().min(1),
});
export type LoginRequest = z.infer<typeof LoginRequestSchema>;

export const LoginResponseSchema = z.object({
  access_token: z.string().min(1),
  refresh_token: z.string().optional().default(''),
});
export type LoginResponse = z.infer<typeof LoginResponseSchema>;

/** POST /auth/login. */
export async function login(payload: LoginRequest): Promise<LoginResponse> {
  const response = await apiClient.post('/auth/login', payload);
  return LoginResponseSchema.parse(response.data);
}

/**
 * POST /auth/refresh — HttpOnly cookie is sent automatically by the browser.
 * No refresh_token in the body needed (GRID-SEC-001 resolved).
 */
export async function refresh(): Promise<LoginResponse> {
  const response = await apiClient.post('/auth/refresh', {});
  return LoginResponseSchema.parse(response.data);
}

/**
 * POST /auth/logout — Revokes the access token and clears the refresh cookie.
 */
export async function logout(): Promise<void> {
  await apiClient.post('/auth/logout');
}
