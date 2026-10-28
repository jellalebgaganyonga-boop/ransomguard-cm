/**
 * AgentsListPage — US4.1, US4.4
 *
 * AC4.1.1: table with columns Hostname, OS, Version, Last heartbeat, Status
 * AC4.1.2: status filter, tenant isolation enforced by backend JWT
 * AC4.1.3: actions — [View] for all roles; [Isolate] for tenant_admin only
 *          NOTE: PRD says analyst also sees "Send command" but backend returns 403
 *          for analyst on POST /commands — showing button for admin only.
 * AC4.4.1: heartbeat freshness chip (green < 5 min, yellow 5–60 min, red > 60 min)
 * AC4.4.2: auto-refresh every 30 seconds
 * AC4.4.3: stale agent warning banner (≥ 1 agent > 60 min)
 */

import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { Server, RefreshCw, AlertTriangle, Download, PlusCircle, Copy, CheckCircle2, X } from 'lucide-react';
import { useAgentList, useDownloadInstaller, useProvisionAgent } from '@/hooks/use-agents';
import { useMe, resolvePrimaryRole } from '@/hooks/use-me';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { agentStatusVariant, isSignalLostRed, protectionVariant } from '@/lib/agent-status';
import { verdictSentence } from '@/lib/agent-posture';
import { installerRequestBody } from '@/lib/installer-download';
import { formatDistanceToNow } from '@/lib/format-date';
import type { AgentStatus } from '@/api/agents';
import { STATUS_FILTERS } from './agent-status-filters';

const PAGE_SIZE = 50;


