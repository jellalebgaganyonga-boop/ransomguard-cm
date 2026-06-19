// ============================================================
// src/pages/dashboard/operational-dashboard.tsx
// US2.3 — security_analyst landing
//
// AC2.3.1: agent health summary (active/offline/stale counts)
// AC2.3.2: live alert feed — 20 alerts, 15s polling, clickable
// AC2.3.3: agent health table — top 10 by heartbeat
// AC2.3.4: quick filters on alerts feed (All/Critical/Last hour)
// AC4.4.3: stale agent warning banner (>= 1 agent offline > 60min)
// ============================================================

import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { AlertTriangle, ExternalLink } from 'lucide-react';
import { PriorityScore } from '@/components/ui/priority-score';
import { SeverityBadge } from '@/components/ui/severity-badge';
import { Button } from '@/components/ui/button';
import { useMetricsSummary, useLiveAlerts, useAgents } from '@/hooks/use-dashboard';
import { formatDistanceToNow } from '@/lib/format-date';
import type { AlertSeverity, AgentSummary } from '@/api/dashboard';

// ── Filter types (AC2.3.4) ─────────────────────────────────

type AlertFilter = 'all' | 'critical' | 'last_hour';

const AGENT_STATUS_COLOR: Record<string, string> = {
  online: 'var(--sys-color-status-online)',
  stale: 'var(--sys-color-status-stale)',
  offline: 'var(--sys-color-status-offline)',
  isolated: 'var(--sys-color-status-isolated)',
};

// ── Agent row ──────────────────────────────────────────────

function AgentRow({ agent }: { agent: AgentSummary }) {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const statusLabel: Record<string, string> = {
    online: t('status.online', 'Actif'),
    stale: t('status.stale', 'Obsolète'),
    offline: t('status.offline', 'Hors ligne'),
    isolated: t('status.isolated', 'Isolé'),
  };

  return (
    <tr
      className="cursor-pointer border-b border-border-subtle hover:bg-hover-bg"
      onClick={() => navigate(`/agents/${agent.id}`)}
    >
      <td className="px-3 py-2 font-mono text-sm font-semibold text-text-primary">
        {agent.hostname}
      </td>
      <td className="px-3 py-2 text-sm text-text-secondary">{agent.os_info}</td>
      <td className="px-3 py-2 text-sm text-text-tertiary">
        {agent.last_heartbeat_at
          ? formatDistanceToNow(agent.last_heartbeat_at)
          : '—'}
      </td>
      <td className="px-3 py-2">
        <span className="flex items-center gap-2 text-sm">
          <span
            className="inline-block size-2 rounded-full"
            style={{ background: AGENT_STATUS_COLOR[agent.status] ?? 'gray' }}
          />
          {statusLabel[agent.status] ?? agent.status}
        </span>
      </td>
    </tr>
  );
}

// ── Page ──────────────────────────────────────────────────

