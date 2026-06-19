// ============================================================
// src/App.tsx  — REMPLACEMENT COMPLET
// Boot sequence: refresh → /me → role routing (AC1.3.4)
// Routes: auth + protected (avec placeholders Day 4-8)
// ============================================================

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
import { AlertsListPage } from '@/pages/alerts/alerts-list-page';
import { AlertDetailPage } from '@/pages/alerts/alert-detail-page';

/**
 * Boot sequence (AC1.3.4):
 * 1. App mounts → attempt POST /auth/refresh (HttpOnly cookie)
 * 2a. Success → setAccessToken → isInitializing=false
 *     → ProtectedRoute renders → calls useMe() → role-based routing
 * 2b. Failure → isInitializing=false (no token)
 *     → ProtectedRoute → Navigate to /auth/login
 *
 * This runs ONCE on mount. Mid-session 401 refresh is handled by
 * the Axios interceptor in src/api/client.ts (AC1.3.1-2 + AC1.3.5).
 */
export function App() {
  const setAccessToken = useAuthStore((s) => s.setAccessToken);
  const setInitializing = useAuthStore((s) => s.setInitializing);

  useEffect(() => {
    let cancelled = false;

    async function bootRefresh() {
      try {
        const { access_token } = await refresh();
        if (!cancelled) setAccessToken(access_token);
      } catch {
        // No valid refresh cookie — expected on first visit.
        // accessToken stays null → ProtectedRoute redirects to login.
      } finally {
        if (!cancelled) setInitializing(false);
      }
    }

    void bootRefresh();
    return () => { cancelled = true; };
  }, [setAccessToken, setInitializing]);

  return (
    <BrowserRouter>
      <Routes>
        {/* ── Public ── */}
        <Route element={<AuthLayout />}>
          <Route path="/auth/login" element={<LoginPage />} />
        </Route>

        {/* ── Protected (AppShell + RBAC + idle timeout) ── */}
        <Route element={<ProtectedRoute />}>
          {/* Day 2-3: done */}
          <Route path="/dashboard" element={<DashboardPage />} />

          {/* Day 4-5: EPIC-ALERTS */}
          <Route path="/alerts" element={<AlertsListPage />} />
          <Route path="/alerts/:id" element={<AlertDetailPage />} />

          {/* Day 6-7: EPIC-AGENTS */}
          <Route
            path="/agents"
            element={
              <PlaceholderPage
                titleKey="nav.agents"
                dayLabel="Sprint 7 — Day 6-7 (EPIC-AGENTS)"
              />
            }
          />
          <Route
            path="/agents/:id"
            element={
              <PlaceholderPage
                titleKey="nav.agents"
                dayLabel="Sprint 7 — Day 6-7 (Agent Detail)"
              />
            }
          />

          {/* Day 6-7: EPIC-USERS */}
          <Route
            path="/users"
            element={
              <PlaceholderPage
                titleKey="nav.users"
                dayLabel="Sprint 7 — Day 6-7 (EPIC-USERS)"
              />
            }
          />

          {/* Day 8: EPIC-AUDIT */}
          <Route
            path="/audit"
            element={
              <PlaceholderPage
                titleKey="nav.audit"
                dayLabel="Sprint 7 — Day 8 (EPIC-AUDIT)"
              />
            }
          />

          {/* Settings: deferred */}
          <Route
            path="/settings"
            element={
              <PlaceholderPage
                titleKey="nav.settings"
                dayLabel="Hors périmètre Sprint 7"
              />
            }
          />
        </Route>

        {/* ── Root redirect ── */}
        <Route path="/" element={<Navigate to="/dashboard" replace />} />

        {/* ── 404 ── */}
        <Route path="*" element={<NotFoundPage />} />
      </Routes>
    </BrowserRouter>
  );
}
