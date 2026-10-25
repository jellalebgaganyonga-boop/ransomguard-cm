/**
 * The console is English whatever the computer is set to.
 *
 * A hospital in Yaounde runs French Windows, and the browser tells every page what language its
 * user prefers. The console used to listen: i18next had 'navigator' in its detection order, so a
 * French browser switched the whole console to French on its own, with nobody choosing anything.
 * In front of a jury that is the worst possible moment to find out.
 *
 * So this runs the console in a browser that asks for French, as firmly as a browser can, and
 * requires English anyway. It is the one test that reproduces the actual condition -- the unit
 * test reads the source, and the source cannot tell you what a French browser will do.
 */

import { test, expect, setupMocks, loginAs } from './fixtures/index';

test.use({ locale: 'fr-FR', extraHTTPHeaders: { 'Accept-Language': 'fr-FR,fr;q=0.9' } });

test.describe('A French browser gets an English console', () => {
  test('the sign-in page is English', async ({ page }) => {
    await setupMocks(page, 'tenant_admin');
    await page.goto('/auth/login');

    await expect(page.getByRole('button', { name: /^sign in$/i })).toBeVisible();
    await expect(page.getByLabel(/organization code/i)).toBeVisible();

    // The French wording of the same page, which must not appear.
    await expect(page.getByRole('button', { name: /se connecter/i })).toHaveCount(0);
    await expect(page.getByText(/code d.établissement/i)).toHaveCount(0);
  });

  test('the signed-in console is English, and offers no way to leave it', async ({ page }) => {
    await setupMocks(page, 'tenant_admin');
    await loginAs(page, 'tenant_admin');

    await expect(page.getByTestId('user-menu')).toHaveAttribute('aria-label', 'Account menu');

    await page.getByTestId('user-menu').click();
    const menu = page.getByRole('menu');
    await expect(menu.getByRole('menuitem', { name: 'My settings' })).toBeVisible();
    await expect(menu.getByRole('menuitem', { name: /Switch to|Passer en/ })).toHaveCount(0);
  });

  test('the page itself declares English', async ({ page }) => {
    // The <html lang> is what a screen reader uses to choose a voice: a French lang on English
    // text is read aloud as gibberish.
    await setupMocks(page, 'tenant_admin');
    await loginAs(page, 'tenant_admin');

    await expect(page.locator('html')).toHaveAttribute('lang', /^en/);
  });
});
