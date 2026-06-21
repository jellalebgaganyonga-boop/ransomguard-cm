/**
 * Axios HTTP client — ADR-FE-004 (Axios + interceptors), ADR-FE-009
 * (HttpOnly refresh cookie + in-memory access token).
 *
 * Responsibilities:
 * 1. Inject `Authorization: Bearer <access_token>` from authStore on
 *    every request (request interceptor).
 * 2. On 401, attempt a single deduplicated refresh via the HttpOnly
 *    refresh cookie (`POST /auth/refresh`, `withCredentials: true`),
 *    then retry the original request with the new access token.
 * 3. On refresh failure, clear the auth store and let the app boot
 *    sequence redirect to /auth/login.
 * 4. Send `X-Requested-With: XMLHttpRequest` on all requests as a
 *    defense-in-depth CSRF signal (STRIDE WT1.7) — combined with
 *    SameSite=Strict on the refresh cookie, this means a cross-site
 *    form submission cannot trigger an authenticated refresh.
 *
 * Refresh deduplication pattern:
 * If 5 queries 401 simultaneously (e.g., on token expiry while multiple
 * TanStack Query observers are active), we must NOT fire 5 concurrent
 * /auth/refresh calls (each would attempt to rotate the refresh token,
 * and the backend's reuse-detection would invalidate the session — see
 * grid AuthService). Instead, the FIRST 401 triggers a single in-flight
 * refresh promise; subsequent 401s await that same promise.
 */

import axios, {
  AxiosError,
  type AxiosInstance,
  type InternalAxiosRequestConfig,
} from 'axios';
import { getAccessToken, getRefreshToken, useAuthStore } from '@/stores/auth.store';

// ---------------------------------------------------------------------------
// Base instance
// ---------------------------------------------------------------------------

export const apiClient: AxiosInstance = axios.create({
  baseURL: `${import.meta.env.VITE_API_BASE_URL ?? ''}/api/v1`,
  withCredentials: true, // send/receive the HttpOnly refresh cookie
  headers: {
    'X-Requested-With': 'XMLHttpRequest', // STRIDE WT1.7 defense-in-depth
  },
  timeout: 15_000,
});

// Separate client for the refresh call itself — avoids interceptor recursion
// (the refresh request must never trigger another refresh attempt).
const refreshClient: AxiosInstance = axios.create({
  baseURL: `${import.meta.env.VITE_API_BASE_URL ?? ''}/api/v1`,
  withCredentials: true,
  headers: {
    'X-Requested-With': 'XMLHttpRequest',
  },
  timeout: 15_000,
});

// ---------------------------------------------------------------------------
// Request interceptor — inject access token
// ---------------------------------------------------------------------------

apiClient.interceptors.request.use((config: InternalAxiosRequestConfig) => {
  const token = getAccessToken();
  if (token) {
    config.headers.Authorization = `Bearer ${token}`;
  }
  return config;
});

// ---------------------------------------------------------------------------
// Response interceptor — 401 -> refresh -> retry, with deduplication
// ---------------------------------------------------------------------------

/**
 * Module-level singleton promise. While a refresh is in flight, all
 * concurrent 401s await this SAME promise instead of starting their own.
 * Reset to null once the refresh settles (success or failure).
 */
let refreshPromise: Promise<string> | null = null;

interface RetriableRequestConfig extends InternalAxiosRequestConfig {
  _retry?: boolean;
}

async function performRefresh(): Promise<string> {
  // See auth.store.ts SECURITY COMPROMISE note — refresh token is in-memory
  // because the backend does not issue HttpOnly cookies (GRID-SEC-001).
  const refreshToken = getRefreshToken();
  if (!refreshToken) {
    throw new Error('No refresh token available');
  }
  const response = await refreshClient.post<{ access_token: string; refresh_token: string }>(
    '/auth/refresh',
    { refresh_token: refreshToken }
  );
  const newToken = response.data.access_token;
  useAuthStore.getState().setAccessToken(newToken);
  if (response.data.refresh_token) {
    useAuthStore.getState().setRefreshToken(response.data.refresh_token);
  }
  return newToken;
}

apiClient.interceptors.response.use(
  (response) => response,
  async (error: AxiosError) => {
    const originalRequest = error.config as RetriableRequestConfig | undefined;

    // No config (e.g., request setup error) or no response at all
    // (network error) — nothing we can do, propagate.
    if (!originalRequest || !error.response) {
      return Promise.reject(error);
    }

    const status = error.response.status;

    // Don't attempt refresh on the refresh/login endpoints themselves,
    // and don't retry a request more than once.
    const isAuthEndpoint =
      originalRequest.url?.includes('/auth/refresh') ||
      originalRequest.url?.includes('/auth/login');

    if (status !== 401 || isAuthEndpoint || originalRequest._retry) {
      return Promise.reject(error);
    }

    originalRequest._retry = true;

    try {
      // Deduplicate concurrent refresh attempts
      if (!refreshPromise) {
        refreshPromise = performRefresh().finally(() => {
          refreshPromise = null;
        });
      }
      const newToken = await refreshPromise;

      // Retry the original request with the new token. `headers` is
      // always defined on InternalAxiosRequestConfig (axios guarantees
      // this), and AxiosHeaders supports index assignment.
      originalRequest.headers.Authorization = `Bearer ${newToken}`;
      return apiClient(originalRequest);
    } catch (refreshError) {
      // Refresh failed — clear all auth state.
      // The app's route guard (App.tsx) will redirect to /auth/login on
      // the next render because accessToken is now null.
      useAuthStore.getState().clearAccessToken();
      useAuthStore.getState().clearRefreshToken();
      return Promise.reject(refreshError);
    }
  }
);
