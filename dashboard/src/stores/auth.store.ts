/**
 * Authentication store — Zustand (client state) per ADR-FE-003.
 *
 * Security model per ADR-FE-009 (STRIDE WT1.3, WT1.7):
 * - Access token: stored HERE, in memory only. NEVER persisted to
 *   localStorage/sessionStorage (XSS exfiltration risk).
 *
 * GRID-SEC-001 RESOLVED (Sprint 8):
 * The refresh token is now stored as an HttpOnly Secure SameSite=Strict
 * cookie by the backend. It is NEVER accessible to JavaScript.
 * The browser automatically sends it with requests to /api/v1/auth/*.
 *
 * On page reload, the access token is lost (expected). The app boot
 * sequence calls /auth/refresh (cookie sent automatically), and if the
 * refresh cookie is still valid, the user stays logged in silently.
 *
 * tenant_id and roles are derived from /dashboard/me (see use-me.ts),
 * NOT decoded from the JWT client-side.
 */

import { create } from 'zustand';

interface AuthState {
  /** In-memory access token. Null = not authenticated (or pending refresh). */
  accessToken: string | null;

  /**
   * True while the app boot sequence is attempting silent refresh.
   * Used to show a full-page loader instead of flashing the login screen
   * on every page reload.
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
 */
export function getAccessToken(): string | null {
  return useAuthStore.getState().accessToken;
}

/**
 * @deprecated GRID-SEC-001 resolved — refresh token is now in HttpOnly cookie.
 * Kept for backward compatibility with any code that calls it (returns null).
 */
export function getRefreshToken(): string | null {
  return null;
}
