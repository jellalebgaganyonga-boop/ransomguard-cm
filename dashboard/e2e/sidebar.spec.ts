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
    const active = nav.locator('a[aria-current="page"]');

    // These retry; the previous version did not. It read the sidebar once, the instant after
    // navigating, with `allInnerTexts()` — so on a loaded machine it sampled the page before
    // React Router had rendered the new route and asserted on whatever was still lit, which is
    // Dashboard. That made these four the flakiest tests in the suite: they passed on an idle
    // run and failed on a busy one, which is the signature of a test waiting on a stopwatch
    // instead of on the thing it is about.
    await expect(active).toHaveCount(1);
    await expect(active).toContainText(expected);
    await expect(active).not.toContainText(notExpected);
  });
}
