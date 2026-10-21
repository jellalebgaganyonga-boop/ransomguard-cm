/**
 * e2e/sidebar.spec.ts — one item lit at a time. "/audit" is a prefix of "/audit/user-actions" and
 * "/settings" of "/settings/notifications": without `end`, React Router's NavLink lit both.
 * On a phone the items live in the drawer behind the hamburger: same rule there.
 */

import { test, expect, setupMocks, loginAs, navigateTo } from './fixtures/index';
import type { Locator, Page } from '@playwright/test';

const PREFS = { id: 'pref-e2e-1', email_critical: true, email_high: true, email_medium: false, email_low: false };

async function navItems(page: Page): Promise<Locator> {
  const phone = (page.viewportSize()?.width ?? 1280) < 768;
  if (phone) {
    await page.getByRole('button', { name: 'Menu' }).click();
    return page.getByRole('dialog');
  }
  return page.getByRole('navigation').first();
}

for (const [path, expected, notExpected] of [
  ['/audit', "Journal d'audit", 'Actions utilisateur'],
  ['/audit/user-actions', 'Actions utilisateur', "Journal d'audit"],
  // /settings redirects to /settings/notifications today: one item lit, the one of the page shown.
  ['/settings', 'Notifications', 'Paramètres'],
  ['/settings/notifications', 'Notifications', 'Paramètres'],
] as const) {
  test(`only the item of ${path} is active`, async ({ page }) => {
    await setupMocks(page, 'tenant_admin');
    await page.route('**/api/v1/dashboard/notifications/preferences', (r) => r.fulfill({ status: 200, json: PREFS }));
    await loginAs(page, 'tenant_admin');
    await navigateTo(page, path);

    const nav = await navItems(page);
    const active = (await nav.locator('a[aria-current="page"]').allInnerTexts()).map((s) => s.trim());
    expect(active).toHaveLength(1);
    expect(active[0]).toContain(expected);
    expect(active[0]).not.toContain(notExpected);
  });
}

test('every sidebar item is at least 44 px tall (touch target)', async ({ page }) => {
  await setupMocks(page, 'tenant_admin');
  await loginAs(page, 'tenant_admin');
  await navigateTo(page, '/dashboard');

  const nav = await navItems(page);
  const links = nav.getByRole('link');
  const count = await links.count();
  expect(count).toBeGreaterThan(3);
  for (let i = 0; i < count; i++) {
    const box = await links.nth(i).boundingBox();
    expect(box?.height ?? 0, `item ${i}`).toBeGreaterThanOrEqual(44);
  }
});
