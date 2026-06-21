// ============================================================
// src/pages/audit/audit-log-page.tsx
// EPIC-AUDIT — US6.1-6.3 (tenant_admin + read_only_auditor)
//
// ── SCOPE NOTE ────────────────────────────────────────────────
// This page shows the Ed25519 agent audit chain (AuditLog DB model),
// NOT a user action / activity log. Each entry represents a signed
// telemetry batch received from a grid agent, with a monotonic
// sequence_number per agent and an Ed25519 signing_key_id.
//
// A user action log (who changed what alert / user) does NOT exist
// in the backend as of Sprint 7 — deferred to Sprint 8 backlog.
//
// AC6.1.1: Paginated list (agent_id link, sequence_number, signing_key_id, received_at)
// AC6.1.2: Filter by agent_id (text input)
// AC6.1.3: Date range filter (received_after / received_before)
// AC6.1.4: Frontend-only sequence gap detection
// AC6.2.1: RBAC — tenant_admin + read_only_auditor; analyst → /dashboard
// AC6.3.1: CSV export — frontend-only (no backend endpoint)
// ============================================================

import { useState, useMemo, useCallback } from 'react';
import { useTranslation } from 'react-i18next';
import { useNavigate, Link } from 'react-router-dom';
import { Download, AlertTriangle } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { useMe } from '@/hooks/use-me';
import { useAuditLogs } from '@/hooks/use-audit';
import { formatDateTime } from '@/lib/format-date';
import { detectSequenceGaps } from '@/lib/audit-integrity';
import type { AuditLogItem } from '@/api/audit';

// ── Pagination ──────────────────────────────────────────────
const PAGE_SIZE = 50;

// ── CSV export ──────────────────────────────────────────────
// AC6.3.1: Frontend-only — no backend CSV endpoint exists.
function downloadCsv(items: AuditLogItem[], filename: string): void {
  const header = ['id', 'agent_id', 'sequence_number', 'signing_key_id', 'received_at'];
  const rows = items.map((r) => [
    r.id,
    r.agent_id,
    String(r.sequence_number),
    r.signing_key_id,
    r.received_at,
  ]);
  const csv = [header, ...rows]
    .map((row) => row.map((cell) => `"${cell.replace(/"/g, '""')}"`).join(','))
    .join('\n');
  const blob = new Blob([csv], { type: 'text/csv;charset=utf-8;' });
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = filename;
  a.click();
  URL.revokeObjectURL(url);
}

// ── Filter state ─────────────────────────────────────────────
interface Filters {
  agent_id: string;
  received_after: string;
  received_before: string;
}

const EMPTY_FILTERS: Filters = {
  agent_id: '',
  received_after: '',
  received_before: '',
};

