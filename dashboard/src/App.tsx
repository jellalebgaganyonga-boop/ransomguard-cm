// ============================================================
// src/App.tsx
// Boot sequence: refresh → /me → role routing (AC1.3.4)
// Routes: auth + protected
//
// GRID-SEC-001 RESOLVED (Sprint 8):
// Refresh token is in HttpOnly cookie. On page reload, the boot
// sequence calls POST /auth/refresh — the browser sends the cookie
// automatically. If valid, the user stays logged in silently.
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
import { AlertsListPage } from '@/pages/alerts/alerts-list-page';
import { AlertDetailPage } from '@/pages/alerts/alert-detail-page';
import { AgentsListPage } from '@/pages/agents/agents-list-page';
import { AgentDetailPage } from '@/pages/agents/agent-detail-page';
import { UsersListPage } from '@/pages/users/users-list-page';
import { AuditLogPage } from '@/pages/audit/audit-log-page';
import { UserActionsPage } from '@/pages/audit/user-actions-page';
import { NotificationPreferencesPage } from '@/pages/settings/notification-preferences-page';

/**
 * Boot sequence (AC1.3.4):
 * 1. App mounts → attempt POST /auth/refresh (HttpOnly cookie sent by browser)
 * 2a. Success → setAccessToken → isInitializing=false → user stays logged in
 * 2b. Failure → isInitializing=false → redirect to /auth/login
 */
export function App() {
  const setAccessToken = useAuthStore((s) => s.setAccessToken);
  const setInitializing = useAuthStore((s) => s.setInitializing);

  useEffect(() => {
    let cancelled = false;

    async function bootRefresh() {
      try {
        // HttpOnly cookie is sent automatically by the browser
        const { access_token } = await refresh();
        if (!cancelled) {
          setAccessToken(access_token);
        }
      } catch {
        // No valid refresh cookie — user must re-authenticate.
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
          <Route path="/dashboard" element={<DashboardPage />} />

          {/* EPIC-ALERTS */}
          <Route path="/alerts" element={<AlertsListPage />} />
          <Route path="/alerts/:id" element={<AlertDetailPage />} />

          {/* EPIC-AGENTS */}
          <Route path="/agents" element={<AgentsListPage />} />
          <Route path="/agents/:id" element={<AgentDetailPage />} />

          {/* EPIC-USERS */}
          <Route path="/users" element={<UsersListPage />} />

          {/* EPIC-AUDIT */}
          <Route path="/audit" element={<AuditLogPage />} />
          <Route path="/audit/user-actions" element={<UserActionsPage />} />

          {/* Settings */}
          <Route path="/settings/notifications" element={<NotificationPreferencesPage />} />
          <Route path="/settings" element={<Navigate to="/settings/notifications" replace />} />
        </Route>

        {/* ── Root redirect ── */}
        <Route path="/" element={<Navigate to="/dashboard" replace />} />

        {/* ── 404 ── */}
        <Route path="*" element={<NotFoundPage />} />
      </Routes>
    </BrowserRouter>
  );
}
