// ============================================================
// src/pages/dashboard/executive-dashboard.tsx
// US2.2 — tenant_admin landing
//
// AC2.2.1: global status card (green/orange/red per critical_alerts_24h)
// AC2.2.2: 4 KPI cards (agents, alerts_24h, critical_alerts_24h, days-since)
// AC2.2.3: 5 most recent alerts widget (clickable → /alerts/:id)
// AC2.2.4: quick actions (Gérer utilisateurs, Journal audit)
// AC2.2.5: responsive layout
// AC2.2.6: 30s auto-refresh via TanStack Query
// ============================================================

import { useNavigate } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { AlertCircle, Shield, AlertTriangle, Users, ScrollText, FileText } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { KPICard } from '@/components/ui/kpi-card';
import { SeverityBadge } from '@/components/ui/severity-badge';
import {
  useMetricsSummary,
  useRecentAlerts,
  useLastCriticalAlert,
  daysSinceLastCritical,
} from '@/hooks/use-dashboard';
import { formatDistanceToNow } from '@/lib/format-date';
import type { AlertListItem } from '@/api/alerts';

// ── Status card (AC2.2.1) ──────────────────────────────────

function StatusCard({ critical24h, alerts24h }: { critical24h: number; alerts24h: number }) {
  const { t } = useTranslation();
  const navigate = useNavigate();

  if (critical24h > 0) {
    return (
      <div
        className="mb-6 flex flex-col gap-3 rounded-lg border border-severity-critical-border bg-severity-critical-subtle p-5"
        style={{ borderLeft: '4px solid var(--sys-color-severity-critical)' }}
        role="alert"
        aria-live="polite"
      >
        <div className="flex items-center gap-3">
          <AlertCircle className="size-6 shrink-0 text-severity-critical" aria-hidden="true" />
          <p className="font-semibold text-severity-critical">
            {t('dashboard.exec.criticalStatus', {
              count: critical24h,
              defaultValue: `${critical24h} incidents critiques requièrent votre attention`,
            })}
          </p>
        </div>
        <Button variant="ghost" size="sm" onClick={() => navigate('/alerts')}>
          {t('dashboard.exec.viewDetails', 'Voir les détails →')}
        </Button>
      </div>
    );
  }

  if (alerts24h > 0) {
    return (
      <div
        className="mb-6 flex items-center gap-3 rounded-lg border border-severity-high-border bg-severity-high-subtle p-5"
        style={{ borderLeft: '4px solid var(--sys-color-severity-high)' }}
        role="status"
      >
        <AlertTriangle className="size-6 shrink-0 text-severity-high" aria-hidden="true" />
        <p className="font-semibold text-severity-high">
          {t('dashboard.exec.highStatus', {
            count: alerts24h,
            defaultValue: `${alerts24h} alertes à examiner`,
          })}
        </p>
      </div>
    );
  }

  return (
    <div
      className="mb-6 flex items-center gap-3 rounded-lg border border-success-border bg-success-subtle p-5"
      style={{ borderLeft: '4px solid var(--sys-color-feedback-success)' }}
      role="status"
    >
      <Shield className="size-6 shrink-0 text-success" aria-hidden="true" />
      <p className="font-semibold text-success">
        {t('dashboard.exec.allNormal', 'Tous les systèmes normaux')}
      </p>
    </div>
  );
}

// ── Recent alert row (AC2.2.3) ─────────────────────────────
// BUG-02 fix: alert_type replaces agent_hostname (doesn't exist in backend).
// mitre_technique_id replaces module_name (doesn't exist in backend).
// priority_score removed entirely (field doesn't exist in backend AlertItem).

function AlertRow({ alert }: { alert: AlertListItem }) {
  const navigate = useNavigate();

  return (
    <button
      className="flex w-full items-center gap-3 border-t border-border-subtle px-0 py-3 text-left transition-colors hover:bg-hover-bg"
      onClick={() => navigate(`/alerts/${alert.id}`)}
      aria-label={`Voir l'alerte ${alert.id}`}
    >
      <SeverityBadge severity={alert.severity.toLowerCase() as 'critical' | 'high' | 'medium' | 'low'} />
      <div className="min-w-0 flex-1">
        <span className="font-mono text-sm font-semibold text-text-primary">
          {alert.alert_type}
        </span>
        {alert.mitre_technique_id && (
          <span className="ml-2 text-xs uppercase tracking-wide text-text-tertiary">
            {alert.mitre_technique_id}
          </span>
        )}
        <p className="truncate text-sm text-text-secondary">{alert.summary}</p>
      </div>
      <span className="shrink-0 text-xs text-text-tertiary">
        {formatDistanceToNow(alert.detected_at)}
      </span>
    </button>
  );
}

// ── Page ──────────────────────────────────────────────────

