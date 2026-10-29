/**
 * "Suspended Processes" on the agent page (ADR-026, B1.6): the processes this endpoint says are
 * frozen right now, and the one action a human can take on them — resume.
 *
 * Everything here comes from what the agent reported. A process the agent has since resumed, or
 * one that exited by itself, leaves the list because the agent said so: the console never shows a
 * freeze it cannot still vouch for. A resume that failed stays, and says plainly that nothing can
 * unfreeze the process before the machine restarts.
 *
 * The resume goes through the alert command (tenant_admin only), where the GRID takes the PID from
 * the alert's own attribution: the console names the suspension, never a process of its choosing.
 */

import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { AlertTriangle, Snowflake } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { useAgentSuspensions, useIssueCommand } from '@/hooks/use-agents';
import { formatDistanceToNow } from '@/lib/format-date';
import type { Role } from '@/api/me';
import type { SuspendedProcess } from '@/api/agents';

export function SuspendedProcessesSection({ agentId, role }: { agentId: string; role: Role }) {
  const { t } = useTranslation();
  const { data: suspensions } = useAgentSuspensions(agentId);

  // Nothing frozen: no section at all, rather than an empty box suggesting a feature is missing.
  if (!suspensions || suspensions.length === 0) return null;

  return (
    <section
      className="rounded-lg border border-border-subtle bg-canvas p-4"
      data-testid="suspended-processes"
      aria-labelledby="suspended-processes-title"
    >
      <h2 id="suspended-processes-title" className="mb-1 flex items-center gap-2 text-lg font-semibold text-text-primary">
        <Snowflake className="size-5 text-severity-critical" aria-hidden="true" />
        {t('agents.suspended.title')}
      </h2>
      <p className="mb-4 text-sm text-text-secondary">{t('agents.suspended.subtitle')}</p>

      <ul className="flex flex-col gap-3">
        {suspensions.map((suspension) => (
          <SuspendedRow key={suspension.suspension_id} agentId={agentId} suspension={suspension} role={role} />
        ))}
      </ul>
    </section>
  );
}

function SuspendedRow({
  agentId,
  suspension,
  role,
}: {
  agentId: string;
  suspension: SuspendedProcess;
  role: Role;
}) {
  const { t } = useTranslation();
  const mutation = useIssueCommand();
  const [confirming, setConfirming] = useState(false);

  const who = suspension.process_name
    ? `${suspension.process_name}${suspension.process_id != null ? ` (PID ${suspension.process_id})` : ''}`
    : suspension.process_id != null
      ? `PID ${suspension.process_id}`
      : t('agents.suspended.unnamedProcess');

  const resume = async () => {
    try {
      await mutation.mutateAsync({
        agent_id: agentId,
        command_type: 'resume_process',
        parameters: { alert_id: suspension.alert_id, suspension_id: suspension.suspension_id },
      });
    } catch {
      return; // the failure is shown below; nothing is claimed
    }
    setConfirming(false);
  };

  return (
    <li className="rounded-md border border-border-subtle p-3" data-testid="suspended-process">
      <p className="font-medium text-text-primary">{who}</p>
      <dl className="mt-1 grid gap-x-6 gap-y-1 text-sm text-text-secondary sm:grid-cols-[auto_1fr]">
        <dt>{t('agents.suspended.since')}</dt>
        <dd data-testid="suspended-since">{formatDistanceToNow(suspension.suspended_at)}</dd>
        {suspension.canary_path && (
          <>
            <dt>{t('agents.suspended.file')}</dt>
            <dd className="break-all font-mono text-xs">{suspension.canary_path}</dd>
          </>
        )}
        {suspension.escalations > 0 && (
          <>
            <dt>{t('agents.suspended.escalations')}</dt>
            <dd>{suspension.escalations}</dd>
          </>
        )}
      </dl>

      {suspension.resume_failed && (
        <p className="mt-2 flex items-start gap-2 text-sm text-error" role="alert" data-testid="resume-failed">
          <AlertTriangle className="mt-0.5 size-4 shrink-0" aria-hidden="true" />
          {t('agents.suspended.resumeFailed')}
        </p>
      )}

      {role === 'tenant_admin' && !suspension.resume_failed && (
        <div className="mt-3">
          {confirming ? (
            <div className="rounded-md border border-border-subtle bg-surface-subtle p-3" data-testid="resume-confirm">
              <p className="mb-3 text-sm text-text-primary">{t('agents.suspended.confirmBody')}</p>
              <div className="flex flex-wrap gap-2">
                <Button variant="primary" onClick={() => void resume()} disabled={mutation.isPending}>
                  {t('agents.suspended.confirmResume')}
                </Button>
                <Button variant="secondary" onClick={() => setConfirming(false)} disabled={mutation.isPending}>
                  {t('common.cancel')}
                </Button>
              </div>
            </div>
          ) : (
            <Button variant="secondary" onClick={() => setConfirming(true)} data-testid="resume-process">
              {t('agents.suspended.resume')}
            </Button>
          )}
          {mutation.isError && (
            <p className="mt-2 text-sm text-error" role="alert">
              {t('agents.suspended.sendFailed')}
            </p>
          )}
          {mutation.isSuccess && (
            <p className="mt-2 text-sm text-text-secondary">{t('agents.suspended.asked')}</p>
          )}
        </div>
      )}
    </li>
  );
}
