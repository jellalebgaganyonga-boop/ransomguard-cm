/**
 * RoleEditorModal — US5.4
 *
 * The list sent replaces every role the user has, so the dialog shows what they have now,
 * ticked, and an administrator edits from there. It used to open with everything unchecked and
 * a notice saying current roles could not be shown; GET /dashboard/users-with-roles returns
 * them, admin-only and tenant-scoped, and had done for some time.
 *
 * When something goes wrong it says what the server said. "An error occurred" was shown on
 * 2026-10-27 over role changes that had succeeded, which is how long it took to notice.
 *
 * See lib/user-permissions.ts for canUpdateRoles() self-demotion guard.
 */

import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Info } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { useUpdateUserRoles, useUsersWithRoles } from '@/hooks/use-users';
import { apiErrorMessage } from '@/lib/api-error';
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
  const current = useUsersWithRoles();
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [selfDemotionError, setSelfDemotionError] = useState(false);
  const [touched, setTouched] = useState(false);

  // Pre-tick what this user has. Only until the administrator touches something: re-ticking a
  // box they have just cleared, because a refetch landed, is the dialog arguing with them.
  const loadedRoles = current.data?.items.find((u) => u.id === userId)?.roles;
  useEffect(() => {
    if (!touched && loadedRoles) setSelected(new Set(loadedRoles));
  }, [touched, loadedRoles]);

  function toggle(role: string) {
    setTouched(true);
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
      aria-label={t('users.roles.title', 'Assign roles')}
      className="fixed inset-0 z-50 flex items-center justify-center bg-overlay p-4"
    >
      <div className="w-full max-w-sm rounded-lg border border-border-default bg-elevated p-6 shadow-lg">
        <h2 className="mb-1 text-sm font-semibold text-text-primary">
          {t('users.roles.title', 'Assign roles')}
        </h2>
        <p className="mb-4 font-mono text-xs text-text-tertiary">{userEmail}</p>

        <div className="mb-4 flex items-start gap-2 rounded-md border border-primary-border bg-primary-subtle px-3 py-2">
          <Info className="mt-0.5 size-4 shrink-0 text-primary" aria-hidden="true" />
          <p className="text-xs text-text-secondary">
            {t(
              'users.roles.replacesAll',
              'Ticked below is what this user has now. Saving replaces all of their roles with what is ticked.',
            )}
          </p>
        </div>

        {current.isError && (
          <p className="mb-4 rounded-md bg-severity-critical-subtle px-3 py-2 text-xs text-severity-critical-on-subtle">
            {apiErrorMessage(current.error) ??
              t('users.roles.currentUnavailable', 'The roles this user has now could not be read.')}
          </p>
        )}

        {/* Role checkboxes */}
        <fieldset className="mb-6 space-y-3">
          <legend className="mb-2 text-xs font-medium text-text-secondary">
            {t('users.roles.chooseRoles', 'Select roles:')}
          </legend>
          {current.isLoading && (
            <p className="text-xs text-text-tertiary">
              {t('users.roles.loadingCurrent', 'Reading current roles...')}
            </p>
          )}
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
          <p className="mb-4 rounded-md bg-severity-critical-subtle px-3 py-2 text-xs text-severity-critical-on-subtle">
            {t(
              'users.roles.selfDemotionError',
              'You cannot remove the tenant_admin role from your own account.',
            )}
          </p>
        )}

        {mutation.error && (
          <p role="alert" className="mb-4 rounded-md bg-severity-critical-subtle px-3 py-2 text-xs text-severity-critical-on-subtle">
            {apiErrorMessage(mutation.error) ?? t('common.error', 'An error occurred')}
          </p>
        )}

        <div className="flex justify-end gap-3">
          <Button variant="ghost" onClick={onClose} disabled={mutation.isPending}>
            {t('common.cancel', 'Cancel')}
          </Button>
          <Button onClick={handleSubmit} disabled={mutation.isPending}>
            {mutation.isPending
              ? t('common.loading', 'Loading...')
              : t('users.roles.submit', 'Save roles')}
          </Button>
        </div>
      </div>
    </div>
  );
}
