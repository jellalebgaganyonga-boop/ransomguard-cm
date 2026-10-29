import { useTranslation } from 'react-i18next';
import { ShieldOff } from 'lucide-react';

/**
 * ForbiddenPage — rendered by ProtectedRoute (App.tsx) when a user's
 * resolved role does not include the requested route (client-side UX
 * guard only — server-side RBAC, STRIDE WT4.2/WT4.5, remains
 * authoritative for every API call).
 */
export function ForbiddenPage() {
  const { t } = useTranslation();

  return (
    <div className="flex min-h-[60vh] flex-col items-center justify-center gap-4 text-center">
      <ShieldOff className="size-12 text-severity-critical" aria-hidden="true" />
      <h1 className="text-xl font-semibold text-text-primary">
        {t('errors.forbidden.title')}
      </h1>
      <p className="max-w-sm text-sm text-text-secondary">
        {t('errors.forbidden.description')}
      </p>
    </div>
  );
}
