/**
 * CreateUserModal — US5.2
 * POST /dashboard/users → roles assigned immediately if provided.
 */

import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import { useTranslation } from 'react-i18next';
import { Button } from '@/components/ui/button';
import { useCreateUser } from '@/hooks/use-users';
import { KNOWN_ROLE_NAMES } from '@/lib/user-permissions';

const CreateUserSchema = z.object({
  email: z.string().email(),
  full_name: z.string().min(1).max(200),
  password: z.string().min(8).max(128),
  roles: z.array(z.string()),
});
type CreateUserForm = z.infer<typeof CreateUserSchema>;

const ROLE_LABEL_KEYS: Record<typeof KNOWN_ROLE_NAMES[number], string> = {
  tenant_admin: 'users.roles.tenantAdmin',
  security_analyst: 'users.roles.securityAnalyst',
  read_only_auditor: 'users.roles.readOnlyAuditor',
};

interface CreateUserModalProps {
  onClose: () => void;
  onSuccess: () => void;
}

export function CreateUserModal({ onClose, onSuccess }: CreateUserModalProps) {
  const { t } = useTranslation();
  const mutation = useCreateUser();

  const {
    register,
    handleSubmit,
    watch,
    setValue,
    formState: { errors, isSubmitting },
  } = useForm<CreateUserForm>({
    resolver: zodResolver(CreateUserSchema),
    defaultValues: { email: '', full_name: '', password: '', roles: [] },
  });

  const selectedRoles = watch('roles');

  function toggleRole(role: string) {
    const current = selectedRoles ?? [];
    if (current.includes(role)) {
      setValue('roles', current.filter((r) => r !== role));
    } else {
      setValue('roles', [...current, role]);
    }
  }

  async function onSubmit(data: CreateUserForm) {
    try {
      await mutation.mutateAsync(data);
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
      aria-label={t('users.createModal.title', 'Créer un utilisateur')}
      className="fixed inset-0 z-50 flex items-center justify-center bg-overlay p-4"
    >
      <div className="w-full max-w-md rounded-lg border border-border-default bg-elevated p-6 shadow-lg">
        <h2 className="mb-4 text-sm font-semibold text-text-primary">
          {t('users.createModal.title', 'Créer un utilisateur')}
        </h2>

        <form onSubmit={handleSubmit(onSubmit)} noValidate>
          {/* Email */}
          <div className="mb-4">
            <label htmlFor="create-email" className="mb-1 block text-xs font-medium text-text-secondary">
              {t('users.email', 'Email')}
            </label>
            <input
              {...register('email')}
              id="create-email"
              type="email"
              autoComplete="off"
              className="w-full rounded-md border border-border-default bg-surface px-3 py-2 text-sm text-text-primary placeholder-text-placeholder focus:border-primary focus:outline-none"
              placeholder="analyst@hopital.cm"
            />
            {errors.email && (
              <p className="mt-1 text-xs text-severity-critical">{t('auth.login.errors.emailInvalid')}</p>
            )}
          </div>

          {/* Full name */}
          <div className="mb-4">
            <label htmlFor="create-full-name" className="mb-1 block text-xs font-medium text-text-secondary">
              {t('users.fullName', 'Nom complet')}
            </label>
            <input
              {...register('full_name')}
              id="create-full-name"
              type="text"
              autoComplete="off"
              className="w-full rounded-md border border-border-default bg-surface px-3 py-2 text-sm text-text-primary placeholder-text-placeholder focus:border-primary focus:outline-none"
              placeholder="Jean Dupont"
            />
            {errors.full_name && (
              <p className="mt-1 text-xs text-severity-critical">{t('common.error')}</p>
            )}
          </div>

          {/* Password */}
          <div className="mb-4">
            <label htmlFor="create-password" className="mb-1 block text-xs font-medium text-text-secondary">
              {t('users.createModal.password', 'Mot de passe provisoire')}
            </label>
            <input
              {...register('password')}
              id="create-password"
              type="password"
              autoComplete="new-password"
              className="w-full rounded-md border border-border-default bg-surface px-3 py-2 text-sm text-text-primary placeholder-text-placeholder focus:border-primary focus:outline-none"
            />
            <p className="mt-1 text-xs text-text-secondary">
              {t('users.createModal.passwordHint', '8 caractères minimum')}
            </p>
            {errors.password && (
              <p className="mt-1 text-xs text-severity-critical">{t('common.error')}</p>
            )}
          </div>

          {/* Roles */}
          <fieldset className="mb-6">
            <legend className="mb-2 text-xs font-medium text-text-secondary">
              {t('users.createModal.roles', 'Rôles initiaux (facultatif)')}
            </legend>
            <div className="space-y-2">
              {KNOWN_ROLE_NAMES.map((role) => (
                <label key={role} className="flex cursor-pointer items-center gap-3">
                  <input
                    type="checkbox"
                    className="size-4 rounded"
                    checked={selectedRoles?.includes(role) ?? false}
                    onChange={() => toggleRole(role)}
                  />
                  <span className="text-sm text-text-primary">
                    {t(ROLE_LABEL_KEYS[role], role)}
                  </span>
                </label>
              ))}
            </div>
          </fieldset>

          {mutation.error && (
            <p className="mb-4 rounded-md bg-severity-critical-subtle px-3 py-2 text-xs text-severity-critical">
              {t('common.error', 'Une erreur est survenue')}
            </p>
          )}

          <div className="flex justify-end gap-3">
            <Button type="button" variant="ghost" onClick={onClose} disabled={isSubmitting}>
              {t('common.cancel', 'Annuler')}
            </Button>
            <Button type="submit" disabled={isSubmitting}>
              {isSubmitting
                ? t('common.loading', 'Chargement...')
                : t('users.createModal.submit', 'Créer')}
            </Button>
          </div>
        </form>
      </div>
    </div>
  );
}
