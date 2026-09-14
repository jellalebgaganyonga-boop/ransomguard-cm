// ============================================================
// src/components/layout/protected-route.tsx  — REMPLACEMENT
// US1.2 AC1.2.1-4 + US1.3 AC1.3.3-5 + US2.1 AC2.1.3
//
// - Boot refresh → /me → role routing (AC1.3.4)
// - Idle timeout 30min → session modal → logout (AC1.2.3)
// - RBAC route guard → ForbiddenPage (AC2.1)
// - Refresh failure → login redirect (AC1.3.3)
// ============================================================

import { useState, useCallback } from 'react';
import { Navigate, Outlet, useLocation } from 'react-router-dom';
import { useAuthStore } from '@/stores/auth.store';
import { useMe, resolvePrimaryRole } from '@/hooks/use-me';
import { useIdleTimeout } from '@/hooks/use-idle-timeout';
import { useLogout } from '@/hooks/use-auth';
import { SessionExpiredModal } from '@/components/auth/session-expired-modal';
import { AppShellLayout } from './app-shell-layout';
import { NAV_ITEMS } from '@/config/navigation';

// ── Full-page loader ───────────────────────────────────────

function FullPageLoader() {
  return (
    <div className="flex h-screen items-center justify-center bg-canvas">
      <div
        className="size-8 animate-spin rounded-full border-4 border-border-subtle border-t-primary"
        role="status"
        aria-label="Chargement"
      />
    </div>
  );
}

// ── Unauthenticated gate ───────────────────────────────────

export function ProtectedRoute() {
  const location = useLocation();
  const accessToken = useAuthStore((s) => s.accessToken);
  const isInitializing = useAuthStore((s) => s.isInitializing);

  if (isInitializing) return <FullPageLoader />;

  if (!accessToken) {
    // AC2.1.3: preserve destination for post-login redirect
    return <Navigate to="/auth/login" state={{ from: location }} replace />;
  }

  return <AuthenticatedRoute />;
}

// ── Authenticated route with idle timeout + RBAC ──────────

function AuthenticatedRoute() {
  const location = useLocation();
  const { data: me, isLoading, isError } = useMe();
  const logoutMutation = useLogout();
  const [showExpiredModal, setShowExpiredModal] = useState(false);

  // AC1.2.3: idle callback — show modal then logout
  const handleIdle = useCallback(() => {
    setShowExpiredModal(true);
  }, []);

  // Sprint 7: warning banner deferred — no-op to keep stable reference
  const handleWarning = useCallback(() => {}, []);

  const handleExpiredDismiss = useCallback(() => {
    setShowExpiredModal(false);
    logoutMutation.mutate();
  }, [logoutMutation]);

  // AC1.2.3: mounted only when authenticated (me is loaded)
  useIdleTimeout({
    enabled: !!me,
    onWarning: handleWarning,
    onIdle: handleIdle,
  });

  if (isLoading) return <FullPageLoader />;

  // AC1.3.3: refresh failure → login
  if (isError || !me) {
    return <Navigate to="/auth/login" state={{ from: location }} replace />;
  }

  const role = resolvePrimaryRole(me.roles);

  // RBAC client-side guard (AC2.1 + AC5.1.2 + AC6.1.2)
  const matchedItem = NAV_ITEMS.find((item) =>
    location.pathname.startsWith(item.to)
  );
  const isRoleForbidden = matchedItem && !matchedItem.roles.includes(role);

  // Redirect to /dashboard when the current route is not allowed for this role
  if (isRoleForbidden) {
    return <Navigate to="/dashboard" replace />;
  }

  return (
    <>
      {/* AC1.2.3: session expired overlay */}
      {showExpiredModal && (
        <SessionExpiredModal onDismiss={handleExpiredDismiss} />
      )}

      <AppShellLayout me={me}>
        <Outlet />
      </AppShellLayout>
    </>
  );
}
