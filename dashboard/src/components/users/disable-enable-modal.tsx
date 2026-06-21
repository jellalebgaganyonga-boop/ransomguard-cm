/**
 * DisableEnableModal — US5.3
 *
 * GAP-05 / GRID-SEC-002:
 * For the "disable" mode, this component carries the ONLY protection against
 * self-lockout. The backend does NOT block self-disable (unlike enable, which
 * returns 422 on self-modify). If the impossible case occurs where the modal
 * is opened for self-disable (e.g., direct URL manipulation bypassing the
 * hidden button), the modal blocks the request entirely instead of proceeding.
 *
 * See lib/user-permissions.ts canDisable() for the authoritative guard.
 */

import { useTranslation } from 'react-i18next';
import { AlertTriangle } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { useDisableUser, useEnableUser } from '@/hooks/use-users';
import { canDisable } from '@/lib/user-permissions';

interface DisableEnableModalProps {
  mode: 'disable' | 'enable';
  userId: string;
  userEmail: string;
  currentUserId: string;
  onClose: () => void;
  onSuccess: () => void;
}

export function DisableEnableModal({
  mode,
  userId,
  userEmail,
  currentUserId,
  onClose,
  onSuccess,
}: DisableEnableModalProps) {
  const { t } = useTranslation();
  const disableMutation = useDisableUser();
  const enableMutation = useEnableUser();
  const mutation = mode === 'disable' ? disableMutation : enableMutation;

  // GAP-05 / GRID-SEC-002 defense-in-depth:
  // If this modal is somehow opened for self-disable (button should be hidden),
  // block immediately — backend has no server-side guard for this case.
  if (mode === 'disable' && !canDisable(userId, currentUserId)) {
    return (
      <div
        role="dialog"
        aria-modal="true"
        aria-label={t('users.disableModal.selfErrorTitle', 'Action impossible')}
        className="fixed inset-0 z-50 flex items-center justify-center bg-overlay p-4"
      >
        <div className="w-full max-w-sm rounded-lg border border-severity-critical-border bg-elevated p-6 shadow-lg">
          <div className="mb-4 flex items-center gap-3">
            <AlertTriangle className="size-5 text-severity-critical" aria-hidden="true" />
            <h2 className="text-sm font-semibold text-text-primary">
              {t('users.disableModal.selfErrorTitle', 'Action impossible')}
            </h2>
          </div>
          <p className="mb-6 text-sm text-text-secondary">
            {t(
              'users.disableModal.selfErrorBody',
              'Vous ne pouvez pas désactiver votre propre compte. Cette action est bloquée pour éviter tout verrouillage accidentel.',
            )}
          </p>
          <Button variant="secondary" className="w-full" onClick={onClose}>
            {t('common.close', 'Fermer')}
          </Button>
        </div>
      </div>
    );
  }

  const isDisable = mode === 'disable';

  async function handleConfirm() {
    try {
      if (isDisable) {
        await disableMutation.mutateAsync(userId);
      } else {
        await enableMutation.mutateAsync(userId);
      }
      onSuccess();
      onClose();
    } catch {
      // error displayed via mutation.error
    }
  }

  return (
    <div
      role="dialog"
      aria-modal="true"
      aria-label={isDisable ? t('users.disableModal.title') : t('users.enableModal.title')}
      className="fixed inset-0 z-50 flex items-center justify-center bg-overlay p-4"
    >
      <div className="w-full max-w-sm rounded-lg border border-border-default bg-elevated p-6 shadow-lg">
        {/* Warning banner for disable */}
        {isDisable && (
          <div className="mb-4 flex items-start gap-2 rounded-md border border-severity-high-border bg-severity-high-subtle px-3 py-2">
            <AlertTriangle className="mt-0.5 size-4 shrink-0 text-severity-high" aria-hidden="true" />
            <p className="text-xs text-text-primary">
              {t('users.disableModal.warning', "L'utilisateur ne pourra plus se connecter immédiatement.")}
            </p>
          </div>
        )}

        <h2 className="mb-2 text-sm font-semibold text-text-primary">
          {isDisable ? t('users.disableModal.title', 'Désactiver cet utilisateur ?') : t('users.enableModal.title', 'Réactiver cet utilisateur ?')}
        </h2>
        <p className="mb-6 text-sm text-text-secondary">
          <span className="font-mono font-semibold">{userEmail}</span>
          {isDisable
            ? ` ${t('users.disableModal.body', 'sera immédiatement désactivé.')}`
            : ` ${t('users.enableModal.body', 'pourra de nouveau se connecter.')}`}
        </p>

        {mutation.error && (
          <p className="mb-4 rounded-md bg-severity-critical-subtle px-3 py-2 text-xs text-severity-critical">
            {t('common.error', 'Une erreur est survenue')}
          </p>
        )}

        <div className="flex justify-end gap-3">
          <Button variant="ghost" onClick={onClose} disabled={mutation.isPending}>
            {t('common.cancel', 'Annuler')}
          </Button>
          <Button
            variant={isDisable ? 'danger' : 'primary'}
            onClick={handleConfirm}
            disabled={mutation.isPending}
          >
            {mutation.isPending
              ? t('common.loading', 'Chargement...')
              : isDisable
                ? t('users.disableModal.submit', 'Désactiver')
                : t('users.enableModal.submit', 'Réactiver')}
          </Button>
        </div>
      </div>
    </div>
  );
}
