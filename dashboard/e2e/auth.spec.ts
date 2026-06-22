/**
 * e2e/auth.spec.ts — EPIC-AUTH happy paths + RBAC
 * AC1.1.x: Login, AC1.2.x: Logout, AC1.3.x: Session guard
 */

import { test, expect, AxeBuilder, setupMocks, loginAs, MOCK_TOKENS } from './fixtures/index';

test.describe('Login', () => {
  test('valid credentials → redirect to /dashboard', async ({ page }) => {
    await setupMocks(page, 'tenant_admin');
    await page.goto('/auth/login');
    await page.getByLabel(/code établissement/i).fill('hopital-yde');
    await page.getByLabel(/^email/i).fill('admin@hopital-yde.cm');
    await page.locator('#password').fill('MotDePasseTest123!');
    await page.getByRole('button', { name: /se connecter/i }).click();
    await page.waitForURL('**/dashboard');
    await expect(page).toHaveURL(/\/dashboard/);
  });

  test('invalid credentials → inline error, no crash or redirect', async ({ page }) => {
    await setupMocks(page, 'tenant_admin');
    // Override login to return 401
    await page.route('**/api/v1/auth/login', (r) =>
      r.fulfill({ status: 401, json: { detail: 'Invalid credentials' } }));

    await page.goto('/auth/login');
    await page.getByLabel(/code établissement/i).fill('hopital-yde');
    await page.getByLabel(/^email/i).fill('wrong@example.cm');
    await page.locator('#password').fill('mauvais-mdp');
    await page.getByRole('button', { name: /se connecter/i }).click();

    // Should stay on login page
    await expect(page).toHaveURL(/\/auth\/login/);
    // Error message visible (i18n key: auth.login.errors.invalidCredentials)
    await expect(page.getByText(/email ou mot de passe incorrect/i)).toBeVisible();
  });

  test('empty form → required field errors, no API call', async ({ page }) => {
    let loginCalled = false;
    await setupMocks(page, 'tenant_admin');
    await page.route('**/api/v1/auth/login', (r) => {
      loginCalled = true;
      return r.fulfill({ status: 200, json: MOCK_TOKENS });
    });

    await page.goto('/auth/login');
    await page.getByRole('button', { name: /se connecter/i }).click();

    // HTML5 / react-hook-form validation fires before submission
    await expect(page).toHaveURL(/\/auth\/login/);
    expect(loginCalled).toBe(false);
  });

  test('@a11y accessibility: login page', async ({ page }) => {
    await page.goto('/auth/login');
    const results = await new AxeBuilder({ page }).analyze();
    const critical = results.violations.filter(
      (v) => v.impact === 'critical' || v.impact === 'serious',
    );
    expect(critical, `Critical/serious a11y violations: ${JSON.stringify(critical, null, 2)}`).toEqual([]);
  });
});

test.describe('Logout', () => {
  test('logout → redirect to /auth/login, protected page then redirects again', async ({ page }) => {
    await setupMocks(page, 'tenant_admin');
    await loginAs(page, 'tenant_admin');

    // Trigger logout via header dropdown
    await page.getByRole('button', { name: /profil|déconnect/i }).first().click();
    // The logout button may be nested in a dropdown
    const logoutBtn = page.getByRole('menuitem', { name: /se déconnecter/i })
      .or(page.getByRole('button', { name: /se déconnecter/i }));
    await logoutBtn.first().click();

    await page.waitForURL('**/auth/login');
    await expect(page).toHaveURL(/\/auth\/login/);

    // Direct access to protected route after logout → redirected
    await page.goto('/dashboard');
    await page.waitForURL('**/auth/login');
    await expect(page).toHaveURL(/\/auth\/login/);
  });
});

test.describe('Session guard', () => {
  test('unauthenticated access to /dashboard → redirect to /auth/login', async ({ page }) => {
    // No login — fresh context, no tokens
    await page.goto('/dashboard');
    await expect(page).toHaveURL(/\/auth\/login/);
  });

  test('unauthenticated access to /alerts/:id → redirect to /auth/login', async ({ page }) => {
    await page.goto('/alerts/some-id');
    await expect(page).toHaveURL(/\/auth\/login/);
  });
});
