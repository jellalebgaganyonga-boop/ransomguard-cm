/**
 * e2e/header.spec.ts — the header user menu (Fix D): my settings, the other language (stored on
 * the GRID, the same as in Settings), sign out. The sign-out paths are in auth.spec.ts.
 */

import { test, expect, AxeBuilder, setupMocks, loginAs, MOCK_ME } from './fixtures/index';

test.describe('Header — user menu', () => {
  test('My settings opens the Settings page, for a role without the sidebar entry', async ({ page }) => {
    await setupMocks(page, 'security_analyst');
    await page.route('**/api/v1/dashboard/notifications/preferences', (r) =>
      r.fulfill({ status: 200, json: { id: 'p', email_critical: true, email_high: true, email_medium: false, email_low: false } }));
    await loginAs(page, 'security_analyst');

    await expect(page.getByRole('navigation').first().getByRole('link', { name: 'Paramètres' })).toHaveCount(0);
    await page.getByTestId('user-menu').click();
    const menu = page.getByRole('menu');
    await expect(menu).toContainText('analyst@hopital-yde.cm');
    await menu.getByRole('menuitem', { name: 'Mes paramètres' }).click();

    await expect(page).toHaveURL(/\/settings$/);
    await expect(page.getByRole('heading', { level: 1 })).toHaveText('Paramètres');
    await expect(page.getByRole('tab', { name: 'Profil' })).toHaveAttribute('aria-selected', 'true');
  });

  test('the language item switches to the other language and stores it on the GRID', async ({ page }) => {
    await setupMocks(page, 'security_analyst');
    const sent: unknown[] = [];
    let preferences = { ...MOCK_ME.security_analyst.preferences };
    await page.route('**/api/v1/dashboard/me', (r) => r.fulfill({ status: 200, json: { ...MOCK_ME.security_analyst, preferences } }));
    await page.route('**/api/v1/dashboard/me/preferences', (r) => {
      const body = r.request().postDataJSON() as { language: 'en' | 'fr' };
      sent.push(body);
      preferences = { ...preferences, ...body };
      return r.fulfill({ status: 200, json: preferences });
    });
    await loginAs(page, 'security_analyst');

    await page.getByTestId('user-menu').click();
    await page.getByRole('menuitem', { name: 'Passer en English' }).click();
    await expect(page.getByTestId('toast')).toContainText('Language saved.');
    await expect(page.getByTestId('user-menu')).toHaveAttribute('aria-label', 'Account menu');
    // The menu that was just used is still unmounting; a click on the trigger during that time is
    // swallowed (CI run 39693599525, chromium only) and the menu never reopens.
    await expect(page.getByRole('menu')).toHaveCount(0);

    await page.getByTestId('user-menu').click();
    const menu = page.getByRole('menu');
    await expect(menu.getByRole('menuitem', { name: 'Switch to Français' })).toBeVisible();
    await expect(menu.getByRole('menuitem', { name: 'My settings' })).toBeVisible();
    await expect(menu.getByRole('menuitem', { name: 'Sign out' })).toBeVisible();

    // The open menu on its own (Radix hides the rest of the page with aria-hidden while it is open,
    // so a whole-page scan now would only report that), then the whole page once it is closed.
    const forbidden = (violations: { impact?: string | null }[]) =>
      violations.filter((v) => v.impact === 'critical' || v.impact === 'serious');
    const open = await new AxeBuilder({ page }).include('[role="menu"]').analyze();
    expect(forbidden(open.violations), `Menu a11y violations: ${JSON.stringify(open.violations, null, 2)}`).toEqual([]);

    await menu.getByRole('menuitem', { name: 'Switch to Français' }).click();
    await expect(page.getByTestId('user-menu')).toHaveAttribute('aria-label', 'Menu du compte');
    expect(sent).toEqual([{ language: 'en' }, { language: 'fr' }]);

    // The page is scanned once the menu has gone: while it unmounts, Radix still keeps the rest of
    // the page aria-hidden, and a scan then reports every button of the page (CI run 39695870874).
    await expect(page.getByRole('menu')).toHaveCount(0);
    await expect(page.locator('body > [aria-hidden="true"]')).toHaveCount(0);
    const closed = await new AxeBuilder({ page }).analyze();
    expect(forbidden(closed.violations), `Page a11y violations: ${JSON.stringify(closed.violations, null, 2)}`).toEqual([]);
  });

  test('a language the GRID could not store is not applied, and the failure is said', async ({ page }) => {
    await setupMocks(page, 'read_only_auditor');
    await page.route('**/api/v1/dashboard/me/preferences', (r) => r.fulfill({ status: 503, json: { detail: 'down' } }));
    await loginAs(page, 'read_only_auditor');

    await page.getByTestId('user-menu').click();
    await page.getByRole('menuitem', { name: 'Passer en English' }).click();

    await expect(page.getByTestId('toast')).toHaveAttribute('data-kind', 'error');
    await expect(page.getByTestId('user-menu')).toHaveAttribute('aria-label', 'Menu du compte');
  });
});
