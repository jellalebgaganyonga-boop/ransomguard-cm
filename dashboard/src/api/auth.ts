/**
 * Auth API.
 *
 * Contract (grid/ auth endpoints):
 *   POST /api/v1/auth/login   { email, password, tenant_code } -> { access_token, refresh_token }
 *   POST /api/v1/auth/refresh { refresh_token } -> { access_token, refresh_token }
 *   POST /api/v1/auth/logout  (Bearer-authenticated) -> 204
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
  refresh_token: z.string().min(1),
});
export type LoginResponse = z.infer<typeof LoginResponseSchema>;

/** POST /auth/login. */
export async function login(payload: LoginRequest): Promise<LoginResponse> {
  const response = await apiClient.post('/auth/login', payload);
  return LoginResponseSchema.parse(response.data);
}

/**
 * POST /auth/refresh — called by the axios response interceptor (api/client.ts)
 * on 401. Sends the in-memory refresh token in the request body.
 * See auth.store.ts SECURITY COMPROMISE note.
 */
export async function refresh(refreshToken: string): Promise<LoginResponse> {
  const response = await apiClient.post('/auth/refresh', { refresh_token: refreshToken });
  return LoginResponseSchema.parse(response.data);
}

/**
 * POST /auth/logout — Revokes the refresh cookie server-side.
 * logoutAll=true invalidates all sessions for the user (AC1.2.2).
 */
export async function logout(logoutAll = false): Promise<void> {
  await apiClient.post('/auth/logout', { logout_all: logoutAll });
}
