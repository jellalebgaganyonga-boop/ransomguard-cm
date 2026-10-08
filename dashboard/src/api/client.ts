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
 * GRID-SEC-001 RESOLVED (Sprint 8):
 * Refresh token is now in HttpOnly Secure SameSite=Strict cookie.
 * The browser sends it automatically with withCredentials: true.
 * No refresh_token in request body or JavaScript memory.
 */

import axios,
{
  AxiosError,
  type AxiosInstance,
  type InternalAxiosRequestConfig,
} from 'axios';
import { getAccessToken, useAuthStore } from '@/stores/auth.store';

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
  // GRID-SEC-001 resolved: refresh token is in HttpOnly cookie,
  // sent automatically by the browser with withCredentials: true.
  // No token in body or JavaScript memory.
  const response = await refreshClient.post<{ access_token: string }>(
    '/auth/refresh',
    {}
  );
  const newToken = response.data.access_token;
  useAuthStore.getState().setAccessToken(newToken);
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

      originalRequest.headers.Authorization = `Bearer ${newToken}`;
      return apiClient(originalRequest);
    } catch (refreshError) {
      // Refresh failed — clear all auth state.
      useAuthStore.getState().clearAccessToken();
      return Promise.reject(refreshError);
    }
  }
);
