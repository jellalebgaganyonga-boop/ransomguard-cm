/**
 * e2e/header.spec.ts — the header user menu (Fix D): my settings, the other language (stored on
 * the GRID, the same as in Settings), sign out. The sign-out paths are in auth.spec.ts.
 */

import { test, expect, AxeBuilder, setupMocks, loginAs } from './fixtures/index';

test.describe('Header — user menu', () => {
  test('My settings opens the Settings page, for a role without the sidebar entry', async ({ page }) => {
    await setupMocks(page, 'security_analyst');
    await page.route('**/api/v1/dashboard/notifications/preferences', (r) =>
      r.fulfill({ status: 200, json: { id: 'p', email_critical: true, email_high: true, email_medium: false, email_low: false } }));
    await loginAs(page, 'security_analyst');

    await expect(page.getByRole('navigation').first().getByRole('link', { name: 'Settings' })).toHaveCount(0);
    await page.getByTestId('user-menu').click();
    const menu = page.getByRole('menu');
    await expect(menu).toContainText('analyst@yde-hospital.cm');
    await menu.getByRole('menuitem', { name: 'My settings' }).click();

    await expect(page).toHaveURL(/\/settings$/);
    await expect(page.getByRole('heading', { level: 1 })).toHaveText('Settings');
    await expect(page.getByRole('tab', { name: 'Profile' })).toHaveAttribute('aria-selected', 'true');
  });

  test('the user menu offers no way to leave English, and stays accessible', async ({ page }) => {
    // The console is English (owner's decision, 2026-10-25). French is kept but is not offered:
    // VITE_ENABLE_FRENCH is false, so the menu must not carry a language item at all. A console
    // that can be switched to French by a stray click is the thing this guards against.
    await setupMocks(page, 'security_analyst');
    const sent: unknown[] = [];
    await page.route('**/api/v1/dashboard/me/preferences', (r) => {
      sent.push(r.request().postDataJSON());
      return r.fulfill({ status: 200, json: { language: 'en' } });
    });
    await loginAs(page, 'security_analyst');

    await page.getByTestId('user-menu').click();
    const menu = page.getByRole('menu');

    await expect(menu.getByRole('menuitem', { name: /Switch to/ })).toHaveCount(0);
    await expect(menu.getByRole('menuitem', { name: /Passer en/ })).toHaveCount(0);
    await expect(menu.getByRole('menuitem', { name: 'My settings' })).toBeVisible();
    await expect(menu.getByRole('menuitem', { name: 'Sign out', exact: true })).toBeVisible();
    await expect(page.getByTestId('user-menu')).toHaveAttribute('aria-label', 'Account menu');

    // The open menu on its own (Radix hides the rest of the page with aria-hidden while it is
    // open, so a whole-page scan now would only report that).
    const forbidden = (violations: { impact?: string | null }[]) =>
      violations.filter((v) => v.impact === 'critical' || v.impact === 'serious');
    const open = await new AxeBuilder({ page }).include('[role="menu"]').analyze();
    expect(forbidden(open.violations), `Menu a11y violations: ${JSON.stringify(open.violations, null, 2)}`).toEqual([]);

    // Nothing was stored: no language was chosen, because none could be.
    expect(sent).toEqual([]);

    await page.keyboard.press('Escape');

    // The page is scanned once the menu has gone: while it unmounts, Radix still keeps the rest of
    // the page aria-hidden, and a scan then reports every button of the page (CI run 39695870874).
    await expect(page.getByRole('menu')).toHaveCount(0);
    await expect(page.locator('body > [aria-hidden="true"]')).toHaveCount(0);
    const closed = await new AxeBuilder({ page }).analyze();
    expect(forbidden(closed.violations), `Page a11y violations: ${JSON.stringify(closed.violations, null, 2)}`).toEqual([]);
  });

});
