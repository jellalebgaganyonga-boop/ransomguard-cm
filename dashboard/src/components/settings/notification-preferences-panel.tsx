/**
 * E-mail notification preferences -- real on both sides: the GRID queues one e-mail per opted-in
 * recipient (grid-notifier sends it through SMTP). When this GRID has no SMTP account configured,
 * the panel says so: the switches are stored, they deliver nothing yet.
 */

import { useTranslation } from 'react-i18next';
import { AlertTriangle } from 'lucide-react';
import { useNotificationPreferences, useUpdateNotificationPreferences } from '@/hooks/use-notifications';
import { useToast } from '@/lib/toast-context';

type Field = 'email_critical' | 'email_high' | 'email_medium' | 'email_low';

export function NotificationPreferencesPanel() {
  const { t } = useTranslation();
  const toast = useToast();
  const { data: prefs, isLoading, isError } = useNotificationPreferences();
  const mutation = useUpdateNotificationPreferences();

  if (isLoading) {
    return <p className="text-text-muted">{t('common.loading')}</p>;
  }

  if (isError || !prefs) {
    return (
      <p role="alert" className="text-sm text-error">
        {t('settings.notifications.loadFailed', 'Les préférences n’ont pas pu être lues.')}
      </p>
    );
  }

  const toggle = async (field: Field) => {
    const next = { ...prefs, [field]: !prefs[field] };
    try {
      await mutation.mutateAsync({
        email_critical: next.email_critical,
        email_high: next.email_high,
        email_medium: next.email_medium,
        email_low: next.email_low,
      });
      toast.success(t('settings.notifications.saved'));
    } catch {
      toast.error(t('settings.notifications.saveFailed', 'Préférence non enregistrée.'));
    }
  };

  const rows: { labelKey: string; field: Field; color: string }[] = [
    { labelKey: 'severity.critical', field: 'email_critical', color: 'bg-red-600' },
    { labelKey: 'severity.high', field: 'email_high', color: 'bg-orange-500' },
    { labelKey: 'severity.medium', field: 'email_medium', color: 'bg-yellow-500' },
    { labelKey: 'severity.low', field: 'email_low', color: 'bg-blue-500' },
  ];

  return (
    <div className="space-y-3">
      {prefs.email_delivery_configured === false && (
        <div
          role="status"
          data-testid="email-delivery-not-configured"
          className="flex items-start gap-2 rounded-lg border border-severity-high-border bg-severity-high-subtle p-3 text-sm text-text-primary"
        >
          <AlertTriangle className="mt-0.5 size-4 shrink-0 text-warning" aria-hidden="true" />
          <span>{t('settings.notifications.deliveryNotConfigured')}</span>
        </div>
      )}

      <div className="rounded-lg border border-border-subtle bg-canvas">
        <table className="w-full">
          <thead>
            <tr className="border-b border-border-subtle">
              <th className="px-4 py-3 text-left text-sm font-medium text-text-muted">{t('settings.notifications.severity')}</th>
              <th className="px-4 py-3 text-center text-sm font-medium text-text-muted">{t('settings.notifications.email')}</th>
            </tr>
          </thead>
          <tbody>
            {rows.map(({ labelKey, field, color }) => (
              <tr key={field} className="border-b border-border-subtle last:border-0">
                <td className="flex items-center gap-2 px-4 py-3">
                  <span className={`inline-block h-3 w-3 rounded-full ${color}`} aria-hidden="true" />
                  <span className="text-sm font-medium text-text-primary">{t(labelKey)}</span>
                </td>
                <td className="px-4 py-3 text-center">
                  <button
                    type="button"
                    role="switch"
                    aria-checked={prefs[field]}
                    aria-label={`${t(labelKey)} — ${t('settings.notifications.email')}`}
                    disabled={mutation.isPending}
                    onClick={() => void toggle(field)}
                    className={`relative inline-flex h-6 w-11 items-center rounded-full transition-colors ${
                      prefs[field] ? 'bg-primary' : 'bg-gray-300'
                    }`}
                  >
                    <span
                      className={`inline-block h-4 w-4 rounded-full bg-white shadow transition-transform ${
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
    </div>
  );
}
