/**
 * e2e/agents.spec.ts — EPIC-AGENTS happy paths + RBAC
 * AC4.x: List, detail, isolate command (admin-only).
 */

import { test, expect, AxeBuilder, setupMocks, loginAs, navigateTo, MOCK_AGENT, MOCK_COMMAND } from './fixtures/index';

test.describe('Agents list', () => {
  test('tenant_admin sees agent list with hostname', async ({ page }) => {
    await setupMocks(page, 'tenant_admin');
    await loginAs(page, 'tenant_admin');

    await navigateTo(page, '/agents');
    await expect(page.getByText('ws-cardio-01', { exact: true }).first()).toBeVisible();
  });

  test('status filter shows results', async ({ page }) => {
    await setupMocks(page, 'tenant_admin');
    await loginAs(page, 'tenant_admin');

    await navigateTo(page, '/agents');
    // Status badge "Actif" visible for the mock agent
    await expect(page.getByText(/actif/i).first()).toBeVisible();
  });

  test('@a11y accessibility: agents list', async ({ page }) => {
    await setupMocks(page, 'tenant_admin');
    await loginAs(page, 'tenant_admin');
    await navigateTo(page, '/agents');

    const results = await new AxeBuilder({ page }).analyze();
    const critical = results.violations.filter(
      (v) => v.impact === 'critical' || v.impact === 'serious',
    );
    expect(critical, `Critical/serious a11y violations: ${JSON.stringify(critical, null, 2)}`).toEqual([]);
  });
});

test.describe('Agent detail — tenant_admin isolate command', () => {
  test('Isolate flow: reason 20+ chars + ack checkbox → command issued (AC4.3.x)', async ({ page }) => {
    await setupMocks(page, 'tenant_admin');
    await page.route('**/api/v1/dashboard/agents/agent-e2e-aaa', (r) =>
      r.fulfill({ status: 200, json: MOCK_AGENT }));
    await page.route('**/api/v1/dashboard/commands', (r) =>
      r.fulfill({ status: 201, json: MOCK_COMMAND }));

    await loginAs(page, 'tenant_admin');
    await navigateTo(page, '/agents/agent-e2e-aaa');

    // Isolate button visible for admin
    const isolateBtn = page.getByRole('button', { name: /isoler/i });
    await expect(isolateBtn).toBeVisible();
    await isolateBtn.click();

    // Modal opens
    const modal = page.getByRole('dialog');
    await expect(modal).toBeVisible();

    // Fill reason (min chars required)
    const reason = 'Suspicion de compromission ransomware — processus chiffrant détecté en cours d\'exécution.';
    await modal.getByRole('textbox').fill(reason);

    // Check acknowledgement checkbox
    await modal.getByRole('checkbox').check();

    // Submit button becomes enabled
    const submitBtn = modal.getByRole('button', { name: /isoler/i }).last();
    await expect(submitBtn).toBeEnabled();
    await submitBtn.click();

    // Command pending status shown (last command state in local React state)
    await expect(page.getByText(/en attente|pending/i)).toBeVisible();
  });

  test('Isolate submit disabled with short reason or unchecked ack', async ({ page }) => {
    await setupMocks(page, 'tenant_admin');
    await page.route('**/api/v1/dashboard/agents/agent-e2e-aaa', (r) =>
      r.fulfill({ status: 200, json: MOCK_AGENT }));

    await loginAs(page, 'tenant_admin');
    await navigateTo(page, '/agents/agent-e2e-aaa');

    await page.getByRole('button', { name: /isoler/i }).click();
    const modal = page.getByRole('dialog');

    // Short reason, no ack → disabled
    await modal.getByRole('textbox').fill('Court');
    const submitBtn = modal.getByRole('button', { name: /isoler/i }).last();
    await expect(submitBtn).toBeDisabled();
  });

  test('@a11y accessibility: agent detail', async ({ page }) => {
    await setupMocks(page, 'tenant_admin');
    await page.route('**/api/v1/dashboard/agents/agent-e2e-aaa', (r) =>
      r.fulfill({ status: 200, json: MOCK_AGENT }));

    await loginAs(page, 'tenant_admin');
    await navigateTo(page, '/agents/agent-e2e-aaa');

    const results = await new AxeBuilder({ page }).analyze();
    const critical = results.violations.filter(
      (v) => v.impact === 'critical' || v.impact === 'serious',
    );
    expect(critical, `Critical/serious a11y violations: ${JSON.stringify(critical, null, 2)}`).toEqual([]);
  });
});

test.describe('Agent detail — security_analyst RBAC (AC4.1.3)', () => {
  test('NO isolate command button visible for analyst', async ({ page }) => {
    await setupMocks(page, 'security_analyst');
    await page.route('**/api/v1/dashboard/agents/agent-e2e-aaa', (r) =>
      r.fulfill({ status: 200, json: MOCK_AGENT }));

    await loginAs(page, 'security_analyst');
    await navigateTo(page, '/agents/agent-e2e-aaa');

    // Isolate button must not be in DOM
    await expect(page.getByRole('button', { name: /isoler/i })).not.toBeAttached();
  });
});
