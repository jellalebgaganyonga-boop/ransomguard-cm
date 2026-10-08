/**
 * UI store — Zustand (client state) per ADR-FE-003.
 *
 * Holds UI-only state that is NOT server data: sidebar collapse state,
 * active language (mirrors i18next but exposed for components that need
 * it without importing i18n directly), and global toast queue trigger
 * (actual toast rendering uses Radix Toast primitives — this just tracks
 * the "is sidebar open on mobile" type of ephemeral UI flags).
 *
 * Server data (alerts, agents, users, audit logs, /me) belongs in
 * TanStack Query (ADR-FE-003) — NOT here.
 */

import { create } from 'zustand';

interface UIState {
  /** Mobile sidebar drawer open/closed (desktop sidebar is always visible). */
  isMobileSidebarOpen: boolean;
  toggleMobileSidebar: () => void;
  closeMobileSidebar: () => void;

  /**
   * Current UI language. Mirrors i18next.language but stored here so
   * components can react to changes without subscribing to i18next's
   * own event emitter. Updated by the language switcher; i18next is
   * the source of truth (this is a read-through cache for convenience).
   */
  language: 'fr' | 'en';
  setLanguage: (lang: 'fr' | 'en') => void;
}

export const useUIStore = create<UIState>((set) => ({
  isMobileSidebarOpen: false,
  toggleMobileSidebar: () =>
    set((state) => ({ isMobileSidebarOpen: !state.isMobileSidebarOpen })),
  closeMobileSidebar: () => set({ isMobileSidebarOpen: false }),

  language: 'fr',
  setLanguage: (lang) => set({ language: lang }),
}));
