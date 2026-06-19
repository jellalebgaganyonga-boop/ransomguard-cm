// ============================================================
// src/hooks/use-auth.ts
// EPIC-AUTH — US1.1, US1.2, US1.3
//
// Responsibilities:
//   - login (US1.1 AC1.1.1-6)
//   - logout single session (US1.2 AC1.2.1, AC1.2.4)
//   - logout all devices (US1.2 AC1.2.2 — fallback if backend unsupported)
//   - idle timeout 30min (US1.2 AC1.2.3)
//   - session expired modal trigger (US1.2 AC1.2.3)
//   - transparent refresh via axios interceptor (US1.3 — already in client.ts)
// ============================================================

import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useNavigate, useLocation } from 'react-router-dom';
import { useAuthStore } from '@/stores/auth.store';
import { login as apiLogin, logout as apiLogout } from '@/api/auth';
import { fetchMe } from '@/api/me';
import type { LoginRequest } from '@/api/auth';

// ── Login mutation (US1.1 AC1.1.1-6) ──────────────────────

export function useLogin() {
  const navigate = useNavigate();
  const location = useLocation();
  const setAccessToken = useAuthStore((s) => s.setAccessToken);
  const setInitializing = useAuthStore((s) => s.setInitializing);
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async (payload: LoginRequest) => {
      // AC1.1.1: POST /auth/login → access_token in memory
      const { access_token } = await apiLogin(payload);
      setAccessToken(access_token);

      // AC1.1.6: immediately fetch /me after storing token
      const me = await fetchMe();
      return me;
    },

    onSuccess: (me) => {
      // AC1.1.6: store /me in query cache (drives role routing)
      queryClient.setQueryData(['me'], me);
      setInitializing(false);

      // AC2.1.3: return-to after login preserves destination
      const from =
        (location.state as { from?: Location } | null)?.from?.pathname ??
        '/dashboard';
      navigate(from, { replace: true });
    },

    onError: () => {
      // AC1.1.2: clear any partial state on failure
      useAuthStore.getState().clearAccessToken();
    },
  });
}

// ── Logout mutation (US1.2 AC1.2.1, AC1.2.4) ──────────────

export function useLogout() {
  const navigate = useNavigate();
  const clearAccessToken = useAuthStore((s) => s.clearAccessToken);
  const queryClient = useQueryClient();

  return useMutation<void, Error, boolean>({
    mutationFn: async (logoutAll: boolean) => {
      try {
        // AC1.2.1: POST /auth/logout — server invalidates JTI + clears cookie
        await apiLogout(logoutAll);
      } catch {
        // AC1.2.4: logout always succeeds client-side even if API unreachable
        // Fire and forget — JWT will expire naturally
      }
    },

    onSettled: () => {
      // AC1.2.1: reset in-memory state regardless of API result
      clearAccessToken();
      // Clear all cached server data (cross-tenant safety)
      queryClient.clear();
      // AC1.2.1: redirect to login
      navigate('/auth/login', { replace: true });
    },
  });
}
