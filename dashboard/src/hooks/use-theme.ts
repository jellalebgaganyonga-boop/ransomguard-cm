/**
 * Light/dark theme for the console.
 *
 * The design-token file (src/styles/tokens.css) already uses a two-tier mapping (sys-color-*
 * references ref-palette-*), with a `html.dark { … }` block that overrides the Tier-2 mappings
 * for dark. All this hook does is:
 *   - decide which theme is in effect (user choice if set, else the OS preference),
 *   - set `document.documentElement.classList` to drive Tailwind's `dark:` variant,
 *   - persist the user's explicit choice in `localStorage` so the next load matches.
 *
 * The choice is one of 'light', 'dark', 'system'. 'system' follows prefers-color-scheme live.
 */

import { useCallback, useEffect, useState } from 'react';

export type ThemeChoice = 'light' | 'dark' | 'system';
export type ResolvedTheme = 'light' | 'dark';

const STORAGE_KEY = 'rg.theme';

function readStoredChoice(): ThemeChoice {
  if (typeof window === 'undefined') return 'system';
  try {
    const value = window.localStorage.getItem(STORAGE_KEY);
    if (value === 'light' || value === 'dark' || value === 'system') return value;
  } catch {
    // storage can be blocked (private mode, cleared cookies): fall back to the system default.
  }
  return 'system';
}

function readSystemTheme(): ResolvedTheme {
  if (typeof window === 'undefined' || !window.matchMedia) return 'light';
  return window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
}

function apply(resolved: ResolvedTheme) {
  if (typeof document === 'undefined') return;
  const root = document.documentElement;
  root.classList.toggle('dark', resolved === 'dark');
  // color-scheme hints form controls (scrollbars, native selects) to switch too.
  root.style.colorScheme = resolved;
}

export function useTheme(): {
  choice: ThemeChoice;
  resolved: ResolvedTheme;
  setChoice: (next: ThemeChoice) => void;
  toggle: () => void;
} {
  const [choice, setChoiceState] = useState<ThemeChoice>(readStoredChoice);
  const [systemTheme, setSystemTheme] = useState<ResolvedTheme>(readSystemTheme);

  const resolved: ResolvedTheme = choice === 'system' ? systemTheme : choice;

  // Apply on every change and on first mount.
  useEffect(() => {
    apply(resolved);
  }, [resolved]);

  // Follow the OS when the user left the choice at 'system'. The listener always exists so a
  // later switch from light to system picks the current OS value without a reload.
  useEffect(() => {
    if (typeof window === 'undefined' || !window.matchMedia) return undefined;
    const media = window.matchMedia('(prefers-color-scheme: dark)');
    const handler = (event: MediaQueryListEvent) => setSystemTheme(event.matches ? 'dark' : 'light');
    media.addEventListener('change', handler);
    return () => media.removeEventListener('change', handler);
  }, []);

  const setChoice = useCallback((next: ThemeChoice) => {
    setChoiceState(next);
    try {
      window.localStorage.setItem(STORAGE_KEY, next);
    } catch {
      // Not stored, but at least applied for this session.
    }
  }, []);

  const toggle = useCallback(() => {
    setChoice(resolved === 'dark' ? 'light' : 'dark');
  }, [resolved, setChoice]);

  return { choice, resolved, setChoice, toggle };
}

/**
 * Apply the stored theme before React boots so the first paint matches, no flash.
 * Call this once from main.tsx, before `createRoot`.
 */
export function applyStoredThemeEagerly(): void {
  const stored = readStoredChoice();
  apply(stored === 'system' ? readSystemTheme() : stored);
}
