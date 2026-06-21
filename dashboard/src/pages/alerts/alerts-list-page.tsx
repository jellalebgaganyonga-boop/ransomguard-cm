/**
 * AlertsListPage — US3.1
 *
 * AC3.1.1: paginated table, 50/page, sortable (created_at/severity/status)
 * AC3.1.2: filters (date range, severity, status, agent, module) synced to URL
 * AC3.1.3: empty state when no alerts match
 * AC3.1.4: multi-tenant isolation — handled server-side (404), not applicable
 *          to list view directly (list is always tenant-scoped by the
 *          backend from the JWT — see AC3.2.6 for the detail-view case)
 * AC3.1.5: role-based row action visibility
 */

import { useNavigate } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { Search, ChevronLeft, ChevronRight, ArrowUp, ArrowDown, Shield } from 'lucide-react';
import { SeverityBadge } from '@/components/ui/severity-badge';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { useAlertList } from '@/hooks/use-alerts';
import { useAlertFilters } from '@/hooks/use-alert-filters';
import { useMe, resolvePrimaryRole } from '@/hooks/use-me';
import { getAlertRowActionPermissions } from '@/lib/alert-permissions';
import { formatDistanceToNow } from '@/lib/format-date';
import type { AlertSeverity, AlertStatus } from '@/api/alerts';

const SEVERITIES: AlertSeverity[] = ['Low', 'Medium', 'High', 'Critical'];
const STATUSES: AlertStatus[] = ['New', 'Investigating', 'Resolved', 'FalsePositive', 'Suppressed'];
const SORT_COLUMNS = [
  { key: 'detected_at' as const, labelKey: 'alerts.list.col.detected' },
  { key: 'severity' as const, labelKey: 'alerts.list.col.severity' },
  { key: 'status' as const, labelKey: 'alerts.list.col.status' },
];

