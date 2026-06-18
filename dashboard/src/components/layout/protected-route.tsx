import { Navigate, Outlet, useLocation } from 'react-router-dom';
import { useAuthStore } from '@/stores/auth.store';
import { useMe, resolvePrimaryRole } from '@/hooks/use-me';
import { NAV_ITEMS } from '@/config/navigation';
import { AppShellLayout } from './app-shell-layout';
import { ForbiddenPage } from '@/pages/forbidden-page';

/**
 * ProtectedRoute — gates access to AppShellLayout + nested routes.
 *
 * Order of checks:
 * 1. Boot sequence still running (isInitializing) -> full-page loader.
 *    (App.tsx's boot effect attempts POST /auth/refresh via the cookie
 *    before this ever renders — see App.tsx.)
 * 2. No access token (refresh failed or never logged in) ->
 *    redirect to /auth/login, preserving the attempted location so
 *    Day 2-3 EPIC-AUTH can redirect back post-login.
 * 3. useMe() loading -> full-page loader.
 * 4. useMe() error (401 — token invalid despite being present, e.g.
 *    revoked mid-session) -> redirect to /auth/login.
 * 5. useMe() success -> resolve role -> RBAC check against NAV_ITEMS
 *    for the current path. If allowed, render AppShellLayout + <Outlet />.
 *    If not, render AppShellLayout + ForbiddenPage (client-side UX only;
 *    server remains authoritative per STRIDE WT4.2/WT4.5).
 */

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

export function ProtectedRoute() {
  const location = useLocation();
  const accessToken = useAuthStore((s) => s.accessToken);
  const isInitializing = useAuthStore((s) => s.isInitializing);

  if (isInitializing) {
    return <FullPageLoader />;
  }

  if (!accessToken) {
    return <Navigate to="/auth/login" state={{ from: location }} replace />;
  }

  return <AuthenticatedRoute />;
}

function AuthenticatedRoute() {
  const location = useLocation();
  const { data: me, isLoading, isError } = useMe();

  if (isLoading) {
    return <FullPageLoader />;
  }

  if (isError || !me) {
    return <Navigate to="/auth/login" state={{ from: location }} replace />;
  }

  const role = resolvePrimaryRole(me.roles);

  // Find the nav item matching the current top-level path segment, e.g.
  // "/users/123" -> matches NAV_ITEMS entry with to: "/users".
  const matchedItem = NAV_ITEMS.find((item) => location.pathname.startsWith(item.to));

  if (matchedItem && !matchedItem.roles.includes(role)) {
    return (
      <AppShellLayout me={me}>
        <ForbiddenPage />
      </AppShellLayout>
    );
  }

  return (
    <AppShellLayout me={me}>
      <Outlet />
    </AppShellLayout>
  );
}