export function ExecutiveDashboard() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const metrics = useMetricsSummary();
  const recentAlerts = useRecentAlerts();
  // AC2.2.2: last_critical_at not in MetricsSummary — derive from alert list
  const lastCritical = useLastCriticalAlert();

  const summary = metrics.data;
  const alerts = recentAlerts.data?.items ?? [];
  const days = daysSinceLastCritical(lastCritical.data?.items[0]?.detected_at ?? null);

  return (
    <div>
      <h1 className="text-2xl font-semibold text-text-primary">
        {t('dashboard.exec.title', 'Tableau de bord exécutif')}
      </h1>
      <p className="mb-6 mt-1 text-sm text-text-secondary">
        {t('dashboard.exec.subtitle', "Vue d'ensemble de la sécurité de l'hôpital")}
      </p>

      {/* AC2.2.1: Status card */}
      {summary && (
        <StatusCard
          critical24h={summary.critical_alerts_24h}
          alerts24h={summary.alerts_24h}
        />
      )}

      {/* AC2.2.2: 4 KPI cards — responsive grid (AC2.2.5) */}
      <div className="mb-6 grid grid-cols-1 gap-4 sm:grid-cols-2 xl:grid-cols-4">
        <KPICard
          label={t('dashboard.exec.kpi.agents', 'Postes protégés')}
          value={
            summary
              ? `${summary.active_agents} / ${summary.total_agents}`
              : '—'
          }
        />
        <KPICard
          label={t('dashboard.exec.kpi.alerts24h', 'Alertes 24h')}
          value={summary?.alerts_24h ?? '—'}
        />
        <KPICard
          label={t('dashboard.exec.kpi.critical24h', 'Critiques 24h')}
          value={summary?.critical_alerts_24h ?? '—'}
        />
        <KPICard
          label={t('dashboard.exec.kpi.daysSince', 'Jours sans incident critique')}
          value={days !== null ? days : '—'}
          {...(days === 0
            ? { description: t('dashboard.exec.kpi.todayReset', "Reset aujourd\u2019hui") }
            : {})}
        />
      </div>

      {/* AC2.2.3: Recent alerts widget */}
      <div className="mb-6 rounded-lg border border-border-subtle bg-elevated p-5 shadow-sm">
        <div className="mb-3 flex items-center justify-between">
          <h2 className="text-sm font-semibold uppercase tracking-wide text-text-secondary">
            {t('dashboard.exec.recentAlerts', 'ALERTES RÉCENTES')}
          </h2>
          <button
            className="text-sm text-primary hover:underline"
            onClick={() => navigate('/alerts')}
          >
            {t('common.viewAll', 'Voir tout →')}
          </button>
        </div>
        {alerts.length === 0 ? (
          <p className="py-4 text-center text-sm text-text-tertiary">
            {t('dashboard.exec.noAlerts', 'Aucune alerte récente')}
          </p>
        ) : (
          <div>
            {alerts.map((alert) => (
              <AlertRow key={alert.id} alert={alert} />
            ))}
          </div>
        )}
      </div>

      {/* AC2.2.4: Quick actions */}
      <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
        <div className="rounded-lg border border-border-subtle bg-elevated p-5 shadow-sm">
          <h2 className="mb-3 text-sm font-semibold text-text-primary">
            {t('dashboard.exec.quickActions', 'Actions rapides')}
          </h2>
          <div className="flex flex-col gap-2">
            <Button variant="secondary" className="w-full justify-start" onClick={() => navigate('/users')}>
              <Users className="size-4" aria-hidden="true" />
              {t('dashboard.exec.manageUsers', 'Gérer les utilisateurs')}
            </Button>
            <Button variant="secondary" className="w-full justify-start" onClick={() => navigate('/audit')}>
              <ScrollText className="size-4" aria-hidden="true" />
              {t('dashboard.exec.viewAudit', "Consulter les journaux d'audit")}
            </Button>
            <Button variant="secondary" className="w-full justify-start" disabled>
              <FileText className="size-4" aria-hidden="true" />
              {t('dashboard.exec.report', 'Rapport trimestriel (Sprint 8)')}
            </Button>
          </div>
        </div>

        <div className="rounded-lg border border-border-subtle bg-elevated p-5 shadow-sm">
          <h2 className="mb-3 text-sm font-semibold text-text-primary">
            {t('dashboard.exec.compliance', 'Conformité réglementaire')}
          </h2>
          <p className="mb-2 text-sm text-text-primary">
            {t('dashboard.exec.anticLaw', 'Loi 2024/017 :')}
            {' '}
            <span className="font-semibold text-success">&#x2705; Conforme</span>
          </p>
          <p className="mb-1 text-xs text-text-tertiary">
            {t('dashboard.exec.nextAudit', 'Prochain audit ANTIC : 15 septembre 2026')}
          </p>
          <Button variant="secondary" className="mt-3 w-full justify-start" disabled>
            <FileText className="size-4" aria-hidden="true" />
            {t('dashboard.exec.downloadReport', 'Télécharger le rapport (Sprint 8)')}
          </Button>
        </div>
      </div>
    </div>
  );
}
