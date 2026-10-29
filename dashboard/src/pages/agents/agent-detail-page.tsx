/**
 * AgentDetailPage — US4.2, US4.3, US4.4
 *
 * AC4.2.1: header — hostname, OS, version, last heartbeat (relative), status
 *          NOTE: enrolled_at NOT available from backend AgentItem (GAP-01 in diagnostic)
 * AC4.2.2: tabs — Overview (alerts for this agent), Alerts (full filtered list). The Heartbeats
 *          and Configuration tabs have no backend (GAP-03): hidden rather than shown inert.
 * AC4.2.3: alerts tab links to /alerts/{id}
 * AC4.3.1: Isolate button (tenant_admin only), CommandModal
 * AC4.3.2: no Isolate button for analyst / auditor
 * AC4.3.4: no command status is shown. There is no GET /commands endpoint, and the agent does not
 *          execute commands before Sprint 9: a status made up in the browser ("Pending") was
 *          removed. The command modal says the command is recorded, not executed.
 * R8: the posture section (verdict banner, modules, transmission, endpoint), refreshed every
 *     30 s while the tab is visible. The heartbeat age is shown as is: its colour is the
 *     server's verdict, never a console threshold.
 */

import { useState } from 'react';
import { useParams, useNavigate, Link } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { ChevronLeft, FileQuestion } from 'lucide-react';
import { useAgentDetail } from '@/hooks/use-agents';
import { useMe, resolvePrimaryRole } from '@/hooks/use-me';
import { useAlertList } from '@/hooks/use-alerts';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { SeverityBadge } from '@/components/ui/severity-badge';
import { AgentPostureSection } from '@/components/agents/agent-posture-section';
import { SuspendedProcessesSection } from '@/components/agents/suspended-processes-section';
import { agentStatusVariant } from '@/lib/agent-status';
import { formatDistanceToNow, formatDateTime } from '@/lib/format-date';

// ── Not found ───────────────────────────────────────────────

function AgentNotFound() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  return (
    <div className="flex flex-col items-center justify-center gap-3 py-20 text-center">
      <FileQuestion className="size-10 text-text-secondary" aria-hidden="true" />
      <p className="font-semibold text-text-primary">
        {t('agents.detail.notFound', 'Agent not found')}
      </p>
      <Button variant="secondary" onClick={() => navigate('/agents')}>
        {t('agents.detail.backToList', 'Back to agents')}
      </Button>
    </div>
  );
}

// ── Tab types ───────────────────────────────────────────────

type Tab = 'overview' | 'alerts';

// ── Page ────────────────────────────────────────────────────