// ── Page ────────────────────────────────────────────────────
export function AuditLogPage() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const { data: me } = useMe();

  const [offset, setOffset] = useState(0);
  const [pendingFilters, setPendingFilters] = useState<Filters>(EMPTY_FILTERS);
  const [appliedFilters, setAppliedFilters] = useState<Filters>(EMPTY_FILTERS);

  // AC6.2.1: RBAC — security_analyst is NOT allowed (backend 403)
  // tenant_admin and read_only_auditor can access.
  const role = me?.roles[0];
  if (me && role === 'security_analyst') {
    navigate('/dashboard', { replace: true });
    return null;
  }

  const queryParams = useMemo((): import('@/api/audit').AuditLogParams => {
    const p: import('@/api/audit').AuditLogParams = { offset, limit: PAGE_SIZE };
    if (appliedFilters.agent_id) p.agent_id = appliedFilters.agent_id;
    if (appliedFilters.received_after) p.received_after = appliedFilters.received_after;
    if (appliedFilters.received_before) p.received_before = appliedFilters.received_before;
    return p;
  }, [appliedFilters, offset]);

  const { data, isLoading, isError, refetch } = useAuditLogs(queryParams);

  const items = data?.items ?? [];
  const gapFlags = detectSequenceGaps(items);
  const total = data?.total ?? 0;
  const totalPages = Math.max(1, Math.ceil(total / PAGE_SIZE));
  const currentPage = Math.floor(offset / PAGE_SIZE) + 1;

  const hasActiveFilters = Object.values(appliedFilters).some(Boolean);

  function applyFilters() {
    setOffset(0);
    setAppliedFilters({ ...pendingFilters });
  }

  function resetFilters() {
    setPendingFilters(EMPTY_FILTERS);
    setAppliedFilters(EMPTY_FILTERS);
    setOffset(0);
  }

  const handleExportCsv = useCallback(() => {
    if (items.length === 0) return;
    const ts = new Date().toISOString().slice(0, 10);
    downloadCsv(items, `audit-logs-${ts}.csv`);
  }, [items]);

  return (
    <div>
      {/* ── Header ── */}
      <div className="mb-6 flex items-start justify-between">
        <div>
          <h1 className="text-2xl font-semibold text-text-primary">
            {t('audit.title', 'Journal d\'audit')}
          </h1>
          <p className="mt-1 text-sm text-text-secondary">
            {t('audit.subtitle', 'Intégrité du journal d\'audit — chaîne Ed25519 par agent')}
          </p>
          {total > 0 && (
            <p className="mt-0.5 text-xs text-text-tertiary">
              {total} {t('audit.totalEntries', 'entrées')}
            </p>
          )}
        </div>
        <Button
          variant="secondary"
          size="sm"
          onClick={handleExportCsv}
          disabled={items.length === 0}
          title={t('audit.exportCsvTitle', 'Exporter la page courante en CSV')}
        >
          <Download className="size-4" aria-hidden="true" />
          {t('audit.exportCsv', 'Exporter CSV')}
        </Button>
      </div>

      {/* ── Filters ── */}
      <div className="mb-4 flex flex-wrap items-end gap-3 rounded-lg border border-border-subtle bg-elevated p-3">
        <div className="flex-1 min-w-[200px]">
          <label className="mb-1 block text-xs font-medium text-text-secondary">
            {t('audit.filterAgentId', 'ID Agent')}
          </label>
          <input
            type="text"
            value={pendingFilters.agent_id}
            onChange={(e) => setPendingFilters((f) => ({ ...f, agent_id: e.target.value }))}
            placeholder={t('audit.filterAgentIdPlaceholder', 'UUID agent...')}
            className="w-full rounded-md border border-border-default bg-surface px-3 py-1.5 text-sm text-text-primary placeholder:text-text-tertiary focus:border-primary focus:outline-none focus:ring-1 focus:ring-primary"
          />
        </div>

        <div className="flex-1 min-w-[160px]">
          <label className="mb-1 block text-xs font-medium text-text-secondary">
            {t('audit.filterAfter', 'Reçu après')}
          </label>
          <input
            type="datetime-local"
            value={pendingFilters.received_after}
            onChange={(e) => setPendingFilters((f) => ({ ...f, received_after: e.target.value }))}
            className="w-full rounded-md border border-border-default bg-surface px-3 py-1.5 text-sm text-text-primary focus:border-primary focus:outline-none focus:ring-1 focus:ring-primary"
          />
        </div>

        <div className="flex-1 min-w-[160px]">
          <label className="mb-1 block text-xs font-medium text-text-secondary">
            {t('audit.filterBefore', 'Reçu avant')}
          </label>
          <input
            type="datetime-local"
            value={pendingFilters.received_before}
            onChange={(e) => setPendingFilters((f) => ({ ...f, received_before: e.target.value }))}
            className="w-full rounded-md border border-border-default bg-surface px-3 py-1.5 text-sm text-text-primary focus:border-primary focus:outline-none focus:ring-1 focus:ring-primary"
          />
        </div>

        <div className="flex items-center gap-2">
          <Button size="sm" onClick={applyFilters}>
            {t('common.search', 'Rechercher')}
          </Button>
          {hasActiveFilters && (
            <Button variant="ghost" size="sm" onClick={resetFilters}>
              {t('common.resetFilters', 'Réinitialiser')}
            </Button>
          )}
        </div>
      </div>

      {/* ── Table ── */}
      <div className="rounded-lg border border-border-subtle bg-elevated shadow-sm">
        {isLoading && (
          <p className="py-10 text-center text-sm text-text-tertiary">
            {t('common.loading', 'Chargement...')}
          </p>
        )}

        {isError && (
          <div className="py-10 text-center">
            <p className="mb-3 text-sm text-severity-critical">{t('common.error')}</p>
            <Button variant="ghost" size="sm" onClick={() => void refetch()}>
              {t('common.retry', 'Réessayer')}
            </Button>
          </div>
        )}

        {!isLoading && !isError && items.length === 0 && (
          <p className="py-10 text-center text-sm text-text-tertiary">
            {t('audit.empty', 'Aucune entrée trouvée')}
          </p>
        )}

        {!isLoading && !isError && items.length > 0 && (
          <div className="overflow-x-auto">
            <table className="w-full border-collapse text-sm">
              <thead>
                <tr className="border-b border-border-default">
                  {[
                    t('audit.col.agentId', 'Agent'),
                    t('audit.col.sequenceNumber', 'Séquence'),
                    t('audit.col.signingKeyId', 'Clé de signature'),
                    t('audit.col.receivedAt', 'Reçu le'),
                  ].map((h) => (
                    <th
                      key={h}
                      className="px-4 py-3 text-left text-xs font-semibold uppercase tracking-wide text-text-secondary"
                    >
                      {h}
                    </th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {items.map((entry, idx) => {
                  const hasGap = gapFlags[idx] === true;
                  return [
                    // AC6.1.4: sequence gap indicator row
                    hasGap && (
                      <tr key={`gap-${entry.id}`}>
                        <td
                          colSpan={4}
                          className="border-y border-warning bg-warning-subtle px-4 py-1.5"
                        >
                          <div className="flex items-center gap-1.5 text-xs font-medium text-warning">
                            <AlertTriangle className="size-3.5 shrink-0" aria-hidden="true" />
                            {t(
                              'audit.gapWarning',
                              'Discontinuité détectée dans la séquence — entrées manquantes ou désordonnées.',
                            )}
                          </div>
                        </td>
                      </tr>
                    ),
                    <tr
                      key={entry.id}
                      className="border-b border-border-subtle last:border-0 hover:bg-hover-bg"
                    >
                      {/* AC6.1.1: agent_id as link to /agents/:id */}
                      <td className="px-4 py-3 font-mono text-xs">
                        <Link
                          to={`/agents/${entry.agent_id}`}
                          className="text-primary underline-offset-2 hover:underline"
                          title={entry.agent_id}
                        >
                          {entry.agent_id.slice(0, 8)}…
                        </Link>
                      </td>

                      <td className="px-4 py-3 font-mono text-text-primary">
                        {entry.sequence_number.toLocaleString()}
                      </td>

                      <td className="px-4 py-3 font-mono text-xs text-text-secondary">
                        <span title={entry.signing_key_id}>
                          {entry.signing_key_id.slice(0, 16)}…
                        </span>
                      </td>

                      <td className="px-4 py-3 text-text-tertiary">
                        {formatDateTime(entry.received_at)}
                      </td>
                    </tr>,
                  ];
                })}
              </tbody>
            </table>
          </div>
        )}
      </div>

      {/* ── Pagination ── */}
      {total > PAGE_SIZE && (
        <div className="mt-4 flex items-center justify-between">
          <Button
            variant="ghost"
            size="sm"
            disabled={offset === 0}
            onClick={() => setOffset((o) => Math.max(0, o - PAGE_SIZE))}
          >
            {t('common.previous', 'Précédent')}
          </Button>
          <span className="text-xs text-text-tertiary">
            {t('common.page', 'Page {page} / {totalPages}')
              .replace('{page}', String(currentPage))
              .replace('{totalPages}', String(totalPages))}
          </span>
          <Button
            variant="ghost"
            size="sm"
            disabled={offset + PAGE_SIZE >= total}
            onClick={() => setOffset((o) => o + PAGE_SIZE)}
          >
            {t('common.next', 'Suivant')}
          </Button>
        </div>
      )}
    </div>
  );
}
