/**
 * The console is English whatever the computer is set to, and French is a deliberate choice.
 *
 * A hospital in Yaounde runs French Windows, and the browser tells every page what language its
 * user prefers. The console used to listen: i18next had 'navigator' in its detection order, so a
 * French browser switched the whole console to French on its own, with nobody choosing anything.
 * In front of a jury that is the worst possible moment to find out.
 *
 * So this runs the console in a browser that asks for French, as firmly as a browser can, and
 * requires English anyway. It is the one test that reproduces the actual condition -- the unit
 * test reads the source, and the source cannot tell you what a French browser will do.
 *
 * What changed on 2026-10-28 is the second half. The console is bilingual again (owner's
 * decision), so "no way to leave English" is no longer the rule and is no longer asserted. The
 * rule now has two parts and both are tested: the browser's language still decides nothing, and
 * a user who wants French can find the way to it.
 */

import { test, expect, setupMocks, loginAs } from './fixtures/index';

test.use({ locale: 'fr-FR', extraHTTPHeaders: { 'Accept-Language': 'fr-FR,fr;q=0.9' } });

test.describe('A French browser gets an English console', () => {
  test('the sign-in page is English', async ({ page }) => {
    await setupMocks(page, 'tenant_admin');
    await page.goto('/auth/login');

    await expect(page.getByRole('button', { name: /^sign in$/i })).toBeVisible();
    await expect(page.getByLabel(/organization code/i)).toBeVisible();

    // The French wording of the same page, which must not appear on its own.
    await expect(page.getByRole('button', { name: /se connecter/i })).toHaveCount(0);
    await expect(page.getByText(/code d.établissement/i)).toHaveCount(0);
  });

  test('the sign-in page offers French, without having taken it', async ({ page }) => {
    // Somebody who reads French needs to find the way to French before signing in, and there is
    // no account yet to remember the choice against.
    await setupMocks(page, 'tenant_admin');
    await page.goto('/auth/login');

    const picker = page.getByRole('group', { name: /language/i });
    await expect(picker).toBeVisible();
    await expect(picker.getByRole('button', { name: 'EN', exact: true })).toHaveAttribute('aria-pressed', 'true');
    await expect(picker.getByRole('button', { name: 'FR', exact: true })).toHaveAttribute('aria-pressed', 'false');
  });

  test('choosing French changes the page, and only when chosen', async ({ page }) => {
    await setupMocks(page, 'tenant_admin');
    await page.goto('/auth/login');

    await page.getByRole('group', { name: /language/i }).getByRole('button', { name: 'FR', exact: true }).click();

    await expect(page.getByRole('button', { name: /se connecter/i })).toBeVisible();
    await expect(page.locator('html')).toHaveAttribute('lang', /^fr/);
  });

  test('the signed-in console is English, with the switch in the bar', async ({ page }) => {
    await setupMocks(page, 'tenant_admin');
    await loginAs(page, 'tenant_admin');

    await expect(page.getByTestId('user-menu')).toHaveAttribute('aria-label', 'Account menu');
    await expect(page.getByRole('group', { name: /language/i })).toBeVisible();
  });

  test('the page itself declares English', async ({ page }) => {
    // The <html lang> is what a screen reader uses to choose a voice: a French lang on English
    // text is read aloud as gibberish.
    await setupMocks(page, 'tenant_admin');
    await loginAs(page, 'tenant_admin');

    await expect(page.locator('html')).toHaveAttribute('lang', /^en/);
  });
});
