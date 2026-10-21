/** Which Settings tab a URL asks for (settings-page.tsx); kept out of the page for fast refresh. */

export const SETTINGS_TABS = ['profile', 'language', 'notifications', 'security'] as const;
export type SettingsTab = (typeof SETTINGS_TABS)[number];

/** The former Notifications page: /settings/notifications, now the tab of the same name. */
export function isLegacyNotificationsPath(pathname: string): boolean {
  return /\/settings\/notifications\/?$/.test(pathname);
}

/** The tab from ?tab=..., or from the legacy /settings/notifications path; profile otherwise. */
export function tabFrom(search: string, pathname: string): SettingsTab {
  const asked = new URLSearchParams(search).get('tab');
  if (asked && (SETTINGS_TABS as readonly string[]).includes(asked)) return asked as SettingsTab;
  if (isLegacyNotificationsPath(pathname)) return 'notifications';
  return 'profile';
}
