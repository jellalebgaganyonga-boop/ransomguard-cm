import { describe, expect, it } from 'vitest';
import { isLegacyNotificationsPath, tabFrom } from './settings-tab';

describe('tabFrom', () => {
  it('defaults to profile', () => {
    expect(tabFrom('', '/settings')).toBe('profile');
  });
  it('reads ?tab=', () => {
    expect(tabFrom('?tab=security', '/settings')).toBe('security');
    expect(tabFrom('?tab=language', '/settings')).toBe('language');
  });
  it('ignores an unknown tab', () => {
    expect(tabFrom('?tab=billing', '/settings')).toBe('profile');
  });
  it('maps the legacy /settings/notifications path to the notifications tab', () => {
    expect(tabFrom('', '/settings/notifications')).toBe('notifications');
    expect(tabFrom('', '/settings/notifications/')).toBe('notifications');
  });
  it('recognises the legacy path alone', () => {
    expect(isLegacyNotificationsPath('/settings/notifications')).toBe(true);
    expect(isLegacyNotificationsPath('/settings')).toBe(false);
    expect(isLegacyNotificationsPath('/settings/notifications/x')).toBe(false);
  });
  it('lets ?tab= win over the legacy path', () => {
    expect(tabFrom('?tab=profile', '/settings/notifications')).toBe('profile');
  });
});
