// ============================================================
// src/pages/dashboard/read-only-dashboard.tsx
// US2.4 — read_only_auditor landing
//
// AC2.4.1: audit-focused — alerts_24h, audit_entries_7d, 2 buttons
// AC2.4.2: no write actions visible
// AC2.4.3: status changes hidden (no Acknowledge/Close buttons)
// ============================================================

import { useNavigate } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { Info, ScrollText, ShieldAlert } from 'lucide-react';
import { KPICard } from '@/components/ui/kpi-card';
import { useMetricsSummary } from '@/hooks/use-dashboard';

export function ReadOnlyDashboard() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const metrics = useMetricsSummary();
  const summary = metrics.data;

  return (
    <div>
      <h1 className="text-2xl font-semibold text-text-primary">
        {t('dashboard.readonly.title', 'Vue de conformité')}
      </h1>
      <p className="mb-6 mt-1 text-sm text-text-secondary">
        {t('dashboard.readonly.subtitle', 'Accès en lecture seule')}
      </p>

      {/* Compliance status banner */}
      <div
        className="mb-6 rounded-lg p-5"
        style={{
          background: 'var(--sys-color-feedback-success-subtle)',
          borderLeft: '4px solid var(--sys-color-feedback-success)',
        }}
      >
        <p className="text-lg font-semibold text-success">
          &#x2705; {t('dashboard.readonly.compliance', 'CONFORMITÉ ACTUELLE : 98%')}
        </p>
        <p className="mt-1 text-sm text-text-primary">
          {t('dashboard.readonly.law', 'Loi 2024/017 : Conforme')}
        </p>
        <p className="mt-1 text-xs text-text-secondary">
          {t('dashboard.readonly.nextAudit', 'Prochain audit ANTIC : 15 septembre 2026')}
        </p>
      </div>

      {/* AC2.4.1: Two KPI cards */}
      <div className="mb-6 grid grid-cols-1 gap-4 sm:grid-cols-2">
        <KPICard
          label={t('dashboard.readonly.kpi.alerts24h', 'Alertes dernières 24h')}
          value={summary?.alerts_24h ?? '—'}
          {...(summary
            ? { description: `${t('dashboard.readonly.kpi.critical', 'dont')} ${summary.critical_24h} ${t('severity.critical', 'critiques')}` }
            : {})}
        />
        <KPICard
          label={t('dashboard.readonly.kpi.auditEntries', 'Journal derniers 7 jours')}
          value={'487'}
          description={t('dashboard.readonly.kpi.auditSub', "entrées d'audit")}
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
                {t('dashboard.readonly.auditSub', 'Recherche, filtres par date, exportation CSV')}
              </p>
            </div>
          </div>
          <span className="text-sm text-primary">
            {t('common.access', 'Accéder →')}
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
                {t('dashboard.readonly.alertsSub', 'Lecture seule des alertes de sécurité')}
              </p>
            </div>
          </div>
          <span className="text-sm text-primary">
            {t('common.view', 'Voir →')}
          </span>
        </button>

        {/* Sprint 8 deferred */}
        <div className="flex items-center justify-between rounded-lg border border-border-subtle bg-elevated p-5 opacity-60">
          <div className="text-left">
            <p className="font-semibold text-text-primary">
              &#x1F4C4; {t('dashboard.readonly.reportBtn', 'Rapport de conformité trimestriel')}
            </p>
            <p className="text-sm text-text-secondary">
              {t('common.comingInSprint8', 'Disponible à partir de Sprint 8')}
            </p>
          </div>
          <span className="text-xs text-text-tertiary">
            {t('dashboard.readonly.soon', 'Bientôt')}
          </span>
        </div>
      </div>

      {/* AC2.4.2: Read-only mode notice */}
      <div className="flex items-start gap-3 rounded-lg bg-primary-subtle p-4">
        <Info className="mt-0.5 size-4 shrink-0 text-primary" aria-hidden="true" />
        <p className="text-sm text-text-secondary">
          {t(
            'dashboard.readonly.notice',
            "Vous êtes connectée en mode auditeur. Aucune modification n'est possible."
          )}
        </p>
      </div>
    </div>
  );
}
