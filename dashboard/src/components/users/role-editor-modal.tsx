/**
 * RoleEditorModal — US5.4
 *
 * GAP-08: GET /dashboard/users does NOT return roles in UserItem.
 * Only PUT /users/{id}/roles response includes roles (UserWithRolesItem).
 * This modal therefore starts with ALL checkboxes unchecked and shows
 * a warning. Admin must explicitly set the desired roles.
 *
 * See lib/user-permissions.ts for canUpdateRoles() self-demotion guard.
 */

import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Info } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { useUpdateUserRoles } from '@/hooks/use-users';
import { KNOWN_ROLE_NAMES, canUpdateRoles } from '@/lib/user-permissions';

interface RoleEditorModalProps {
  userId: string;
  userEmail: string;
  currentUserId: string;
  onClose: () => void;
  onSuccess: () => void;
}

const ROLE_LABEL_KEYS: Record<typeof KNOWN_ROLE_NAMES[number], string> = {
  tenant_admin: 'users.roles.tenantAdmin',
  security_analyst: 'users.roles.securityAnalyst',
  read_only_auditor: 'users.roles.readOnlyAuditor',
};

export function RoleEditorModal({
  userId,
  userEmail,
  currentUserId,
  onClose,
  onSuccess,
}: RoleEditorModalProps) {
  const { t } = useTranslation();
  const mutation = useUpdateUserRoles();
  // GAP-08: start with nothing checked — current roles not available from list endpoint
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [selfDemotionError, setSelfDemotionError] = useState(false);

  function toggle(role: string) {
    setSelected((prev) => {
      const next = new Set(prev);
      if (next.has(role)) next.delete(role);
      else next.add(role);
      return next;
    });
    setSelfDemotionError(false);
  }

  async function handleSubmit() {
    const newRoles = Array.from(selected);
    if (!canUpdateRoles(userId, currentUserId, newRoles)) {
      setSelfDemotionError(true);
      return;
    }
    try {
      await mutation.mutateAsync({ userId, payload: { role_names: newRoles } });
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
      aria-label={t('users.roles.title', 'Attribuer des rôles')}
      className="fixed inset-0 z-50 flex items-center justify-center bg-overlay p-4"
    >
      <div className="w-full max-w-sm rounded-lg border border-border-default bg-elevated p-6 shadow-lg">
        <h2 className="mb-1 text-sm font-semibold text-text-primary">
          {t('users.roles.title', 'Attribuer des rôles')}
        </h2>
        <p className="mb-4 font-mono text-xs text-text-tertiary">{userEmail}</p>

        {/* GAP-08 warning */}
        <div className="mb-4 flex items-start gap-2 rounded-md border border-primary-border bg-primary-subtle px-3 py-2">
          <Info className="mt-0.5 size-4 shrink-0 text-primary" aria-hidden="true" />
          <p className="text-xs text-text-secondary">
            {t(
              'users.roles.gapWarning',
              "Les rôles actuels ne sont pas affichés (limitation backend connue). Cochez les rôles que cet utilisateur doit avoir — la liste ci-dessous remplacera l'intégralité des rôles existants.",
            )}
          </p>
        </div>

        {/* Role checkboxes */}
        <fieldset className="mb-6 space-y-3">
          <legend className="mb-2 text-xs font-medium text-text-secondary">
            {t('users.roles.chooseRoles', 'Sélectionnez les rôles :')}
          </legend>
          {KNOWN_ROLE_NAMES.map((role) => (
            <label key={role} className="flex cursor-pointer items-center gap-3">
              <input
                type="checkbox"
                className="size-4 rounded"
                checked={selected.has(role)}
                onChange={() => toggle(role)}
              />
              <span className="text-sm text-text-primary">
                {t(ROLE_LABEL_KEYS[role], role)}
              </span>
            </label>
          ))}
        </fieldset>

        {selfDemotionError && (
          <p className="mb-4 rounded-md bg-severity-critical-subtle px-3 py-2 text-xs text-severity-critical">
            {t(
              'users.roles.selfDemotionError',
              'Vous ne pouvez pas retirer le rôle tenant_admin de votre propre compte.',
            )}
          </p>
        )}

        {mutation.error && (
          <p className="mb-4 rounded-md bg-severity-critical-subtle px-3 py-2 text-xs text-severity-critical">
            {t('common.error', 'Une erreur est survenue')}
          </p>
        )}

        <div className="flex justify-end gap-3">
          <Button variant="ghost" onClick={onClose} disabled={mutation.isPending}>
            {t('common.cancel', 'Annuler')}
          </Button>
          <Button onClick={handleSubmit} disabled={mutation.isPending}>
            {mutation.isPending
              ? t('common.loading', 'Chargement...')
              : t('users.roles.submit', 'Enregistrer les rôles')}
          </Button>
        </div>
      </div>
    </div>
  );
}
