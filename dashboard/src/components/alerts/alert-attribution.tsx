/**
 * "Qui l'a fait ?" -- the attribution of a SENTINEL alert (ADR-025). A wrong process is worse than
 * none: when nobody is named, the reason says why, in plain words.
 */

import type { ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import type { AlertAttribution as Attribution } from '@/api/alerts';
import { attributionView } from '@/lib/alert-attribution';
import { formatDateTime } from '@/lib/format-date';

export function AlertAttribution({ attribution, detectedAt }: { attribution: Attribution | null | undefined; detectedAt: string }) {
  const { t } = useTranslation();
  const view = attributionView(attribution, detectedAt);
  if (!view) return null;

  let body: ReactNode;
  switch (view.kind) {
    case 'pending':
      body = <p className="text-text-secondary">{t('alerts.attribution.pending', 'Attribution in progress…')}</p>;
      break;
    case 'not_received':
      body = <p className="text-text-secondary">{t('alerts.attribution.notReceived', 'Attribution not received')}</p>;
      break;
    case 'resolved':
      body = (
        <div>
          <p className="font-semibold text-text-primary">
            {view.processName ?? t('alerts.attribution.unnamed', 'process with no known name')}
            {view.processId != null && (
              <span className="font-normal text-text-secondary"> — PID {view.processId}</span>
            )}
          </p>
          {view.createdAt && (
            <p className="text-xs text-text-secondary">
              {t('alerts.attribution.startedAt', 'started {when}', { when: formatDateTime(view.createdAt) })}
            </p>
          )}
          {view.imagePath && <p className="mt-1 break-all font-mono text-xs text-text-secondary">{view.imagePath}</p>}
        </div>
      );
      break;
    case 'ambiguous':
      body = (
        <p className="text-text-primary">
          {t('alerts.attribution.ambiguous', "Several processes wrote this file: none is named")}
        </p>
      );
      break;
    default:
      body = (
        <p className="text-text-primary">
          {t('alerts.attribution.unavailable', 'Process not identified')}
          {view.reason && (
            <span className="text-text-secondary"> — {t(`alerts.attribution.reason.${view.reason}`, view.reason)}</span>
          )}
        </p>
      );
  }

  return (
    <div className="mb-6 border-t border-border-subtle pt-5" data-testid="alert-attribution" data-kind={view.kind}>
      <h3 className="mb-2 text-xs font-semibold uppercase tracking-wide text-text-secondary">
        {t('alerts.attribution.title', "Qui l'a fait ?")}
      </h3>
      {body}
    </div>
  );
}
