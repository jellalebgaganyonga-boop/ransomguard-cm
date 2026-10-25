/**
 * i18n configuration — ADR-FE-006 (react-i18next + ICU MessageFormat).
 *
 * The console is English. The owner's decision (2026-10-25) replaced the earlier rule that
 * French and English were kept in step: the presentation is in English, so nothing French may
 * appear in the running system unless a user deliberately asks for it.
 *
 * Two things follow, and both are deliberate:
 *
 *  - the browser's own language is no longer consulted. A French Windows or a French browser
 *    used to switch the whole console to French on its own, which is exactly what must not
 *    happen in front of a jury. Only a choice made in Settings, or one already stored in this
 *    browser, changes the language;
 *  - the French option is hidden behind VITE_ENABLE_FRENCH, which is false in every env file.
 *    fr.json stays where it is and stays loaded: the translations are not deleted, they are
 *    simply not offered.
 *
 * ICU MessageFormat (i18next-icu) stays for correct pluralization, and for languages with more
 * plural categories if the product ever adds one.
 */

import i18n from 'i18next';
import { initReactI18next } from 'react-i18next';
import ICU from 'i18next-icu';
import LanguageDetector from 'i18next-browser-languagedetector';

import en from './locales/en.json';

/**
 * Whether French is offered at all. False in every env file: the console is English, and the
 * switcher is not shown. Setting it to true is the only way back to a two-language console.
 */
export const FRENCH_ENABLED = import.meta.env.VITE_ENABLE_FRENCH === 'true';

let frenchLoaded = false;

/**
 * Loads the French translations, once, and only if something asks for them.
 *
 * <p>fr.json is deliberately not imported at the top of this file. A static import inlines every
 * French string into the main bundle, where it ships to every user whether or not French is
 * offered -- and where check-no-french cannot tell a translation from a string that leaked. As a
 * dynamic import it becomes its own chunk: the translations are kept, and the bundle the console
 * actually serves has no French in it.</p>
 */
export async function loadFrench(): Promise<void> {
  if (frenchLoaded) return;
  const fr = (await import('./locales/fr.json')).default;
  i18n.addResourceBundle('fr', 'translation', fr, true, true);
  frenchLoaded = true;
}

/** Switches language, fetching the translations first when they are not bundled. */
export async function setLanguage(language: string): Promise<void> {
  if (language === 'fr') await loadFrench();
  await i18n.changeLanguage(language);
}

void i18n
  .use(ICU)
  .use(LanguageDetector)
  .use(initReactI18next)
  .init({
    resources: {
      en: { translation: en },
    },
    // English always, unless this browser has a stored choice. The choice made in Settings is
    // kept on the GRID (/dashboard/me.preferences) and in this browser, and applied once /me is
    // known (protected-route.tsx).
    lng: 'en',
    fallbackLng: 'en',
    supportedLngs: ['en', 'fr'],
    nonExplicitSupportedLngs: true,
    interpolation: {
      escapeValue: false, // React already escapes
    },
    detection: {
      // 'navigator' is deliberately absent: the browser's own language must not decide this.
      order: ['localStorage'],
      lookupLocalStorage: 'rg-language',
      caches: ['localStorage'],
    },
    react: {
      useSuspense: false, // avoid suspense boundaries for simple key lookups
    },
  });

// Keep <html lang> with the language actually in use. It is what a screen reader reads the page
// with, and it was hardcoded to French: an English console announced itself as French, so the
// text was read aloud with a French voice. index.html now starts at "en" and this follows any
// later change.
function syncDocumentLanguage(language: string): void {
  if (typeof document !== 'undefined') {
    document.documentElement.lang = language.split('-')[0] ?? 'en';
  }
}

syncDocumentLanguage(i18n.language ?? 'en');
i18n.on('languageChanged', syncDocumentLanguage);

export default i18n;
