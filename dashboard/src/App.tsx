import { useEffect } from 'react';
import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom';
import { useAuthStore } from '@/stores/auth.store';
import { refresh } from '@/api/auth';
import { AuthLayout } from '@/components/layout/auth-layout';
import { ProtectedRoute } from '@/components/layout/protected-route';
import { LoginPage } from '@/pages/login-page';
import { DashboardPage } from '@/pages/dashboard-page';
import { NotFoundPage } from '@/pages/not-found-page';
import { PlaceholderPage } from '@/pages/placeholder-page';

/**
 * App — root component. Router + boot sequence.
 *
 * Boot sequence (per ADR-FE-009): on mount, attempt a silent token
 * refresh via the HttpOnly refresh cookie (POST /auth/refresh,
 * withCredentials). This runs ONCE before any protected route renders:
 * - Success: in-memory access token is populated -> protected routes
 *   render normally (ProtectedRoute sees accessToken !== null).
 * - Failure (401 — no valid refresh cookie, e.g. first visit or
 *   expired session): isInitializing becomes false with accessToken
 *   still null -> ProtectedRoute redirects to /auth/login.
 *
 * This effect runs exactly once (empty dependency array) — it is NOT
 * the same refresh-on-401 logic in api/client.ts's response
 * interceptor (that handles mid-session token expiry during normal
 * use; this handles the initial page-load state).
 */
export function App() {
  const setAccessToken = useAuthStore((s) => s.setAccessToken);
  const setInitializing = useAuthStore((s) => s.setInitializing);

  useEffect(() => {
    let cancelled = false;

    async function bootRefresh() {
      try {
        const { access_token } = await refresh();
        if (!cancelled) {
          setAccessToken(access_token);
        }
      } catch {
        // No valid refresh cookie — expected on first visit / logged out.
        // accessToken remains null; ProtectedRoute will redirect to login.
      } finally {
        if (!cancelled) {
          setInitializing(false);
        }
      }
    }

    void bootRefresh();

    return () => {
      cancelled = true;
    };
  }, [setAccessToken, setInitializing]);

  return (
    <BrowserRouter>
      <Routes>
        {/* Public routes */}
        <Route element={<AuthLayout />}>
          <Route path="/auth/login" element={<LoginPage />} />
        </Route>

        {/* Protected routes — AppShellLayout + RBAC via ProtectedRoute */}
        <Route element={<ProtectedRoute />}>
          <Route path="/dashboard" element={<DashboardPage />} />
          <Route
            path="/alerts"
            element={<PlaceholderPage titleKey="nav.alerts" dayLabel="Sprint 7 — Day 4-5 (EPIC-ALERTS)" />}
          />
          <Route
            path="/agents"
            element={<PlaceholderPage titleKey="nav.agents" dayLabel="Sprint 7 — Day 6-7 (EPIC-AGENTS)" />}
          />
          <Route
            path="/users"
            element={<PlaceholderPage titleKey="nav.users" dayLabel="Sprint 7 — Day 6-7 (EPIC-USERS)" />}
          />
          <Route
            path="/audit"
            element={<PlaceholderPage titleKey="nav.audit" dayLabel="Sprint 7 — Day 8 (EPIC-AUDIT)" />}
          />
          <Route
            path="/settings"
            element={<PlaceholderPage titleKey="nav.settings" dayLabel="Reporté (hors périmètre Sprint 7)" />}
          />
        </Route>

        {/* Root redirect */}
        <Route path="/" element={<Navigate to="/dashboard" replace />} />

        {/* 404 */}
        <Route path="*" element={<NotFoundPage />} />
      </Routes>
    </BrowserRouter>
  );
}
