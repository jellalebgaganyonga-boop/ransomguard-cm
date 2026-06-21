/**
 * CommandModal — US4.3
 *
 * AC4.3.1: confirmation modal for "Isolate endpoint" command
 *   - Warning text (destructive — red)
 *   - Reason field (required, 20–500 chars)
 *   - Acknowledgement checkbox ("I understand this will disconnect the endpoint")
 *   - Submits: POST /commands { agent_id, command_type: "isolate", parameters: { reason } }
 *
 * AC4.3.2: only tenant_admin can see/use this component (enforced by parent + backend 403).
 *
 * NOTE: "restore" command (AC4.3.3) is descoped to Sprint 8.
 * AgentStatus has no "isolated" value — there is no way to detect isolation
 * from agent status alone. Restore would require a dedicated status value
 * or a separate command history endpoint (neither exists in Sprint 7 backend).
 *
 * Backend payload contract (confirmed from schemas/dashboard.py):
 *   { agent_id: str, command_type: str, parameters: dict[str, Any] }
 *   NOT { agent_id, command_type, justification }
 */

import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { AlertTriangle } from 'lucide-react';
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogFooter,
} from '@/components/ui/dialog';
import { Textarea } from '@/components/ui/textarea';
import { Label } from '@/components/ui/label';
import { Checkbox } from '@/components/ui/checkbox';
import { Button } from '@/components/ui/button';
import { useIssueCommand } from '@/hooks/use-agents';

interface CommandModalProps {
  agentId: string;
  hostname: string;
  open: boolean;
  onOpenChange: (open: boolean) => void;
  onSuccess?: () => void;
}

const REASON_MIN = 20;
const REASON_MAX = 500;

export function CommandModal({
  agentId,
  hostname,
  open,
  onOpenChange,
  onSuccess,
}: CommandModalProps) {
  const { t } = useTranslation();
  const [reason, setReason] = useState('');
  const [acknowledged, setAcknowledged] = useState(false);
  const mutation = useIssueCommand();

  const reasonValid = reason.length >= REASON_MIN && reason.length <= REASON_MAX;
  const formValid = reasonValid && acknowledged;

  const handleConfirm = async () => {
    if (!formValid) return;
    await mutation.mutateAsync({
      agent_id: agentId,
      command_type: 'isolate',
      parameters: { reason },
    });
    setReason('');
    setAcknowledged(false);
    onOpenChange(false);
    onSuccess?.();
  };

  const handleOpenChange = (next: boolean) => {
    if (!next) {
      setReason('');
      setAcknowledged(false);
    }
    onOpenChange(next);
  };

  return (
    <Dialog open={open} onOpenChange={handleOpenChange}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle className="flex items-center gap-2 text-error">
            <AlertTriangle className="size-5" aria-hidden="true" />
            {t('agents.command.title', 'Isoler le terminal')}
          </DialogTitle>
        </DialogHeader>

        <div className="flex flex-col gap-4 p-5">
          {/* Warning banner */}
          <div className="rounded-lg border border-severity-critical-border bg-severity-critical-subtle px-4 py-3 text-sm text-severity-critical">
            <p className="font-semibold">{t('agents.command.warningTitle', 'Action destructrice')}</p>
            <p className="mt-1">
              {t('agents.command.warningBody', {
                hostname,
                defaultValue: `L'isolation de "${hostname}" coupera immédiatement l'accès réseau de cet agent. Il sera inaccessible jusqu'à ce qu'un opérateur effectue une restauration physique ou logicielle.`,
              })}
            </p>
          </div>

          {/* Reason */}
          <div>
            <Label htmlFor="cmd-reason">
              {t('agents.command.reasonLabel', 'Motif (obligatoire)')}{' '}
              <span className="text-error" aria-hidden="true">*</span>
            </Label>
            <Textarea
              id="cmd-reason"
              value={reason}
              onChange={(e) => setReason(e.target.value)}
              placeholder={t(
                'agents.command.reasonPlaceholder',
                'Ex: Suspicion de compromission — ransomware détecté en cours d\'exécution...'
              )}
              hasError={reason.length > 0 && !reasonValid}
              className="mt-1.5"
              maxLength={REASON_MAX}
              rows={3}
            />
            <div className="mt-1 flex justify-between text-xs">
              <span
                className={
                  reason.length > 0 && reason.length < REASON_MIN
                    ? 'text-warning'
                    : 'text-text-tertiary'
                }
              >
                {reason.length > 0 && reason.length < REASON_MIN
                  ? t('agents.command.minChars', {
                      count: REASON_MIN - reason.length,
                      defaultValue: `${REASON_MIN - reason.length} caractères minimum`,
                    })
                  : ''}
              </span>
              <span className="text-text-tertiary">
                {reason.length} / {REASON_MAX}
              </span>
            </div>
          </div>

          {/* Acknowledgement checkbox */}
          <div className="flex items-start gap-3">
            <Checkbox
              id="cmd-ack"
              checked={acknowledged}
              onCheckedChange={(v) => setAcknowledged(v === true)}
              className="mt-0.5"
            />
            <Label htmlFor="cmd-ack" className="cursor-pointer leading-snug text-text-primary">
              {t(
                'agents.command.ackLabel',
                "Je comprends que cette action déconnectera immédiatement le terminal du réseau."
              )}
            </Label>
          </div>
        </div>

        <DialogFooter>
          <Button variant="secondary" onClick={() => handleOpenChange(false)}>
            {t('common.cancel', 'Annuler')}
          </Button>
          <Button
            variant="primary"
            onClick={handleConfirm}
            isLoading={mutation.isPending}
            disabled={!formValid}
            className="bg-error text-white hover:bg-error/90"
          >
            {t('agents.command.submit', 'Isoler le terminal')}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
