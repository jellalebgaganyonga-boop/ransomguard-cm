/**
 * Authentication store — Zustand (client state) per ADR-FE-003.
 *
 * Security model per ADR-FE-009 (STRIDE WT1.3, WT1.7):
 * - Access token: stored HERE, in memory only. NEVER persisted to
 *   localStorage/sessionStorage (XSS exfiltration risk).
 *
 * ⚠️  SECURITY COMPROMISE — GRID-SEC-001:
 * The ideal architecture stores the refresh token in an HttpOnly Secure
 * SameSite=Strict cookie (inaccessible to JavaScript). However, the
 * current backend (grid/) returns the refresh token in the JSON response
 * body and does NOT set an HttpOnly cookie. Until the backend is updated
 * to issue HttpOnly cookies (tracked as GRID-SEC-001), we store the
 * refresh token in-memory (same XSS surface as the access token, but
 * NOT persisted to localStorage/sessionStorage). This is intentional and
 * reviewed — do NOT "fix" this by writing it to localStorage.
 *
 * - On page reload, BOTH tokens are lost (expected). The user must
 *   re-authenticate. Silent refresh across reloads is not available
 *   until GRID-SEC-001 is resolved.
 *
 * tenant_id and roles are derived from /dashboard/me (see use-me.ts),
 * NOT decoded from the JWT client-side.
 */

import { create } from 'zustand';

// Note: the canonical `Role` type lives in `@/api/me` (derived from the
// Zod schema validated against GET /dashboard/me). It is intentionally
// NOT redefined here — this store only holds tokens and boot state;
// role/tenant data flows through useMe(), never through this store.

interface AuthState {
  /** In-memory access token. Null = not authenticated (or pending refresh). */
  accessToken: string | null;

  /**
   * In-memory refresh token.
   * See SECURITY COMPROMISE note above — stored here only because the
   * backend does not issue HttpOnly cookies (GRID-SEC-001).
   * NEVER persist this to localStorage/sessionStorage.
   */
  refreshToken: string | null;

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

  /** Store the refresh token in-memory (see SECURITY COMPROMISE note). */
  setRefreshToken: (token: string) => void;

  /** Clear on logout or after a failed refresh. */
  clearRefreshToken: () => void;

  /** Toggled by the app boot sequence (see App.tsx useEffect). */
  setInitializing: (value: boolean) => void;
}

export const useAuthStore = create<AuthState>((set) => ({
  accessToken: null,
  refreshToken: null,
  isInitializing: true,

  setAccessToken: (token) => set({ accessToken: token }),
  clearAccessToken: () => set({ accessToken: null }),

  setRefreshToken: (token) => set({ refreshToken: token }),
  clearRefreshToken: () => set({ refreshToken: null }),

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

/** Non-reactive getter for the refresh token — see SECURITY COMPROMISE note. */
export function getRefreshToken(): string | null {
  return useAuthStore.getState().refreshToken;
}
