/**
 * e2e/idle-timeout.spec.ts — AC1.4.x: Session expiry after 30 min idle.
 *
 * Uses page.clock.install() (Playwright ≥ 1.45) to fast-forward
 * the browser's timer API without waiting real time.
 * The idle-timeout hook (use-idle-timeout.ts) uses setTimeout/addEventListener —
 * both are intercepted by the fake clock.
 *
 * NOTE: page.clock.install() must be called BEFORE any navigation
 * so the fake clock is active from the first script execution.
 */

import { test, expect, setupMocks, MOCK_TOKENS, MOCK_ME } from './fixtures/index';

const THIRTY_MIN_MS = 30 * 60 * 1000;

test.describe('Idle timeout', () => {
  test('session expired modal appears after 30 min idle → redirects to login on CTA click', async ({ page }) => {
    // Install fake clock BEFORE navigation (intercepts all timer functions)
    await page.clock.install();

    await setupMocks(page, 'tenant_admin');
    // Explicit refresh mock for boot sequence
    await page.route('**/api/v1/auth/refresh', (r) =>
      r.fulfill({ status: 200, json: MOCK_TOKENS }));

    // Perform login
    await page.goto('/auth/login');
    await page.getByLabel(/code établissement/i).fill('hopital-yde');
    await page.getByLabel(/^email/i).fill(MOCK_ME.tenant_admin.email);
    await page.locator('#password').fill('MotDePasseTest123!');
    await page.getByRole('button', { name: /se connecter/i }).click();
    await page.waitForURL('**/dashboard');

    // Fast-forward 30 minutes + 1 ms to trigger the idle timeout
    await page.clock.fastForward(THIRTY_MIN_MS + 1);

    // Session expired modal should be visible (i18n: auth.sessionExpired.title)
    // SessionExpiredModal uses role="alertdialog" (ARIA subtype of dialog)
    const modal = page.getByRole('alertdialog');
    await expect(modal).toBeVisible({ timeout: 5000 });
    await expect(modal).toContainText(/session.*expir|expir.*session/i);

    // Click the CTA button (fr: "Se reconnecter")
    await modal.getByRole('button', { name: /se reconnecter|reconnect/i }).click();

    // Should redirect to login
    await expect(page).toHaveURL(/\/auth\/login/);
  });

  test('user activity resets idle timer (no modal if active within 30 min)', async ({ page }) => {
    await page.clock.install();
    await setupMocks(page, 'tenant_admin');

    await page.goto('/auth/login');
    await page.getByLabel(/code établissement/i).fill('hopital-yde');
    await page.getByLabel(/^email/i).fill(MOCK_ME.tenant_admin.email);
    await page.locator('#password').fill('MotDePasseTest123!');
    await page.getByRole('button', { name: /se connecter/i }).click();
    await page.waitForURL('**/dashboard');

    // Advance 20 minutes
    await page.clock.fastForward(20 * 60 * 1000);

    // Simulate user activity (mousemove / keypress resets the timer)
    await page.mouse.move(400, 300);

    // Advance another 20 minutes (total 40 min, but 20 min since last activity)
    await page.clock.fastForward(20 * 60 * 1000);

    // No modal should be shown (timer was reset by activity)
    const modal = page.getByRole('alertdialog');
    await expect(modal).not.toBeVisible();
  });
});
