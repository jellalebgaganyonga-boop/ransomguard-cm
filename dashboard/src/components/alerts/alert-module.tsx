/**
 * MODULE of an alert: the module that raised it (stable code from the GRID, translated FR/EN),
 * then the event type underneath. Used by the alert list (MODULE column) and the alert detail.
 *
 * Never an empty cell: a missing module reads as UNKNOWN ("Module inconnu"), and a code this
 * console does not know yet is shown as is.
 */

import { useTranslation } from 'react-i18next';

interface AlertModuleProps {
  module?: string | null | undefined;
  alertType: string;
  /** "cell": compact, for the list; "field": larger, for the detail page. */
  layout?: 'cell' | 'field';
}

export function AlertModule({ module, alertType, layout = 'cell' }: AlertModuleProps) {
  const { t } = useTranslation();
  const code = module || 'UNKNOWN';

  return (
    <>
      <span
        className={
          layout === 'cell'
            ? 'font-semibold text-text-primary'
            : 'mt-0.5 block font-semibold text-text-primary'
        }
      >
        {t(`modules.${code}`, code)}
      </span>
      <span
        className={
          layout === 'cell'
            ? 'block text-[10px] text-text-secondary'
            : 'block font-mono text-[10px] text-text-secondary'
        }
      >
        {alertType}
      </span>
    </>
  );
}
