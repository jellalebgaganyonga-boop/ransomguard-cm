/**
 * e2e/sidebar.spec.ts — one item lit at a time. "/audit" is a prefix of "/audit/user-actions" and
 * "/settings" of "/settings/notifications": without `end`, React Router's NavLink lit both.
 */

import { test, expect, setupMocks, loginAs, navigateTo } from './fixtures/index';

const PREFS = { id: 'pref-e2e-1', email_critical: true, email_high: true, email_medium: false, email_low: false };

for (const [path, expected, notExpected] of [
  ['/audit', 'Audit log', 'User actions'],
  ['/audit/user-actions', 'User actions', 'Audit log'],
  ['/settings', 'Settings', 'Notifications'],
  ['/settings/notifications', 'Settings', 'Notifications'], // the Notifications tab of Settings
] as const) {
  test(`only the item of ${path} is active`, async ({ page }) => {
    await setupMocks(page, 'tenant_admin');
    await page.route('**/api/v1/dashboard/notifications/preferences', (r) => r.fulfill({ status: 200, json: PREFS }));
    await loginAs(page, 'tenant_admin');
    await navigateTo(page, path);

    const nav = page.getByRole('navigation').first();
    const active = (await nav.locator('a[aria-current="page"]').allInnerTexts()).map((s) => s.trim());
    expect(active).toHaveLength(1);
    expect(active[0]).toContain(expected);
    expect(active[0]).not.toContain(notExpected);
  });
}
