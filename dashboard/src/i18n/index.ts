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
    // English by default (owner's decision, 2026-10-21), French kept: a browser in French gets
    // French; the choice made in Settings is kept on the GRID (/dashboard/me.preferences) and in
    // this browser, and applied once /me is known (protected-route.tsx).
    fallbackLng: 'en',
    supportedLngs: ['en', 'fr'],
    nonExplicitSupportedLngs: true,
    interpolation: {
      escapeValue: false, // React already escapes
    },
    detection: {
      order: ['localStorage', 'navigator'],
      lookupLocalStorage: 'rg-language',
      caches: ['localStorage'],
    },
    react: {
      useSuspense: false, // avoid suspense boundaries for simple key lookups
    },
  });

export default i18n;
