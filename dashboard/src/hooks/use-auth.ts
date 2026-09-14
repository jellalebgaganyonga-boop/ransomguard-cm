// ============================================================
// src/hooks/use-auth.ts
// EPIC-AUTH — US1.1, US1.2, US1.3
//
// GRID-SEC-001 RESOLVED (Sprint 8):
// Refresh token is now an HttpOnly cookie — no more in-memory storage.
// Login stores only the access_token; refresh cookie is set by the server.
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
      // AC1.1.1: POST /auth/login → access_token in memory, refresh in HttpOnly cookie
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

  return useMutation<void, Error, void>({
    mutationFn: async () => {
      try {
        // AC1.2.1: POST /auth/logout — server invalidates JTI + clears HttpOnly cookie
        await apiLogout();
      } catch {
        // AC1.2.4: logout always succeeds client-side even if API unreachable
      }
    },

    onSettled: () => {
      clearAccessToken();
      queryClient.clear();
      navigate('/auth/login', { replace: true });
    },
  });
}
