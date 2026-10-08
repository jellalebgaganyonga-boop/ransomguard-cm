/**
 * User Actions Audit Log page (Sprint 8)
 *
 * Unlike the Ed25519 agent audit chain (/audit), this page shows
 * WHO did WHAT on the dashboard: login, logout, alert triage,
 * command issued, user management actions.
 *
 * Accessible to: tenant_admin, read_only_auditor
 */

import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useUserActions } from '@/hooks/use-user-actions';
import { formatDate } from '@/lib/format-date';

const ACTION_TYPES = [
  'login',
  'logout',
  'alert_status_change',
  'command_issued',
  'user_create',
  'user_disable',
  'user_enable',
  'agent_decommission',
  'agent_provision',
] as const;

export function UserActionsPage() {
  const { t } = useTranslation();
  const [actionFilter, setActionFilter] = useState('');
  const [offset, setOffset] = useState(0);
  const limit = 50;

  const { data, isLoading } = useUserActions({
    action_type: actionFilter || undefined,
    offset,
    limit,
  });

  const exportCsv = () => {
    if (!data?.items.length) return;
    const headers = [
      t('userActions.col.date'),
      t('userActions.col.action'),
      t('userActions.col.user'),
      t('userActions.col.target'),
      t('userActions.col.details'),
      t('userActions.col.ip'),
    ];
    const rows = data.items.map((item) => [
      item.created_at,
      t(`userActions.actionType.${item.action_type}`, item.action_type),
      item.actor_email || item.actor_user_id,
      item.target_id || '',
      item.details_json ? JSON.stringify(item.details_json) : '',
      item.ip_address || '',
    ]);
    const csv = [headers, ...rows].map((r) => r.map((c) => `"${c}"`).join(',')).join('\n');
    const blob = new Blob([csv], { type: 'text/csv;charset=utf-8;' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = `user-actions-${new Date().toISOString().slice(0, 10)}.csv`;
    a.click();
    URL.revokeObjectURL(url);
  };

  return (
    <div className="p-6">
      <div className="mb-6 flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-bold text-text-primary">
            {t('userActions.title')}
          </h1>
          <p className="text-sm text-text-muted mt-1">
            {t('userActions.subtitle')}
          </p>
        </div>
        <button
          type="button"
          onClick={exportCsv}
          disabled={!data?.items.length}
          className="rounded-md bg-primary px-4 py-2 text-sm font-medium text-white hover:bg-primary/90 disabled:opacity-50"
        >
          {t('audit.exportCsv')}
        </button>
      </div>

      {/* Filter */}
      <div className="mb-4">
        <label htmlFor="action-filter" className="block text-sm font-medium text-text-muted mb-1">
          {t('userActions.filterByAction')}
        </label>
        <select
          id="action-filter"
          value={actionFilter}
          onChange={(e) => { setActionFilter(e.target.value); setOffset(0); }}
          className="rounded-md border border-border-subtle bg-canvas px-3 py-2 text-sm"
        >
          <option value="">{t('common.all')}</option>
          {ACTION_TYPES.map((key) => (
            <option key={key} value={key}>{t(`userActions.actionType.${key}`)}</option>
          ))}
        </select>
      </div>

      {/* Table */}
      <div className="rounded-lg border border-border-subtle bg-canvas overflow-x-auto">
        <table className="w-full text-sm">
          <thead>
            <tr className="border-b border-border-subtle bg-gray-50">
              <th className="px-4 py-3 text-left font-medium text-text-muted">{t('userActions.col.date')}</th>
              <th className="px-4 py-3 text-left font-medium text-text-muted">{t('userActions.col.action')}</th>
              <th className="px-4 py-3 text-left font-medium text-text-muted">{t('userActions.col.user')}</th>
              <th className="px-4 py-3 text-left font-medium text-text-muted">{t('userActions.col.target')}</th>
              <th className="px-4 py-3 text-left font-medium text-text-muted">{t('userActions.col.details')}</th>
            </tr>
          </thead>
          <tbody>
            {isLoading ? (
              <tr>
                <td colSpan={5} className="px-4 py-8 text-center text-text-muted">
                  {t('common.loading')}
                </td>
              </tr>
            ) : !data?.items.length ? (
              <tr>
                <td colSpan={5} className="px-4 py-8 text-center text-text-muted">
                  {t('userActions.empty')}
                </td>
              </tr>
            ) : (
              data.items.map((item) => (
                <tr key={item.id} className="border-b border-border-subtle last:border-0 hover:bg-gray-50/50">
                  <td className="px-4 py-3 whitespace-nowrap font-mono text-xs">
                    {formatDate(item.created_at)}
                  </td>
                  <td className="px-4 py-3">
                    <span className="inline-block rounded bg-gray-100 px-2 py-0.5 text-xs font-medium">
                      {t(`userActions.actionType.${item.action_type}`, item.action_type)}
                    </span>
                  </td>
                  <td className="px-4 py-3 text-xs">
                    {item.actor_email || `${item.actor_user_id.slice(0, 8)}...`}
                  </td>
                  <td className="px-4 py-3 font-mono text-xs">
                    {item.target_type && item.target_id
                      ? `${item.target_type}:${item.target_id.slice(0, 8)}`
                      : '—'}
                  </td>
                  <td className="px-4 py-3 text-xs text-text-muted max-w-xs truncate">
                    {item.details_json ? JSON.stringify(item.details_json) : '—'}
                  </td>
                </tr>
              ))
            )}
          </tbody>
        </table>
      </div>

      {/* Pagination */}
      {data && data.total > limit && (
        <div className="mt-4 flex items-center justify-between text-sm text-text-muted">
          <span>{data.total} {t('common.results')}</span>
          <div className="flex gap-2">
            <button
              type="button"
              disabled={offset === 0}
              onClick={() => setOffset(Math.max(0, offset - limit))}
              className="rounded border border-border-subtle px-3 py-1 disabled:opacity-50"
            >
              {t('common.previous', 'Précédent')}
            </button>
            <button
              type="button"
              disabled={offset + limit >= data.total}
              onClick={() => setOffset(offset + limit)}
              className="rounded border border-border-subtle px-3 py-1 disabled:opacity-50"
            >
              {t('common.next', 'Suivant')}
            </button>
          </div>
        </div>
      )}
    </div>
  );
}
