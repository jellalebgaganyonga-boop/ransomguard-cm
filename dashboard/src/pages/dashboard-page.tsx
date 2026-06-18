import { useTranslation } from 'react-i18next';
import { useMe, resolvePrimaryRole } from '@/hooks/use-me';
import { KPICard } from '@/components/ui/kpi-card';

/**
 * DashboardPage — Day 1 bootstrap stub.
 *
 * Renders a minimal role-aware placeholder so routing + RBAC landing
 * logic can be validated end-to-end before the full dashboards
 * (Hi-Fi Mockup 1 — Operational, Low-Fi Wireframes 2/4 — Executive /
 * Read-Only) are built in Day 2-3 EPIC-DASHBOARD.
 *
 * Confirms: useMe() resolves, resolvePrimaryRole() picks the right
 * branch, AppShellLayout renders the correct RBAC-filtered sidebar for
 * each of the 3 roles.
 */

export function DashboardPage() {
  const { t } = useTranslation();
  const { data: me } = useMe();

  if (!me) {
    // ProtectedRoute (App.tsx) guarantees `me` is loaded before this
    // page renders; this branch exists only to satisfy TypeScript.
    return null;
  }

  const role = resolvePrimaryRole(me.roles);

  return (
    <div className="flex flex-col gap-6">
      <div>
        <h1 className="text-2xl font-semibold text-text-primary">
          {t('nav.dashboard')}
        </h1>
        <p className="mt-1 text-sm text-text-secondary">
          {me.tenant.name} — {role}
        </p>
      </div>

      <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-4">
        <KPICard label="Bootstrap" value="OK" description="Sprint 7 Day 1" />
        <KPICard label="Rôle détecté" value={role} />
        <KPICard label="Tenant" value={me.tenant.status} />
        <KPICard label="Langue" value={me.preferences.language.toUpperCase()} />
      </div>

      <div className="rounded-lg border border-border-subtle bg-elevated p-6 text-sm text-text-secondary shadow-sm">
        Tableau de bord complet ({role === 'tenant_admin' ? 'Exécutif' : role === 'security_analyst' ? 'Opérationnel' : 'Lecture seule'}) — Sprint 7 Day 2-3 EPIC-DASHBOARD.
      </div>
    </div>
  );
}
