import { useTranslation } from 'react-i18next';

/**
 * PlaceholderPage — temporary stub for routes whose full implementation
 * is scheduled for later Sprint 7 days (Alerts/Agents: Day 4-5, Users:
 * Day 6-7, Audit: Day 6-7, Settings: deferred).
 *
 * Exists so the Sidebar (Organism #36) and routing/RBAC guards
 * (App.tsx) can be fully exercised in Day 1 without 404s for
 * authorized-but-not-yet-built routes.
 */

interface PlaceholderPageProps {
  titleKey: string;
  dayLabel: string;
}

export function PlaceholderPage({ titleKey, dayLabel }: PlaceholderPageProps) {
  const { t } = useTranslation();

  return (
    <div className="flex flex-col gap-2">
      <h1 className="text-2xl font-semibold text-text-primary">{t(titleKey)}</h1>
      <div className="mt-4 rounded-lg border border-dashed border-border-default bg-surface-subtle p-8 text-center text-sm text-text-tertiary">
        {dayLabel}
      </div>
    </div>
  );
}