export function AgentDetailPage() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const { id } = useParams<{ id: string }>();
  const { data: agent, isLoading, isError, error } = useAgentDetail(id);
  const { data: me } = useMe();
  const role = me ? resolvePrimaryRole(me.roles) : 'read_only_auditor'; // fail closed

  const [activeTab, setActiveTab] = useState<Tab>('overview');

  // Alerts for this agent (used in both overview and alerts tab)
  const { data: alertsData } = useAlertList(
    id ? { agent: id, limit: activeTab === 'overview' ? 5 : 50 } : {}
  );

  const is404 =
    isError && (error as { response?: { status?: number } })?.response?.status === 404;

  if (isLoading) {
    return (
      <div className="py-20 text-center text-sm text-text-secondary">
        {t('common.loading', 'Loading...')}
      </div>
    );
  }

  if (is404 || !agent) {
    return <AgentNotFound />;
  }

  const TABS: Array<{ id: Tab; label: string }> = [
    { id: 'overview', label: t('agents.detail.tabOverview', 'Vue d\'ensemble') },
    { id: 'alerts',   label: t('agents.detail.tabAlerts', 'Alerts') },
  ];

  return (
    <div>
      {/* Breadcrumb */}
      <button
        className="mb-4 flex items-center gap-1.5 text-sm text-primary hover:underline"
        onClick={() => navigate('/agents')}
      >
        <ChevronLeft className="size-3.5" />
        {t('agents.detail.backToList', 'Back to agents')}
      </button>

      {/* Header */}
      <div className="mb-6 rounded-lg border border-border-subtle bg-elevated p-6 shadow-sm">
        <div className="flex flex-wrap items-start justify-between gap-4">
          <div className="flex items-center gap-3">
            <Badge variant={agentStatusVariant(agent.status)}>
              {t(`status.${agent.status}`, agent.status)}
            </Badge>
            <h1 className="font-mono text-xl font-bold text-text-primary">{agent.hostname}</h1>
          </div>

          {/* The "Isolate" button is gone: it queued a command the agent never executed. The
              response to an attack is on the alert (ADR-026): suspend / resume the named process.
              Network isolation does not exist yet (AGT-ISO-001). */}
        </div>

        {/* Metadata grid */}
        <div className="mt-4 grid grid-cols-2 gap-4 sm:grid-cols-4">
          <div>
            <span className="text-[10px] font-semibold uppercase text-text-secondary">
              {t('agents.os', 'OS')}
            </span>
            <p className="mt-0.5 text-sm text-text-primary">{agent.os_version}</p>
          </div>
          <div>
            <span className="text-[10px] font-semibold uppercase text-text-secondary">
              {t('agents.list.version', 'Version')}
            </span>
            <p className="mt-0.5 font-mono text-sm text-text-primary">{agent.agent_version}</p>
          </div>
          <div>
            <span className="text-[10px] font-semibold uppercase text-text-secondary">
              {t('agents.detail.fqdn', 'FQDN')}
            </span>
            <p className="mt-0.5 font-mono text-sm text-text-primary">{agent.fqdn}</p>
          </div>
          <div>
            <span className="text-[10px] font-semibold uppercase text-text-secondary">
              {t('agents.heartbeat', 'Heartbeat')}
            </span>
            <p className="mt-0.5 text-sm font-semibold text-text-primary">
              {agent.last_heartbeat_at
                ? formatDistanceToNow(agent.last_heartbeat_at)
                : t('agents.detail.neverReported', 'Jamais')}
            </p>
            {agent.last_heartbeat_at && (
              <p className="text-xs text-text-secondary">
                {formatDateTime(agent.last_heartbeat_at)}
              </p>
            )}
          </div>
        </div>

      </div>

      {/* R8: is this endpoint protected, why not, did everything arrive? */}
      <div className="mb-6">
        <AgentPostureSection agent={agent} />
      </div>

      {/* ADR-026: what this endpoint says is frozen right now, and the way to let it run again. */}
      <div className="mb-6">
        <SuspendedProcessesSection agentId={agent.id} role={role} />
      </div>

      {/* Tabs */}
      <div className="mb-4 flex gap-1 border-b border-border-subtle">
        {TABS.map((tab) => (
          <button
            key={tab.id}
            onClick={() => setActiveTab(tab.id)}
            className={[
              'px-4 py-2 text-sm font-medium transition-colors',
              activeTab === tab.id
                ? 'border-b-2 border-primary text-primary'
                : 'text-text-secondary hover:text-text-primary',
            ].join(' ')}
          >
            {tab.label}
          </button>
        ))}
      </div>

      {/* Tab content */}
      {activeTab === 'overview' && (
        <div className="space-y-6">
          {/* Recent alerts for this agent */}
          <div className="rounded-lg border border-border-subtle bg-elevated p-6 shadow-sm">
            <div className="mb-3 flex items-center justify-between">
              <h2 className="text-sm font-semibold uppercase tracking-wide text-text-primary">
                {t('agents.detail.recentAlerts', 'Recent alerts')}
              </h2>
              <Link
                to={`/alerts?agent=${id}`}
                className="text-xs font-medium text-primary hover:underline"
              >
                {t('common.viewAll', 'Voir tout →')}
              </Link>
            </div>
            {!alertsData || alertsData.items.length === 0 ? (
              <p className="text-sm text-text-secondary">
                {t('agents.detail.noAlerts', 'No alerts for this agent')}
              </p>
            ) : (
              <ul className="space-y-2">
                {alertsData.items.slice(0, 5).map((alert) => (
                  <li key={alert.id}>
                    <Link
                      to={`/alerts/${alert.id}`}
                      className="flex items-center gap-3 rounded p-2 hover:bg-surface-subtle"
                    >
                      <SeverityBadge
                        severity={alert.severity.toLowerCase() as 'critical' | 'high' | 'medium' | 'low'}
                      />
                      <span className="flex-1 truncate text-sm text-text-primary">
                        {alert.summary}
                      </span>
                      <span className="shrink-0 text-xs text-text-secondary">
                        {formatDistanceToNow(alert.detected_at)}
                      </span>
                    </Link>
                  </li>
                ))}
              </ul>
            )}
          </div>
        </div>
      )}

      {activeTab === 'alerts' && (
        <div className="rounded-lg border border-border-subtle bg-elevated shadow-sm">
          <div className="border-b border-border-subtle px-4 py-3 text-sm font-semibold uppercase tracking-wide text-text-primary">
            {t('agents.detail.allAlerts', 'All alerts')}
            {alertsData && (
              <span className="ml-2 font-normal text-text-secondary">
                ({alertsData.total})
              </span>
            )}
          </div>
          {!alertsData || alertsData.items.length === 0 ? (
            <div className="py-12 text-center text-sm text-text-secondary">
              {t('agents.detail.noAlerts', 'No alerts for this agent')}
            </div>
          ) : (
            // eslint-disable-next-line jsx-a11y/no-noninteractive-tabindex
            <div className="overflow-x-auto" tabIndex={0} role="region" aria-label={t('a11y.scrollableTable')}>
              <table className="w-full text-sm">
                <thead>
                  <tr className="border-b border-border-subtle text-left text-xs font-semibold uppercase tracking-wide text-text-secondary">
                    <th className="px-4 py-3">{t('alerts.list.col.detected', 'Detected')}</th>
                    <th className="px-4 py-3">{t('alerts.list.col.severity', 'Severity')}</th>
                    <th className="px-4 py-3">{t('alerts.list.col.status', 'Status')}</th>
                    <th className="px-4 py-3">{t('alerts.list.col.module', 'Module')}</th>
                    <th className="px-4 py-3"></th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-border-subtle">
                  {alertsData.items.map((alert) => (
                    <tr key={alert.id} className="hover:bg-surface-subtle">
                      <td className="px-4 py-3 text-xs text-text-secondary">
                        {formatDistanceToNow(alert.detected_at)}
                      </td>
                      <td className="px-4 py-3">
                        <SeverityBadge
                          severity={alert.severity.toLowerCase() as 'critical' | 'high' | 'medium' | 'low'}
                        />
                      </td>
                      <td className="px-4 py-3">
                        <span className="rounded bg-surface-subtle px-2 py-0.5 text-xs font-semibold uppercase text-text-secondary">
                          {t(`status.${alert.status.toLowerCase()}`, alert.status)}
                        </span>
                      </td>
                      <td className="px-4 py-3 text-xs text-text-secondary">{alert.alert_type}</td>
                      <td className="px-4 py-3 text-right">
                        <Link
                          to={`/alerts/${alert.id}`}
                          className="text-xs font-medium text-primary hover:underline"
                        >
                          {t('common.view', 'Voir →')}
                        </Link>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </div>
      )}

    </div>
  );
}
