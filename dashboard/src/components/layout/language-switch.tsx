/**
 * The EN / FR switch.
 *
 * The console is bilingual and English by default, and until 2026-10-28 there was no way for a
 * user to say so: the translations were complete, loaded, and unreachable behind a build flag.
 *
 * Two placements, and they need different behaviour. Signed in, the choice belongs to the
 * account and is stored on the GRID, so it follows the user to another machine. On the sign-in
 * page there is no account yet, so the choice is this browser's alone — and it has to work
 * there, because somebody who reads French needs the sign-in page in French to begin with.
 */

import { useTranslation } from 'react-i18next';
import { Globe } from 'lucide-react';
import { setLanguage } from '@/i18n';
import { useLanguage, type Language } from '@/hooks/use-language';

const LANGUAGES: { code: Language; label: string }[] = [
  { code: 'en', label: 'EN' },
  // A language is called what it calls itself. french-ok: the French language's own name
  { code: 'fr', label: 'FR' },
];

interface LanguageSwitchProps {
  /** Sign-in page: no account to store the choice against, so keep it to this browser. */
  localOnly?: boolean;
  className?: string;
}

export function LanguageSwitch({ localOnly = false, className = '' }: LanguageSwitchProps) {
  const { t, i18n } = useTranslation();
  const account = useLanguage();
  const current: Language = (i18n.resolvedLanguage ?? 'en').startsWith('fr') ? 'fr' : 'en';

  async function pick(code: Language) {
    if (code === current) return;
    if (localOnly) await setLanguage(code);
    else await account.choose(code);
  }

  return (
    <div
      role="group"
      aria-label={t('header.language', 'Language')}
      className={`flex items-center gap-1 rounded-md border border-border-default p-0.5 ${className}`}
    >
      <Globe className="ml-1 size-3.5 shrink-0 text-text-tertiary" aria-hidden="true" />
      {LANGUAGES.map(({ code, label }) => {
        const active = code === current;
        return (
          <button
            key={code}
            type="button"
            lang={code}
            aria-pressed={active}
            disabled={!localOnly && account.isPending}
            onClick={() => void pick(code)}
            className={
              'rounded px-1.5 py-0.5 text-xs font-medium transition-colors ' +
              (active
                ? 'bg-primary text-primary-on'
                : 'text-text-secondary hover:bg-subtle hover:text-text-primary')
            }
          >
            {label}
          </button>
        );
      })}
    </div>
  );
}
