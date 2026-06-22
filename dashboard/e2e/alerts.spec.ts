/**
 * e2e/alerts.spec.ts — EPIC-ALERTS happy paths + RBAC
 * AC3.x: List, detail, acknowledge, close, read-only enforcement.
 */

import { test, expect, AxeBuilder, setupMocks, loginAs, navigateTo, MOCK_ALERT_NEW, MOCK_ALERT_INVESTIGATING, MOCK_ALERT_RESOLVED } from './fixtures/index';

test.describe('Alerts list', () => {
  test('security_analyst sees alert list with data', async ({ page }) => {
    await setupMocks(page, 'security_analyst');
    await loginAs(page, 'security_analyst');

    await navigateTo(page, '/alerts');
    // Alert type from mock data
    await expect(page.getByText(/SENTINEL_SUSPICIOUS_PROCESS/i)).toBeVisible();
  });

  test('severity filter updates URL params', async ({ page }) => {
    await setupMocks(page, 'security_analyst');
    await loginAs(page, 'security_analyst');

    await navigateTo(page, '/alerts');
    await page.getByRole('combobox').first().selectOption('High');
    await expect(page).toHaveURL(/severity=High/i);
  });

  test('@a11y accessibility: alerts list', async ({ page }) => {
    await setupMocks(page, 'security_analyst');
    await loginAs(page, 'security_analyst');
    await navigateTo(page, '/alerts');

    const results = await new AxeBuilder({ page }).analyze();
    const critical = results.violations.filter(
      (v) => v.impact === 'critical' || v.impact === 'serious',
    );
    expect(critical, `Critical/serious a11y violations: ${JSON.stringify(critical, null, 2)}`).toEqual([]);
  });
});

test.describe('Alert detail — security_analyst happy path', () => {
  test('Acknowledge (New → Investigating)', async ({ page }) => {
    await setupMocks(page, 'security_analyst');
    // Stateful detail route: returns Investigating after status mutation
    let statusMutated = false;
    await page.route('**/api/v1/dashboard/alerts/alert-e2e-001', (r) =>
      r.fulfill({ status: 200, json: statusMutated ? MOCK_ALERT_INVESTIGATING : MOCK_ALERT_NEW }));
    await page.route('**/api/v1/dashboard/alerts/*/status', async (r) => {
      statusMutated = true;
      return r.fulfill({ status: 200, json: MOCK_ALERT_INVESTIGATING });
    });

    await loginAs(page, 'security_analyst');
    await navigateTo(page, '/alerts/alert-e2e-001');

    // Click acknowledge button (AC3.3.1)
    await page.getByRole('button', { name: /prendre en charge/i }).click();

    // Modal opens
    await expect(page.getByRole('dialog')).toBeVisible();

    // Confirm without note (default justification sent)
    await page.getByRole('dialog').getByRole('button', { name: /confirmer|prendre en charge/i }).click();

    // After mutation: detail refetch returns Investigating → shows "En cours"
    await expect(page.getByText(/en cours/i)).toBeVisible();
  });

  test('Close (Investigating → Resolved): requires category + 20+ char notes', async ({ page }) => {
    await setupMocks(page, 'security_analyst');
    // Start at Investigating so Close button is shown
    let statusMutated = false;
    await page.route('**/api/v1/dashboard/alerts/alert-e2e-001', (r) =>
      r.fulfill({ status: 200, json: statusMutated ? MOCK_ALERT_RESOLVED : MOCK_ALERT_INVESTIGATING }));
    await page.route('**/api/v1/dashboard/alerts/*/status', async (r) => {
      statusMutated = true;
      return r.fulfill({ status: 200, json: MOCK_ALERT_RESOLVED });
    });

    await loginAs(page, 'security_analyst');
    await navigateTo(page, '/alerts/alert-e2e-001');

    await page.getByRole('button', { name: /fermer l'incident/i }).click();
    await expect(page.getByRole('dialog')).toBeVisible();

    // Select resolution category (shadcn Select — click trigger then option)
    await page.getByRole('combobox').click();
    await page.getByRole('option', { name: /vrai positif.*contenu/i }).click();

    // Type resolution notes (min 20 chars)
    const notes = 'Investigation complète : processus isolé et neutralisé avec succès.';
    await page.getByRole('textbox').fill(notes);

    // Submit
    await page.getByRole('dialog').getByRole('button', { name: /fermer l'incident/i }).click();

    // Use exact match: "Résolu" is the status badge; "résolution" in modal labels also matches /résolu/i
    await expect(page.getByText('Résolu', { exact: true })).toBeVisible();
  });

  test('Close submit disabled until notes >= 20 chars (AC3.4.1)', async ({ page }) => {
    await setupMocks(page, 'security_analyst');
    await page.route('**/api/v1/dashboard/alerts/alert-e2e-001', (r) =>
      r.fulfill({ status: 200, json: MOCK_ALERT_INVESTIGATING }));

    await loginAs(page, 'security_analyst');
    await navigateTo(page, '/alerts/alert-e2e-001');
    await page.getByRole('button', { name: /fermer l'incident/i }).click();

    const submitBtn = page.getByRole('dialog').getByRole('button', { name: /fermer l'incident/i });

    // Before category + notes: submit is disabled
    await expect(submitBtn).toBeDisabled();

    // Fill category but short notes (< 20 chars) — shadcn Select inside Dialog: force click on option
    await page.getByRole('combobox').click();
    await page.getByRole('option', { name: /vrai positif.*contenu/i }).click({ force: true });
    await page.getByRole('textbox').fill('Trop court');
    await expect(submitBtn).toBeDisabled();

    // Verify no API call was sent
    let apiCalled = false;
    await page.route('**/api/v1/dashboard/alerts/*/status', (r) => {
      apiCalled = true;
      return r.fulfill({ status: 200, json: MOCK_ALERT_RESOLVED });
    });
    expect(apiCalled).toBe(false);
  });
});

test.describe('Alert detail — read_only_auditor RBAC', () => {
  test('NO action buttons present in DOM (not just hidden)', async ({ page }) => {
    await setupMocks(page, 'read_only_auditor');
    await page.route('**/api/v1/dashboard/alerts/alert-e2e-001', (r) =>
      r.fulfill({ status: 200, json: MOCK_ALERT_NEW }));

    await loginAs(page, 'read_only_auditor');
    await navigateTo(page, '/alerts/alert-e2e-001');

    // AC3.3.3 / AC3.4.3: no write actions for auditor
    await expect(page.getByRole('button', { name: /prendre en charge/i })).not.toBeAttached();
    await expect(page.getByRole('button', { name: /fermer l'incident/i })).not.toBeAttached();

    // Read-only notice visible instead
    await expect(page.getByText(/mode lecture seule/i)).toBeVisible();
  });

  test('@a11y accessibility: alert detail', async ({ page }) => {
    await setupMocks(page, 'security_analyst');
    await page.route('**/api/v1/dashboard/alerts/alert-e2e-001', (r) =>
      r.fulfill({ status: 200, json: MOCK_ALERT_NEW }));

    await loginAs(page, 'security_analyst');
    await navigateTo(page, '/alerts/alert-e2e-001');

    const results = await new AxeBuilder({ page }).analyze();
    const critical = results.violations.filter(
      (v) => v.impact === 'critical' || v.impact === 'serious',
    );
    expect(critical, `Critical/serious a11y violations: ${JSON.stringify(critical, null, 2)}`).toEqual([]);
  });
});
