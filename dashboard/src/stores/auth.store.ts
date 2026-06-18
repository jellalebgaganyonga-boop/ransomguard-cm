/**
 * Authentication store — Zustand (client state) per ADR-FE-003.
 *
 * Security model per ADR-FE-009 (STRIDE WT1.3, WT1.7):
 * - Access token: stored HERE, in memory only. NEVER persisted to
 *   localStorage/sessionStorage (XSS exfiltration risk).
 * - Refresh token: stored in HttpOnly Secure SameSite=Strict cookie,
 *   set by the backend on /auth/login and /auth/refresh. This store
 *   NEVER sees the refresh token — it is inaccessible to JavaScript
 *   by design.
 * - On page reload, accessToken is lost (expected). The app boot
 *   sequence (see App.tsx) calls /auth/refresh (cookie-authenticated)
 *   to obtain a new access token before rendering protected routes.
 *
 * tenant_id and roles are derived from /dashboard/me (see use-me.ts),
 * NOT decoded from the JWT client-side — the backend is the source of
 * truth, and decoding JWTs client-side for authorization decisions is
 * an anti-pattern (the JWT is opaque to the frontend beyond the
 * Authorization header).
 */

import { create } from 'zustand';

// Note: the canonical `Role` type lives in `@/api/me` (derived from the
// Zod schema validated against GET /dashboard/me). It is intentionally
// NOT redefined here — this store only holds the opaque access token and
// boot state; role/tenant data flows through useMe(), never through this
// store, to avoid two sources of truth.

interface AuthState {
  /** In-memory access token. Null = not authenticated (or pending refresh). */
  accessToken: string | null;

  /**
   * True while the app boot sequence is attempting silent refresh via
   * the HttpOnly cookie. Used to show a full-page loader instead of
   * flashing the login screen on every page reload.
   */
  isInitializing: boolean;

  /** Set after a successful login or token refresh. */
  setAccessToken: (token: string) => void;

  /** Clear on logout or unrecoverable 401 (refresh also failed). */
  clearAccessToken: () => void;

  /** Toggled by the app boot sequence (see App.tsx useEffect). */
  setInitializing: (value: boolean) => void;
}

export const useAuthStore = create<AuthState>((set) => ({
  accessToken: null,
  isInitializing: true,

  setAccessToken: (token) => set({ accessToken: token }),

  clearAccessToken: () => set({ accessToken: null }),

  setInitializing: (value) => set({ isInitializing: value }),
}));

/**
 * Non-reactive getter for use in non-component contexts (e.g., Axios
 * interceptors, which run outside React's render cycle).
 *
 * Per Zustand docs: `getState()` reads the current state without
 * subscribing — safe to call from interceptors.
 */
export function getAccessToken(): string | null {
  return useAuthStore.getState().accessToken;
}
