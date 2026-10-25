/**
 * The language of the console, chosen by the user: stored on the GRID (/dashboard/me/preferences),
 * applied at once, kept in this browser (i18next detector cache) and applied at every sign-in
 * (protected-route.tsx). One hook for the two places that offer the choice: the Settings page and
 * the header user menu.
 */

import { useTranslation } from 'react-i18next';
import { setLanguage as applyLanguage } from '@/i18n';
import { useUpdatePreferences } from '@/hooks/use-me';
import { useToast } from '@/lib/toast-context';
import { useUIStore } from '@/stores/ui.store';

export type Language = 'en' | 'fr';

export function useLanguage() {
  const { t, i18n } = useTranslation();
  const toast = useToast();
  const mutation = useUpdatePreferences();
  const setLanguage = useUIStore((s) => s.setLanguage);
  const current: Language = (i18n.resolvedLanguage ?? 'en').startsWith('fr') ? 'fr' : 'en';

  /** Stores the choice on the GRID, then applies it; a failed store is said, nothing changes. */
  const choose = async (language: Language): Promise<void> => {
    try {
      await mutation.mutateAsync({ language });
      await applyLanguage(language);
      setLanguage(language);
      toast.success(t('settings.language.saved'));
    } catch {
      toast.error(t('settings.language.saveFailed'));
    }
  };

  return { current, other: current === 'fr' ? ('en' as const) : ('fr' as const), choose, isPending: mutation.isPending };
}