export function AlertsListPage() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const { data: me } = useMe();
  const { filters, setFilter, resetFilters, hasActiveFilters } = useAlertFilters();
  const { data, isLoading } = useAlertList(filters);

  const role = me ? resolvePrimaryRole(me.roles) : 'read_only_auditor';
  const items = data?.items ?? [];
  const total = data?.total ?? 0;
  const offset = filters.offset ?? 0;
  const limit = filters.limit ?? 50;
  const currentPage = Math.floor(offset / limit) + 1;
  const totalPages = Math.max(1, Math.ceil(total / limit));

  const handleSort = (col: 'detected_at' | 'severity' | 'status') => {
    const isSameCol = filters.sort_by === col;
    const nextDir = isSameCol && filters.sort_dir === 'desc' ? 'asc' : 'desc';
    setFilter('sort_by', col);
    setFilter('sort_dir', nextDir);
  };

  return (
    <div>
      <h1 className="mb-5 text-2xl font-semibold text-text-primary">
        {t('nav.alerts', 'Alertes')}
      </h1>

      {/* AC3.1.2: search (agent hostname) */}
      <div className="relative mb-3">
        <Search
          className="absolute left-3 top-1/2 size-4 -translate-y-1/2 text-text-tertiary"
          aria-hidden="true"
        />
        <Input
          placeholder={t('alerts.list.searchPlaceholder', 'Rechercher (hôte...)')}
          className="pl-9"
          defaultValue={filters.agent ?? ''}
          onChange={(e) => setFilter('agent', e.target.value)}
        />
      </div>

      {/* AC3.1.2: filter chips */}
      <div className="mb-4 flex flex-wrap items-center gap-2">
        <select
          className="rounded border border-border-default bg-canvas px-2 py-1 text-xs text-text-secondary"
          value={filters.severity ?? ''}
          onChange={(e) => setFilter('severity', e.target.value || undefined)}
          aria-label={t('alerts.list.col.severity', 'Sévérité')}
        >
          <option value="">{t('alerts.list.allSeverities', 'Toutes sévérités')}</option>
          {SEVERITIES.map((s) => (
            <option key={s} value={s}>
              {t(`severity.${s.toLowerCase()}`, s)}
            </option>
          ))}
        </select>

        <select
          className="rounded border border-border-default bg-canvas px-2 py-1 text-xs text-text-secondary"
          value={filters.status ?? ''}
          onChange={(e) => setFilter('status', e.target.value || undefined)}
          aria-label={t('alerts.list.col.status', 'Statut')}
        >
          <option value="">{t('alerts.list.allStatuses', 'Tous statuts')}</option>
          {STATUSES.map((s) => (
            <option key={s} value={s}>
              {t(`status.${s.toLowerCase()}`, s)}
            </option>
          ))}
        </select>

        {hasActiveFilters && (
          <button
            className="text-xs text-primary hover:underline"
            onClick={resetFilters}
          >
            {t('common.resetFilters', 'Réinitialiser les filtres')}
          </button>
        )}
      </div>

      {/* AC3.1.1: table */}
      <div className="overflow-hidden rounded-lg border border-border-subtle bg-elevated shadow-sm">
        <table className="w-full border-collapse text-sm">
          <thead>
            <tr className="bg-surface-subtle">
              <th className="px-3 py-2.5 text-left text-xs font-semibold uppercase tracking-wide text-text-secondary">
                {t('alerts.list.col.id', 'ID')}
              </th>
              {SORT_COLUMNS.map((col) => (
                <th
                  key={col.key}
                  className="cursor-pointer select-none px-3 py-2.5 text-left text-xs font-semibold uppercase tracking-wide text-text-secondary"
                  onClick={() => handleSort(col.key)}
                >
                  <span className="flex items-center gap-1">
                    {t(col.labelKey, col.key)}
                    {filters.sort_by === col.key &&
                      (filters.sort_dir === 'asc' ? (
                        <ArrowUp className="size-3" />
                      ) : (
                        <ArrowDown className="size-3" />
                      ))}
                  </span>
                </th>
              ))}
              <th className="px-3 py-2.5 text-left text-xs font-semibold uppercase tracking-wide text-text-secondary">
                {t('alerts.list.col.agent', 'Agent')}
              </th>
              <th className="px-3 py-2.5 text-left text-xs font-semibold uppercase tracking-wide text-text-secondary">
                {t('alerts.list.col.module', 'Type')}
              </th>
              <th className="px-3 py-2.5 text-right text-xs font-semibold uppercase tracking-wide text-text-secondary">
                {t('alerts.list.col.actions', 'Actions')}
              </th>
            </tr>
          </thead>
          <tbody>
            {isLoading && (
              <tr>
                <td colSpan={6} className="px-3 py-8 text-center text-sm text-text-tertiary">
                  {t('common.loading', 'Chargement...')}
                </td>
              </tr>
            )}

            {/* AC3.1.3: empty state */}
            {!isLoading && items.length === 0 && (
              <tr>
                <td colSpan={6} className="px-3 py-12">
                  <div className="flex flex-col items-center gap-3 text-center">
                    <Shield className="size-9 text-text-tertiary" aria-hidden="true" />
                    <p className="font-semibold text-text-primary">
                      {t('alerts.list.empty.title', 'Aucune alerte ne correspond à vos filtres')}
                    </p>
                    <p className="text-sm text-text-tertiary">
                      {t(
                        'alerts.list.empty.body',
                        "Essayez d'élargir votre recherche ou de réinitialiser les filtres."
                      )}
                    </p>
                    {hasActiveFilters && (
                      <Button variant="secondary" size="sm" onClick={resetFilters}>
                        {t('common.resetFilters', 'Réinitialiser les filtres')}
                      </Button>
                    )}
                  </div>
                </td>
              </tr>
            )}

            {!isLoading &&
              items.map((alert) => {
                // AC3.1.5: role-based row action visibility
                const perms = getAlertRowActionPermissions(role, alert.status);
                return (
                  <tr
                    key={alert.id}
                    className="cursor-pointer border-t border-border-subtle hover:bg-hover-bg"
                    onClick={() => navigate(`/alerts/${alert.id}`)}
                  >
                    <td className="px-3 py-2.5 font-mono text-xs text-text-tertiary">
                      {alert.id.slice(0, 8)}
                    </td>
                    <td className="px-3 py-2.5 text-text-secondary">
                      {formatDistanceToNow(alert.detected_at)}
                    </td>
                    <td className="px-3 py-2.5">
                      <SeverityBadge severity={alert.severity.toLowerCase() as 'critical' | 'high' | 'medium' | 'low'} />
                    </td>
                    <td className="px-3 py-2.5 text-text-secondary">
                      {t(`status.${alert.status.toLowerCase()}`, alert.status)}
                    </td>
                    <td className="px-3 py-2.5 font-mono text-xs text-text-primary">
                      {alert.agent_id.slice(0, 12)}
                    </td>
                    <td className="px-3 py-2.5 text-xs uppercase text-text-tertiary">
                      {alert.alert_type}
                    </td>
                    <td
                      className="px-3 py-2.5 text-right"
                      onClick={(e) => e.stopPropagation()}
                    >
                      <div className="flex justify-end gap-2">
                        {/* AC3.1.5: read_only_auditor sees only "View" */}
                        {perms.canAcknowledge && (
                          <Button
                            variant="secondary"
                            size="sm"
                            onClick={() => navigate(`/alerts/${alert.id}`)}
                          >
                            {t('alerts.acknowledge', 'Prendre en charge')}
                          </Button>
                        )}
                        <Button
                          variant="ghost"
                          size="sm"
                          onClick={() => navigate(`/alerts/${alert.id}`)}
                        >
                          {t('common.view', 'Voir →')}
                        </Button>
                      </div>
                    </td>
                  </tr>
                );
              })}
          </tbody>
        </table>
      </div>

      {/* AC3.1.1: pagination */}
      {total > 0 && (
        <div className="mt-4 flex items-center justify-end gap-3 text-sm text-text-secondary">
          <button
            disabled={offset === 0}
            onClick={() => setFilter('offset', Math.max(0, offset - limit))}
            className="flex items-center gap-1 disabled:opacity-40"
          >
            <ChevronLeft className="size-4" /> {t('common.previous', 'Précédent')}
          </button>
          <span className="font-medium">
            {t('common.page', { page: currentPage, totalPages, defaultValue: `Page ${currentPage} / ${totalPages}` })}
          </span>
          <button
            disabled={currentPage >= totalPages}
            onClick={() => setFilter('offset', offset + limit)}
            className="flex items-center gap-1 disabled:opacity-40"
          >
            {t('common.next', 'Suivant')} <ChevronRight className="size-4" />
          </button>
        </div>
      )}
    </div>
  );
}