export function OperationalDashboard() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const [filter, setFilter] = useState<AlertFilter>('all');

  const metrics = useMetricsSummary();
  const agents = useAgents(10);

  // AC2.3.4: apply filter → pass to query
  const alertParams = {
    all: {},
    critical: { severity: 'critical' as AlertSeverity },
    last_hour: { hours: 1 },
  }[filter];
  const liveAlerts = useLiveAlerts(alertParams);

  const summary = metrics.data;
  const agentList = agents.data?.items ?? [];
  const alertList = liveAlerts.data?.items ?? [];

  // AC4.4.3: stale warning banner
  const hasStaleAgents = (summary?.offline_agents ?? 0) + (summary?.stale_agents ?? 0) > 0;
  const staleCount = (summary?.stale_agents ?? 0) + (summary?.offline_agents ?? 0);

  return (
    <div>
      <h1 className="text-2xl font-semibold text-text-primary">
        {t('dashboard.ops.title', 'Console opérationnelle')}
      </h1>
      <p className="mb-6 mt-1 text-xs text-text-tertiary">
        &#x2318;K {t('dashboard.ops.commandHint', 'pour la palette de commandes')}
      </p>

      {/* AC4.4.3: stale agent warning */}
      {hasStaleAgents && (
        <div className="mb-4 flex items-center gap-2 rounded-lg border border-severity-high-border bg-severity-high-subtle px-4 py-3">
          <AlertTriangle className="size-4 shrink-0 text-severity-high" aria-hidden="true" />
          <p className="text-sm text-text-primary">
            {t('dashboard.ops.staleWarning', {
              count: staleCount,
              defaultValue: `${staleCount} agents n'ont pas envoyé de heartbeat depuis plus d'1 heure`,
            })}
          </p>
        </div>
      )}

      {/* AC2.3.1: Agent health summary */}
      <div className="mb-6 flex gap-8 rounded-lg border border-border-subtle bg-elevated px-5 py-4 shadow-sm">
        {[
          {
            n: summary?.active_agents ?? 0,
            label: t('dashboard.ops.agents.active', 'actifs'),
            color: 'var(--sys-color-status-online)',
          },
          {
            n: summary?.stale_agents ?? 0,
            label: t('dashboard.ops.agents.stale', 'obsolètes'),
            color: 'var(--sys-color-status-stale)',
          },
          {
            n: summary?.offline_agents ?? 0,
            label: t('dashboard.ops.agents.offline', 'hors ligne'),
            color: 'var(--sys-color-status-offline)',
          },
        ].map((s) => (
          <div key={s.label} className="flex items-center gap-3">
            <span
              className="size-3 rounded-full"
              style={{ background: s.color }}
            />
            <span className="text-2xl font-bold tabular-nums text-text-primary">
              {s.n}
            </span>
            <span className="text-sm text-text-tertiary">{s.label}</span>
          </div>
        ))}
      </div>

      {/* AC2.3.2: Live alert feed */}
      <div className="mb-6 rounded-lg border border-border-subtle bg-elevated p-5 shadow-sm">
        <div className="mb-3 flex items-center justify-between">
          <h2 className="text-sm font-semibold uppercase tracking-wide text-text-secondary">
            {t('dashboard.ops.feed.title', "FLUX D\u2019ALERTES")}
          </h2>
          <span className="flex items-center gap-1.5 text-xs text-text-tertiary">
            <span
              className="size-2 animate-pulse rounded-full bg-success"
              aria-hidden="true"
            />
            {t('dashboard.ops.feed.refresh', 'Toutes les 15s')}
          </span>
        </div>

        {/* AC2.3.4: filter chips */}
        <div className="mb-4 flex gap-2">
          {(
            [
              { key: 'all', label: t('common.all', 'Tous') },
              { key: 'critical', label: t('severity.critical', 'Critique') },
              { key: 'last_hour', label: t('dashboard.ops.feed.lastHour', 'Dernière heure') },
            ] as const
          ).map((f) => (
            <button
              key={f.key}
              onClick={() => setFilter(f.key)}
              className="rounded px-3 py-1 text-xs font-medium transition-colors"
              style={{
                background:
                  filter === f.key
                    ? 'var(--sys-color-interactive-selected-bg)'
                    : 'transparent',
                color:
                  filter === f.key
                    ? 'var(--sys-color-primary)'
                    : 'var(--sys-color-text-secondary)',
                border: '1px solid var(--sys-color-border-default)',
              }}
            >
              {f.label}
            </button>
          ))}
        </div>

        {/* Alert rows */}
        <div>
          {alertList.length === 0 ? (
            <p className="py-6 text-center text-sm text-text-tertiary">
              {t('common.noResults', 'Aucun résultat')}
            </p>
          ) : (
            alertList.map((alert, i) => (
              <button
                key={alert.id}
                className="flex w-full items-center gap-3 py-3 text-left transition-colors hover:bg-hover-bg"
                style={{
                  borderTop:
                    i > 0 ? '1px solid var(--sys-color-border-subtle)' : 'none',
                }}
                onClick={() => navigate(`/alerts/${alert.id}`)}
              >
                <PriorityScore score={alert.priority_score} />
                <SeverityBadge severity={alert.severity} />
                <div className="min-w-0 flex-1">
                  <span className="font-mono text-sm font-semibold text-text-primary">
                    {alert.agent_hostname}
                  </span>
                  <span className="ml-2 text-xs uppercase tracking-wide text-text-tertiary">
                    {alert.module_name}
                  </span>
                  <p className="truncate text-sm text-text-secondary">{alert.summary}</p>
                </div>
                <span className="shrink-0 text-xs text-text-tertiary">
                  {formatDistanceToNow(alert.detected_at)}
                </span>
                <div className="flex gap-2">
                  <Button variant="secondary" size="sm">
                    {t('alerts.acknowledge', 'Prendre en charge')}
                  </Button>
                  <Button variant="ghost" size="sm">
                    {t('common.view', 'Voir →')}
                  </Button>
                </div>
              </button>
            ))
          )}
        </div>
      </div>

      {/* AC2.3.3: Agent health table — top 10 */}
      <div className="rounded-lg border border-border-subtle bg-elevated p-5 shadow-sm">
        <div className="mb-3 flex items-center justify-between">
          <h2 className="text-sm font-semibold uppercase tracking-wide text-text-secondary">
            {t('dashboard.ops.agents.title', 'AGENTS')}
          </h2>
          <button
            className="flex items-center gap-1 text-sm text-primary hover:underline"
            onClick={() => navigate('/agents')}
          >
            <ExternalLink className="size-3" aria-hidden="true" />
            {t('common.viewAll', 'Voir tout')}
          </button>
        </div>
        <div className="overflow-x-auto">
          <table className="w-full border-collapse text-sm">
            <thead>
              <tr className="border-b border-border-default">
                {[
                  t('agents.hostname', 'Hôte'),
                  t('agents.os', 'OS'),
                  t('agents.heartbeat', 'Heartbeat'),
                  t('agents.status', 'État'),
                ].map((h) => (
                  <th
                    key={h}
                    className="px-3 py-2 text-left text-xs font-semibold uppercase tracking-wide text-text-secondary"
                  >
                    {h}
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              {agentList.map((agent) => (
                <AgentRow key={agent.id} agent={agent} />
              ))}
            </tbody>
          </table>
        </div>
      </div>
    </div>
  );
}
