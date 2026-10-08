/**
 * i18n configuration — ADR-FE-006 (react-i18next + ICU MessageFormat).
 *
 * Per Definition Phase Day 4 §6 and Discovery personas: French is the
 * default language for all three roles (Dr. Amani, Marc Tchoumi, Sister
 * Jeanne all operate primarily in French). English is available via the
 * language switcher in the header (cmp.header).
 *
 * ICU MessageFormat (i18next-icu) is required for correct French
 * pluralization (e.g., "1 alerte" vs "2 alertes"). It also future-proofs
 * for languages with more plural categories if the product expands
 * beyond fr/en.
 */

import i18n from 'i18next';
import { initReactI18next } from 'react-i18next';
import ICU from 'i18next-icu';
import LanguageDetector from 'i18next-browser-languagedetector';

import fr from './locales/fr.json';
import en from './locales/en.json';

void i18n
  .use(ICU)
  .use(LanguageDetector)
  .use(initReactI18next)
  .init({
    resources: {
      fr: { translation: fr },
      en: { translation: en },
    },
    fallbackLng: 'fr', // French-default per product spec
    // lng omitted: LanguageDetector decides, falls back to fallbackLng
    interpolation: {
      escapeValue: false, // React already escapes
    },
    detection: {
      // Order: browser -> fallback. Sprint 8 will add persisted user
      // preference from /dashboard/me.preferences.language.
      order: ['navigator'],
      caches: [], // do not write to localStorage (no persisted prefs in Sprint 7)
    },
    react: {
      useSuspense: false, // avoid suspense boundaries for simple key lookups
    },
  });

export default i18n;
