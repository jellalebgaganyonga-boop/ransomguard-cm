/**
 * "Réponse" -- what the agent did about the alert (ADR-026), and the two commands the console can
 * send: suspend the attributed process, resume it. A sent command shows the agent's own answer:
 * sent, received, executed, or not executed with the agent's reason. Nothing here claims an
 * effect the agent has not reported.
 */

import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Button } from '@/components/ui/button';
import type { AlertDetail } from '@/api/alerts';
import type { Role } from '@/api/me';
import { useIssueAlertCommand } from '@/hooks/use-alerts';
import { responseView } from '@/lib/alert-response';

type Pending = 'suspend_process' | 'resume_process' | null;

export function AlertResponse({ alert, role }: { alert: AlertDetail; role: Role }) {
  const { t } = useTranslation();
  const view = responseView(alert, role);
  const mutation = useIssueAlertCommand(alert.id);
  const [confirming, setConfirming] = useState<Pending>(null);

  // Nothing to say for an alert that carries neither attribution nor response nor command.
  if (!alert.attribution && view.state === 'none' && !view.lastCommand) return null;

  const send = async (type: Exclude<Pending, null>) => {
    const parameters: Record<string, unknown> = { alert_id: alert.id };
    if (type === 'resume_process' && view.suspensionId) parameters.suspension_id = view.suspensionId;
    try {
      await mutation.mutateAsync({ agent_id: alert.agent_id, command_type: type, parameters });
    } catch {
      return; // mutation.isError is shown below
    }
    setConfirming(null);
  };

  const who = view.processName
    ? `${view.processName}${view.processId != null ? ` (PID ${view.processId})` : ''}`
    : view.processId != null
      ? `PID ${view.processId}`
      : t('alerts.response.unnamedProcess', 'processus sans nom connu');

  let stateText: string;
  switch (view.state) {
    case 'suspended':
      stateText = t('alerts.response.suspended', 'Processus suspendu : {who} — en attente de votre décision, rien ne reprend tout seul', { who });
      break;
    case 'resumed':
      stateText = t('alerts.response.resumed', 'Processus repris sur votre demande : {who}', { who });
      break;
    case 'suspend_failed':
      stateText = t('alerts.response.suspendFailed', 'Suspension impossible : {who} tourne encore ({reason})', {
        who, reason: t(`alerts.response.reason.${view.reason ?? 'unknown'}`, view.reason ?? ''),
      });
      break;
    case 'isolation_recommended':
      stateText = t('alerts.response.isolationRecommended', "Plusieurs canaris touchés sans processus désigné : isolez ce poste (l'agent ne le fait pas)");
      break;
    default:
      stateText = alert.attribution?.state === 'resolved'
        ? t('alerts.response.noneResolved', 'Aucune réponse automatique sur cette alerte')
        : t('alerts.response.noneUnresolved', 'Aucune réponse possible : le processus n’est pas identifié, l’agent ne frappe pas au hasard');
  }

  const commandLabel = (type: string) =>
    type === 'suspend_process'
      ? t('alerts.response.command.suspend', 'Suspendre')
      : type === 'resume_process'
        ? t('alerts.response.command.resume', 'Reprendre')
        : type === 'isolate'
          ? t('alerts.response.command.isolate', 'Isoler')
          : type;

  return (
    <div className="mb-6 border-t border-border-subtle pt-5" data-testid="alert-response" data-state={view.state}>
      <h3 className="mb-2 text-xs font-semibold uppercase tracking-wide text-text-secondary">
        {t('alerts.response.title', 'Réponse')}
      </h3>
      <p className="text-text-primary">{stateText}</p>

      {view.lastCommand && (
        <p className="mt-1 text-sm text-text-secondary" data-testid="alert-command-status" data-status={view.lastCommand.status}>
          {t('alerts.response.commandLine', 'Commande « {command} » : {status}', {
            command: commandLabel(view.lastCommand.type),
            status: t(`alerts.response.status.${view.lastCommand.status}`, view.lastCommand.status),
          })}
          {view.lastCommand.status === 'not_executed' && view.lastCommand.reason && (
            <span> — {t(`alerts.response.reason.${view.lastCommand.reason}`, view.lastCommand.reason)}</span>
          )}
        </p>
      )}

      {(view.canSuspend || view.canResume) && (
        <div className="mt-3 flex flex-wrap gap-2">
          {confirming === null ? (
            <>
              {view.canSuspend && (
                <Button variant="primary" onClick={() => setConfirming('suspend_process')}>
                  {t('alerts.response.suspendButton', 'Suspendre le processus détecté')}
                </Button>
              )}
              {view.canResume && (
                <Button variant="secondary" onClick={() => setConfirming('resume_process')}>
                  {t('alerts.response.resumeButton', 'Reprendre le processus')}
                </Button>
              )}
            </>
          ) : (
            <>
              <Button variant="primary" onClick={() => void send(confirming)} disabled={mutation.isPending}>
                {confirming === 'suspend_process'
                  ? t('alerts.response.confirmSuspend', 'Confirmer la suspension')
                  : t('alerts.response.confirmResume', 'Confirmer la reprise')}
              </Button>
              <Button variant="secondary" onClick={() => setConfirming(null)} disabled={mutation.isPending}>
                {t('common.cancel', 'Annuler')}
              </Button>
            </>
          )}
        </div>
      )}

      {mutation.isError && (
        <p role="alert" className="mt-2 text-sm text-error">
          {t('alerts.response.sendFailed', "La commande n'a pas pu être envoyée")}
        </p>
      )}
    </div>
  );
}
