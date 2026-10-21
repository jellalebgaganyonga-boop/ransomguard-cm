/**
 * e2e/settings.spec.ts — Settings (Fix C): four tabs under /settings. Profile (own name), Language
 * (stored on the GRID, applied at sign-in), Notifications (e-mail preferences, honest about
 * delivery), Security (change password: 403 wrong current, 422 unfit). Every role reaches the page;
 * the bell in the header is its quick link.
 */

import { test, expect, AxeBuilder, setupMocks, loginAs, navigateTo, expectNoHorizontalOverflow, MOCK_ME } from './fixtures/index';
import type { Page } from '@playwright/test';

const PREFS = { id: 'pref-e2e-1', email_critical: true, email_high: true, email_medium: false, email_low: false, email_delivery_configured: false };

async function mockPrefs(page: Page): Promise<void> {
  await page.route('**/api/v1/dashboard/notifications/preferences', (r) => r.fulfill({ status: 200, json: PREFS }));
}

test.describe('Settings', () => {
  test('a read-only auditor reaches it; the profile tab is read-only but for the name', async ({ page }) => {
    await setupMocks(page, 'read_only_auditor');
    await mockPrefs(page);
    let patched: unknown = null;
    await page.route('**/api/v1/dashboard/me', (r) => {
      if (r.request().method() === 'PATCH') {
        patched = r.request().postDataJSON();
        return r.fulfill({ status: 200, json: { ...MOCK_ME.read_only_auditor, full_name: (patched as { full_name: string }).full_name } });
      }
      return r.fulfill({ status: 200, json: MOCK_ME.read_only_auditor });
    });
    await loginAs(page, 'read_only_auditor');
    await navigateTo(page, '/settings');

    await expect(page.getByRole('heading', { level: 1 })).toHaveText('Paramètres');
    await expect(page.getByRole('tab', { name: 'Profil' })).toHaveAttribute('aria-selected', 'true');
    await expect(page.getByTestId('profile-name')).toHaveText('Auditeur E2E');
    await expectNoHorizontalOverflow(page);
    const panel = page.getByRole('tabpanel');
    await expect(panel.getByText('auditor@hopital-yde.cm')).toBeVisible();
    await expect(panel.getByText('Hôpital Régional de Yaoundé')).toBeVisible();

    await page.getByRole('button', { name: 'Modifier' }).click();
    await page.getByLabel('Nom affiché').fill('  Auditrice E2E ');
    await page.getByRole('button', { name: 'Enregistrer' }).click();

    expect(patched).toEqual({ full_name: 'Auditrice E2E' });
    await expect(page.getByTestId('toast')).toContainText('Nom enregistré.');
    await expect(page.getByTestId('profile-name')).toHaveText('Auditrice E2E');

    const results = await new AxeBuilder({ page }).analyze();
    const critical = results.violations.filter((v) => v.impact === 'critical' || v.impact === 'serious');
    expect(critical, `Critical/serious a11y violations: ${JSON.stringify(critical, null, 2)}`).toEqual([]);
  });

  test('the language is stored on the GRID and applied at once', async ({ page }) => {
    await setupMocks(page, 'security_analyst');
    await mockPrefs(page);
    let sent: unknown = null;
    let preferences = { ...MOCK_ME.security_analyst.preferences };
    await page.route('**/api/v1/dashboard/me', (r) => r.fulfill({ status: 200, json: { ...MOCK_ME.security_analyst, preferences } }));
    await page.route('**/api/v1/dashboard/me/preferences', (r) => {
      sent = r.request().postDataJSON();
      preferences = { ...preferences, ...(sent as object) };
      return r.fulfill({ status: 200, json: preferences });
    });
    await loginAs(page, 'security_analyst');
    await navigateTo(page, '/settings?tab=language');

    await expect(page.getByRole('tab', { name: 'Langue' })).toHaveAttribute('aria-selected', 'true');
    await page.getByRole('tabpanel').getByRole('combobox').selectOption('en');

    await expect(page.getByRole('heading', { level: 1 })).toHaveText('Settings');
    expect(sent).toEqual({ language: 'en' });
    await expect(page.getByTestId('toast')).toContainText('Language saved.');
    await expect(page.getByText('Choice stored on the GRID.')).toBeVisible();

    // Stored on the GRID: a fresh load comes up in English before any click.
    await page.reload();
    await expect(page.getByRole('heading', { level: 1 })).toHaveText('Settings');
  });

  test('the bell leads every role to the notifications tab; a preference is stored on the GRID', async ({ page }) => {
    await setupMocks(page, 'read_only_auditor');
    let stored = { ...PREFS };
    let sent: unknown = null;
    await page.route('**/api/v1/dashboard/notifications/preferences', (r) => {
      if (r.request().method() === 'PUT') {
        sent = r.request().postDataJSON();
        stored = { ...stored, ...(sent as object) };
      }
      return r.fulfill({ status: 200, json: stored });
    });
    await loginAs(page, 'read_only_auditor');

    await page.getByTestId('notifications-link').click();
    await expect(page).toHaveURL(/\/settings\?tab=notifications$/);
    await expect(page.getByRole('tab', { name: 'Notifications' })).toHaveAttribute('aria-selected', 'true');

    await expect(page.getByTestId('email-delivery-not-configured')).toContainText("n'est pas configuré");
    const medium = page.getByRole('switch', { name: /moyen|medium/i });
    await expect(medium).toHaveAttribute('aria-checked', 'false');
    await medium.click();

    expect(sent).toEqual({ email_critical: true, email_high: true, email_medium: true, email_low: false });
    await expect(medium).toHaveAttribute('aria-checked', 'true');
    await expect(page.getByTestId('toast')).toContainText('Préférence enregistrée.');
  });

  test('a failed preference save is said, never pretended', async ({ page }) => {
    await setupMocks(page, 'security_analyst');
    await page.route('**/api/v1/dashboard/notifications/preferences', (r) =>
      r.request().method() === 'PUT'
        ? r.fulfill({ status: 503, json: { detail: 'down' } })
        : r.fulfill({ status: 200, json: PREFS }));
    await loginAs(page, 'security_analyst');
    await navigateTo(page, '/settings/notifications'); // the old path still lands on the tab

    await expect(page).toHaveURL(/\/settings\?tab=notifications$/);
    await expect(page.getByRole('tab', { name: 'Notifications' })).toHaveAttribute('aria-selected', 'true');
    await page.getByRole('switch', { name: /faible|low/i }).click();

    await expect(page.getByTestId('toast')).toHaveAttribute('data-kind', 'error');
    await expect(page.getByRole('switch', { name: /faible|low/i })).toHaveAttribute('aria-checked', 'false');
  });

  test('change password: a wrong current password is refused (403), a right one changes it (204)', async ({ page }) => {
    await setupMocks(page, 'tenant_admin');
    await mockPrefs(page);
    const attempts: unknown[] = [];
    await page.route('**/api/v1/dashboard/me/change-password', (r) => {
      const body = r.request().postDataJSON() as { current_password: string; new_password: string };
      attempts.push(body);
      if (body.current_password !== 'MotDePasseTest123!') return r.fulfill({ status: 403, json: { detail: 'Current password is incorrect' } });
      if (body.new_password === body.current_password) return r.fulfill({ status: 422, json: { detail: 'same' } });
      return r.fulfill({ status: 204 });
    });
    await loginAs(page, 'tenant_admin');
    await navigateTo(page, '/settings?tab=security');

    const submit = page.getByRole('button', { name: 'Changer le mot de passe' });
    await expect(submit).toBeDisabled();

    await expectNoHorizontalOverflow(page);
    await page.getByLabel('Mot de passe actuel').fill('wrong');
    await page.getByLabel(/^Nouveau mot de passe/).fill('short');
    await expect(page.getByText('8 caractères au moins.')).toBeVisible();
    await expect(submit).toBeDisabled();

    await page.getByLabel(/^Nouveau mot de passe/).fill('NouveauMotDePasse1!');
    await page.getByLabel('Confirmer le nouveau mot de passe').fill('NouveauMotDePasse1?');
    await expect(page.getByText('Les deux saisies diffèrent.')).toBeVisible();
    await expect(submit).toBeDisabled();

    await page.getByLabel('Confirmer le nouveau mot de passe').fill('NouveauMotDePasse1!');
    await submit.click();
    await expect(page.getByRole('alert')).toHaveText('Mot de passe actuel incorrect.');

    await page.getByLabel('Mot de passe actuel').fill('MotDePasseTest123!');
    await submit.click();
    await expect(page.getByTestId('toast')).toContainText('Mot de passe changé.');
    await expect(page.getByLabel('Mot de passe actuel')).toHaveValue('');
    expect(attempts).toHaveLength(2);
  });
});
