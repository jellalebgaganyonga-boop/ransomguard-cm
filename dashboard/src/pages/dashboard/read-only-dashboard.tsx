// ============================================================
// src/pages/dashboard/read-only-dashboard.tsx
// US2.4 — read_only_auditor landing
//
// AC2.4.1: audit-focused — alerts_24h, audit_entries_7d (from the API), 2 buttons
//
// No compliance verdict here: a regulatory status ("98 %", "Loi 2024/017 : Conforme") is shown
// only once a real computation backs it (Reports lot). Until then the tile does not exist.
// AC2.4.2: no write actions visible
// AC2.4.3: status changes hidden (no Acknowledge/Close buttons)
// ============================================================

import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { Info, ScrollText, ShieldAlert } from 'lucide-react';
import { KPICard } from '@/components/ui/kpi-card';
import { useMetricsSummary } from '@/hooks/use-dashboard';
import { useAuditLogs } from '@/hooks/use-audit';

export function ReadOnlyDashboard() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const metrics = useMetricsSummary();
  const summary = metrics.data;

  // Audit entries received over the last 7 days, counted by the GRID (total of the page query).
  // Rounded to the hour so the query key stays stable between renders.
  const [weekAgo] = useState(() => {
    const d = new Date(Date.now() - 7 * 24 * 3600 * 1000);
    d.setMinutes(0, 0, 0);
    return d.toISOString();
  });
  const auditWeek = useAuditLogs({ received_after: weekAgo, limit: 1 });

  return (
    <div>
      <h1 className="text-2xl font-semibold text-text-primary">
        {t('dashboard.readonly.title', 'Vue auditeur')}
      </h1>
      <p className="mb-6 mt-1 text-sm text-text-secondary">
        {t('dashboard.readonly.subtitle', 'Read-only access')}
      </p>

      {/* AC2.4.1: Two KPI cards */}
      <div className="mb-6 grid grid-cols-1 gap-4 sm:grid-cols-2">
        <KPICard
          label={t('dashboard.readonly.kpi.alerts24h', 'Alerts last 24h')}
          value={summary?.alerts_24h ?? '—'}
          {...(summary
            ? { description: `${t('dashboard.readonly.kpi.critical', 'dont')} ${summary.critical_alerts_24h} ${t('severity.critical', 'critiques')}` }
            : {})}
        />
        <KPICard
          label={t('dashboard.readonly.kpi.auditEntries', 'Journal derniers 7 jours')}
          value={auditWeek.data?.total ?? '—'}
          description={t('dashboard.readonly.kpi.auditSub', "audit entries")}
        />
      </div>

      {/* AC2.4.1: Two prominent action buttons */}
      <div className="mb-6 flex flex-col gap-4">
        <button
          className="flex items-center justify-between rounded-lg border border-border-subtle bg-elevated p-5 shadow-sm transition-colors hover:bg-hover-bg"
          onClick={() => navigate('/audit')}
        >
          <div className="flex items-center gap-3">
            <ScrollText className="size-5 text-text-secondary" aria-hidden="true" />
            <div className="text-left">
              <p className="font-semibold text-text-primary">
                &#x1F4DC; {t('dashboard.readonly.auditBtn', "Consulter les journaux d'audit")}
              </p>
              <p className="text-sm text-text-secondary">
                {t('dashboard.readonly.auditSub', 'Search, date filters, CSV export')}
              </p>
            </div>
          </div>
          <span className="text-sm text-primary">
            {t('common.access', 'Access →')}
          </span>
        </button>

        <button
          className="flex items-center justify-between rounded-lg border border-border-subtle bg-elevated p-5 shadow-sm transition-colors hover:bg-hover-bg"
          onClick={() => navigate('/alerts')}
        >
          <div className="flex items-center gap-3">
            <ShieldAlert className="size-5 text-text-secondary" aria-hidden="true" />
            <div className="text-left">
              <p className="font-semibold text-text-primary">
                &#x1F6A8; {t('dashboard.readonly.alertsBtn', 'Consulter les alertes')}
              </p>
              <p className="text-sm text-text-secondary">
                {t('dashboard.readonly.alertsSub', 'Read-only security alerts')}
              </p>
            </div>
          </div>
          <span className="text-sm text-primary">
            {t('common.view', 'Voir →')}
          </span>
        </button>

      </div>

      {/* AC2.4.2: Read-only mode notice */}
      <div className="flex items-start gap-3 rounded-lg bg-primary-subtle p-4">
        <Info className="mt-0.5 size-4 shrink-0 text-primary" aria-hidden="true" />
        <p className="text-sm text-text-secondary">
          {t(
            'dashboard.readonly.notice',
            "You are connected in auditor mode. No modifications are possible."
          )}
        </p>
      </div>
    </div>
  );
}
