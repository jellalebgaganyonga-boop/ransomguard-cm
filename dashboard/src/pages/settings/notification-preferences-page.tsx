import { useTranslation } from 'react-i18next';
import {
  useNotificationPreferences,
  useUpdateNotificationPreferences,
} from '@/hooks/use-notifications';

export function NotificationPreferencesPage() {
  const { t } = useTranslation();
  const { data: prefs, isLoading } = useNotificationPreferences();
  const mutation = useUpdateNotificationPreferences();

  if (isLoading || !prefs) {
    return (
      <div className="p-6">
        <p className="text-text-muted">{t('common.loading')}</p>
      </div>
    );
  }

  const toggle = (field: 'email_critical' | 'email_high' | 'email_medium' | 'email_low') => {
    mutation.mutate({
      email_critical: prefs.email_critical,
      email_high: prefs.email_high,
      email_medium: prefs.email_medium,
      email_low: prefs.email_low,
      [field]: !prefs[field],
    });
  };

  const rows = [
    { labelKey: 'severity.critical', field: 'email_critical' as const, color: 'bg-red-600' },
    { labelKey: 'severity.high', field: 'email_high' as const, color: 'bg-orange-500' },
    { labelKey: 'severity.medium', field: 'email_medium' as const, color: 'bg-yellow-500' },
    { labelKey: 'severity.low', field: 'email_low' as const, color: 'bg-blue-500' },
  ];

  return (
    <div className="p-6 max-w-2xl">
      <h1 className="text-2xl font-bold text-text-primary mb-2">
        {t('settings.notifications.title')}
      </h1>
      <p className="text-text-muted mb-6">
        {t('settings.notifications.description')}
      </p>

      <div className="rounded-lg border border-border-subtle bg-canvas">
        <table className="w-full">
          <thead>
            <tr className="border-b border-border-subtle">
              <th className="px-4 py-3 text-left text-sm font-medium text-text-muted">
                {t('settings.notifications.severity')}
              </th>
              <th className="px-4 py-3 text-center text-sm font-medium text-text-muted">
                {t('settings.notifications.email')}
              </th>
            </tr>
          </thead>
          <tbody>
            {rows.map(({ labelKey, field, color }) => (
              <tr key={field} className="border-b border-border-subtle last:border-0">
                <td className="px-4 py-3 flex items-center gap-2">
                  <span className={`inline-block h-3 w-3 rounded-full ${color}`} />
                  <span className="text-sm font-medium text-text-primary">{t(labelKey)}</span>
                </td>
                <td className="px-4 py-3 text-center">
                  <button
                    type="button"
                    role="switch"
                    aria-checked={prefs[field]}
                    aria-label={`${t(labelKey)} email`}
                    onClick={() => toggle(field)}
                    className={`relative inline-flex h-6 w-11 items-center rounded-full transition-colors ${
                      prefs[field] ? 'bg-primary' : 'bg-gray-300'
                    }`}
                  >
                    <span
                      className={`inline-block h-4 w-4 rounded-full bg-white transition-transform ${
                        prefs[field] ? 'translate-x-6' : 'translate-x-1'
                      }`}
                    />
                  </button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      {mutation.isSuccess && (
        <p className="mt-4 text-sm text-green-600">
          {t('settings.notifications.saved')}
        </p>
      )}
    </div>
  );
}
