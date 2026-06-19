/**
 * Auth API — Day 1 bootstrap stubs.
 *
 * Day 1 scope: types + Zod schemas + the request function signature, so
 * LoginPage (Day 1) can be built against a stable contract. The full
 * wiring — TanStack Query mutation, error-state mapping to inline form
 * errors, redirect-after-login, "remember me" semantics — is Day 2-3
 * EPIC-AUTH per Sprint 7 PRD v2 sequencing.
 *
 * Contract assumed (existing v0.8.0-grid auth endpoints — NOT part of
 * Phase 0, already implemented in grid/):
 *   POST /api/v1/auth/login   { email, password } -> { access_token }
 *                              + sets HttpOnly refresh cookie
 *   POST /api/v1/auth/refresh (cookie-authenticated) -> { access_token }
 *   POST /api/v1/auth/logout  (cookie-authenticated) -> 204
 */

import { z } from 'zod';
import { apiClient } from './client';

export const LoginRequestSchema = z.object({
  email: z.string().email(),
  password: z.string().min(1),
});
export type LoginRequest = z.infer<typeof LoginRequestSchema>;

export const LoginResponseSchema = z.object({
  access_token: z.string().min(1),
});
export type LoginResponse = z.infer<typeof LoginResponseSchema>;

/**
 * POST /auth/login.
 *
 * NOT YET CALLED from LoginPage in Day 1 (form validation only).
 * Day 2-3 wires this into a useMutation with onSuccess ->
 * authStore.setAccessToken + queryClient.invalidateQueries(['me']) ->
 * navigate to role-based dashboard.
 */
export async function login(payload: LoginRequest): Promise<LoginResponse> {
  const response = await apiClient.post('/auth/login', payload);
  return LoginResponseSchema.parse(response.data);
}

/**
 * POST /auth/refresh — called by the app boot sequence (App.tsx) and by
 * the axios response interceptor (api/client.ts) on 401.
 */
export async function refresh(): Promise<LoginResponse> {
  const response = await apiClient.post('/auth/refresh');
  return LoginResponseSchema.parse(response.data);
}

/**
 * POST /auth/logout — Revokes the refresh cookie server-side.
 * logoutAll=true invalidates all sessions for the user (AC1.2.2).
 */
export async function logout(logoutAll = false): Promise<void> {
  await apiClient.post('/auth/logout', { logout_all: logoutAll });
}
