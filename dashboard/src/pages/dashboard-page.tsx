// ============================================================
// src/pages/dashboard-page.tsx  — REMPLACEMENT
// US2.1 — Role-based landing routing
//
// AC2.1.1: route /dashboard → role-specific component
// AC2.1.2: unknown role → ReadOnlyDashboard (least privilege)
// AC2.1.3: unauthenticated → /auth/login (handled by ProtectedRoute)
// ============================================================

import { useMe, resolvePrimaryRole } from '@/hooks/use-me';
import { ExecutiveDashboard } from './dashboard/executive-dashboard';
import { OperationalDashboard } from './dashboard/operational-dashboard';
import { ReadOnlyDashboard } from './dashboard/read-only-dashboard';

export function DashboardPage() {
  const { data: me } = useMe();

  // ProtectedRoute guarantees me is loaded before this renders
  if (!me) return null;

  // AC2.1.1: role-based component selection
  const role = resolvePrimaryRole(me.roles);

  switch (role) {
    case 'tenant_admin':
      return <ExecutiveDashboard />;
    case 'security_analyst':
      return <OperationalDashboard />;
    case 'read_only_auditor':
      return <ReadOnlyDashboard />;
    default:
      // AC2.1.2: unknown role → least-privilege fallback + console error
      console.error('[DashboardPage] Unrecognized role:', role, '— falling back to ReadOnlyDashboard');
      return <ReadOnlyDashboard />;
  }
}