export function AgentsListPage() {
  const { t } = useTranslation();
  const { data: me } = useMe();
  const role = me ? resolvePrimaryRole(me.roles) : 'read_only_auditor';

  const [statusFilter, setStatusFilter] = useState<AgentStatus | ''>('');
  const [offset, setOffset] = useState(0);
  const [showProvision, setShowProvision] = useState(false);

  // Escape closes the provisioning modal. The backdrop is deliberately not
  // clickable: a click handler on a non-interactive element is unreachable by
  // keyboard, and the rest of the console's modals close the same way.
  useEffect(() => {
    if (!showProvision) return;
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') setShowProvision(false);
    };
    document.addEventListener('keydown', onKey);
    return () => document.removeEventListener('keydown', onKey);
  }, [showProvision]);
  const [copiedField, setCopiedField] = useState<'otp' | null>(null);

  const provision = useProvisionAgent();
  const download = useDownloadInstaller();

  const handleProvision = () => {
    provision.reset();
    download.reset();
    setShowProvision(true);
    provision.mutate();
  };

  const copyToClipboard = (text: string, field: 'otp') => {
    void navigator.clipboard.writeText(text).then(() => {
      setCopiedField(field);
      setTimeout(() => setCopiedField(null), 2000);
    });
  };

  const params = {
    ...(statusFilter ? { status: statusFilter } : {}),
    offset,
    limit: PAGE_SIZE,
  };
  const { data, isLoading, isError, isFetching, dataUpdatedAt } = useAgentList(params);

  const agents = data?.items ?? [];
  const total = data?.total ?? 0;
  const totalPages = Math.ceil(total / PAGE_SIZE);
  const currentPage = Math.floor(offset / PAGE_SIZE) + 1;

  // The server's "no signal for more than 60 min" (unprotected, signal_lost): no console threshold.
  const staleCount = agents.filter(isSignalLostRed).length;

  const exportCsv = () => {
    if (!agents.length) return;
    const headers = ['Hostname', 'FQDN', 'OS', 'Version', 'Status', 'Last Heartbeat', 'Protection', 'Protection detail'];
    const rows = agents.map((a) => [
      a.hostname, a.fqdn, a.os_version, a.agent_version, a.status, a.last_heartbeat_at ?? '',
      a.protection_state, verdictSentence(t, a),
    ]);
    const csv = [headers, ...rows].map((r) => r.map((c) => `"${String(c).replace(/"/g, '""')}"`).join(',')).join('\n');
    const blob = new Blob([csv], { type: 'text/csv;charset=utf-8;' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = `agents-${new Date().toISOString().slice(0, 10)}.csv`;
    a.click();
    URL.revokeObjectURL(url);
  };

  return (
    <div>
      {/* Header */}
      <div className="mb-6 flex items-center justify-between">
        <div>
          <h1 className="text-lg font-semibold text-text-primary">
            {t('nav.agents', 'Agents')}
          </h1>
          <p className="text-sm text-text-secondary">
            {total} {t('agents.list.totalEndpoints', 'registered endpoints')}
          </p>
        </div>
        <div className="flex items-center gap-2">
          <Button variant="secondary" size="sm" onClick={exportCsv} disabled={agents.length === 0}>
            <Download className="mr-1 size-4" />
            CSV
          </Button>
          {role === 'tenant_admin' && (
            <Button size="sm" onClick={handleProvision} disabled={provision.isPending}>
              <PlusCircle className="mr-1 size-4" />
              {t('agents.list.addAgent', 'Add Agent')}
            </Button>
          )}
          {isFetching && (
            <RefreshCw className="size-4 animate-spin text-text-tertiary" aria-hidden="true" />
          )}
        </div>
      </div>

      {/* AC4.4.3: stale agent warning banner */}
      {staleCount > 0 && (
        <div className="mb-4 flex items-start gap-2 rounded-lg border border-severity-medium-border bg-severity-medium-subtle px-4 py-3 text-sm text-text-primary">
          <AlertTriangle className="mt-0.5 size-4 shrink-0 text-warning" aria-hidden="true" />
          <span>
            {t('posture.list.signalLostCount', { count: staleCount })}
          </span>
        </div>
      )}

      {/* Status filter chips */}
      <div className="mb-4 flex flex-wrap gap-2">
        {STATUS_FILTERS.map((f) => (
          <button
            key={f.value}
            onClick={() => { setStatusFilter(f.value); setOffset(0); }}
            className={[
              'rounded-full px-3 py-1 text-xs font-medium transition-colors',
              statusFilter === f.value
                ? 'bg-primary text-primary-on'
                : 'bg-surface-subtle text-text-secondary hover:bg-surface-muted',
            ].join(' ')}
          >
            {t(f.labelKey, f.fallback)}
          </button>
        ))}
      </div>

      {/* Table */}
      <div className="rounded-lg border border-border-subtle bg-elevated shadow-sm">
        {isLoading ? (
          <div className="py-20 text-center text-sm text-text-secondary">
            {t('common.loading', 'Loading...')}
          </div>
        ) : isError ? (
          <div className="py-20 text-center text-sm text-error">
            {t('common.error', 'An error occurred')}
          </div>
        ) : agents.length === 0 ? (
          <div className="flex flex-col items-center gap-2 py-20">
            <Server className="size-10 text-text-tertiary" aria-hidden="true" />
            <p className="text-sm text-text-secondary">
              {t('agents.list.empty', 'No agents registered')}
            </p>
          </div>
        ) : (
          // eslint-disable-next-line jsx-a11y/no-noninteractive-tabindex
          <div className="overflow-x-auto" tabIndex={0} role="region" aria-label={t('a11y.scrollableTable')}>
            <table className="w-full text-sm">
              <thead>
                <tr className="border-b border-border-subtle text-left text-xs font-semibold uppercase tracking-wide text-text-secondary">
                  <th className="px-4 py-3">{t('agents.hostname', 'Host')}</th>
                  <th className="px-4 py-3">{t('agents.os', 'OS')}</th>
                  <th className="px-4 py-3">{t('agents.list.version', 'Version')}</th>
                  <th className="px-4 py-3">{t('posture.list.protection')}</th>
                  <th className="px-4 py-3">{t('agents.heartbeat', 'Heartbeat')}</th>
                  <th className="px-4 py-3">{t('agents.status', 'Status')}</th>
                  <th className="px-4 py-3 text-right">{t('alerts.list.col.actions', 'Actions')}</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-border-subtle">
                {agents.map((agent) => {
                  return (
                    <tr key={agent.id} className="hover:bg-surface-subtle">
                      <td className="px-4 py-3">
                        <div className="font-mono font-semibold text-text-primary">
                          {agent.hostname}
                        </div>
                        <div className="text-xs text-text-secondary">{agent.fqdn}</div>
                      </td>
                      <td className="px-4 py-3 text-text-secondary">{agent.os_version}</td>
                      <td className="px-4 py-3 font-mono text-xs text-text-secondary">
                        {agent.agent_version}
                      </td>
                      <td className="px-4 py-3">
                        {/* Protection: the server's verdict, coloured; the sentence on hover. */}
                        <span title={verdictSentence(t, agent)}>
                          <Badge variant={protectionVariant(agent.protection_state)}>
                            {t(`posture.verdictShort.${agent.protection_state}`)}
                          </Badge>
                        </span>
                      </td>
                      <td className="px-4 py-3">
                        {agent.last_heartbeat_at ? (
                          <span className="text-text-primary">
                            {formatDistanceToNow(agent.last_heartbeat_at)}
                          </span>
                        ) : (
                          <span className="text-text-secondary">—</span>
                        )}
                      </td>
                      <td className="px-4 py-3">
                        <Badge variant={agentStatusVariant(agent.status)}>
                          {t(`status.${agent.status}`, agent.status)}
                        </Badge>
                      </td>
                      <td className="px-4 py-3">
                        <div className="flex items-center justify-end gap-2">
                          <Link
                            to={`/agents/${agent.id}`}
                            className="text-xs font-medium text-primary hover:underline"
                          >
                            {t('common.view', 'Voir →')}
                          </Link>
                        </div>
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        )}
      </div>

      {/* Pagination */}
      {total > PAGE_SIZE && (
        <div className="mt-4 flex items-center justify-between text-sm text-text-secondary">
          <Button
            variant="secondary"
            disabled={offset === 0}
            onClick={() => setOffset(Math.max(0, offset - PAGE_SIZE))}
          >
            {t('common.previous', 'Previous')}
          </Button>
          <span>
            {t('common.page', { page: currentPage, totalPages }).replace('{page}', String(currentPage)).replace('{totalPages}', String(totalPages))}
          </span>
          <Button
            variant="secondary"
            disabled={offset + PAGE_SIZE >= total}
            onClick={() => setOffset(offset + PAGE_SIZE)}
          >
            {t('common.next', 'Next')}
          </Button>
        </div>
      )}

      {/* Last refresh hint */}
      {dataUpdatedAt > 0 && (
        <p className="mt-2 text-right text-xs text-text-secondary">
          {t('agents.list.autoRefresh', 'Auto-refresh every 30 s')}
        </p>
      )}

      {/* Provision new agent modal */}
      {showProvision && (
        <div
          role="dialog"
          aria-modal="true"
          aria-label={t('agents.provision.title', 'Add new agent')}
          className="fixed inset-0 z-modal flex items-center justify-center bg-scrim"
        >
          <div className="relative w-full max-w-lg rounded-xl bg-elevated p-6 shadow-xl">
            <button
              aria-label={t('common.close', 'Close')}
              onClick={() => setShowProvision(false)}
              className="absolute right-4 top-4 text-text-secondary hover:text-text-primary"
            >
              <X className="size-4" />
            </button>

            <h2 className="mb-1 text-base font-semibold text-text-primary">
              {t('agents.provision.title', 'Add new agent')}
            </h2>
            <p className="mb-4 text-sm text-text-secondary">
              {t('agents.provision.subtitle')}
            </p>

            {provision.isPending && (
              <div className="flex items-center gap-2 py-8 text-sm text-text-secondary">
                <RefreshCw className="size-4 animate-spin" />
                {t('common.loading', 'Generating...')}
              </div>
            )}

            {provision.isError && (
              <p className="text-sm text-error">
                {t('common.error', 'Failed to generate OTP. Please retry.')}
              </p>
            )}

            {provision.isSuccess && provision.data && (() => {
              const { otp, expires_in_minutes: expires } = provision.data;
              // ADR-027: the GRID builds an installer that embeds this code and this host; the
              // browser saves it. Nothing is typed on the endpoint.
              const handleDownload = () => {
                try {
                  download.mutate(installerRequestBody({ host: window.location.hostname, otp }));
                } catch {
                  // host or code not what it should be: the error state below says so
                }
              };
              const status = (download.error as { response?: { status?: number } } | null)?.response?.status;
              return (
                <div className="space-y-4">
                  <div>
                    <p className="text-xs font-semibold uppercase tracking-wide text-text-secondary">
                      {t('agents.provision.otpLabel')}
                      <span className="ml-2 font-semibold text-[color:var(--ref-palette-orange-30)]">
                        {t('agents.provision.expires', { n: expires })}
                      </span>
                    </p>
                    <div className="mt-1 flex items-center gap-2">
                      <p className="font-mono text-sm text-text-primary" data-testid="enrollment-otp">{otp}</p>
                      <button
                        onClick={() => copyToClipboard(otp, 'otp')}
                        className="shrink-0 text-text-secondary hover:text-text-primary"
                        aria-label={t('agents.provision.copyOtp')}
                      >
                        {copiedField === 'otp' ? <CheckCircle2 className="size-4 text-success" /> : <Copy className="size-4" />}
                      </button>
                    </div>
                  </div>

                  <Button
                    variant="primary"
                    className="w-full"
                    onClick={handleDownload}
                    disabled={download.isPending}
                    data-testid="download-installer"
                  >
                    <Download className="mr-1.5 size-4" aria-hidden="true" />
                    {download.isPending ? t('agents.provision.downloading') : t('agents.provision.downloadButton')}
                  </Button>

                  {download.isError && (
                    <p role="alert" className="text-sm text-error">
                      {status === 404
                        ? t('agents.provision.noInstaller')
                        : status === 409
                          ? t('agents.provision.codeInvalid')
                          : t('agents.provision.downloadFailed')}
                    </p>
                  )}

                  <ol className="list-decimal space-y-1 pl-5 text-sm text-text-secondary">
                    <li>{t('agents.provision.step1')}</li>
                    <li>{t('agents.provision.step2')}</li>
                    <li>{t('agents.provision.step3')}</li>
                  </ol>
                  <p className="text-xs text-text-secondary">{t('agents.provision.scriptedNote')}</p>

                  <Button
                    variant="secondary"
                    size="sm"
                    onClick={handleProvision}
                    className="w-full"
                  >
                    <RefreshCw className="mr-1 size-3" />
                    {t('agents.provision.regenerate', 'Generate new OTP')}
                  </Button>
                </div>
              );
            })()}
          </div>
        </div>
      )}

    </div>
  );
}
