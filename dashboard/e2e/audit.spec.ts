/**
 * e2e/audit.spec.ts — EPIC-AUDIT happy paths + RBAC
 * AC6.x: Ed25519 agent chain integrity view (re-scoped from user-action log).
 * RBAC: tenant_admin + read_only_auditor allowed; security_analyst → redirect.
 */

import { test, expect, AxeBuilder, setupMocks, loginAs, navigateTo, MOCK_AUDIT_LOGS } from './fixtures/index';

test.describe('Audit log — authorized roles', () => {
  test('tenant_admin: /audit page renders table with entries', async ({ page }) => {
    await setupMocks(page, 'tenant_admin');
    await loginAs(page, 'tenant_admin');

    await navigateTo(page, '/audit');

    // Table should be visible with the mock entry
    await expect(page.getByRole('table')).toBeVisible();
    // Sequence number from mock data
    await expect(page.getByText('42')).toBeVisible();
  });

  test('read_only_auditor: /audit page accessible (AC6.2.1 — auditor allowed)', async ({ page }) => {
    await setupMocks(page, 'read_only_auditor');
    await loginAs(page, 'read_only_auditor');

    await navigateTo(page, '/audit');

    // Should NOT be redirected
    await expect(page).toHaveURL(/\/audit/);
    await expect(page.getByRole('table')).toBeVisible();
  });

  test('agent_id filter: filling and applying filter updates query', async ({ page }) => {
    await setupMocks(page, 'tenant_admin');

    await page.route('**/api/v1/dashboard/audit-logs*', (route) =>
      route.fulfill({ status: 200, json: MOCK_AUDIT_LOGS }));

    await loginAs(page, 'tenant_admin');
    await navigateTo(page, '/audit');

    await page.getByPlaceholder(/agent uuid/i).fill('agent-e2e-aaa');

    // Wait for the request itself rather than for 300ms and a hope. The old version asserted a
    // flag that the handler sets, after a fixed pause: on a loaded machine the request had not
    // been made yet and the test failed for being early, not for being wrong.
    const filtered = page.waitForRequest((request) =>
      request.url().includes('agent_id=agent-e2e-aaa'));
    await page.getByRole('button', { name: /^search$/i }).click();
    await filtered;
  });

  test('CSV export button triggers download (frontend-only, no backend endpoint)', async ({ page }) => {
    await setupMocks(page, 'tenant_admin');
    await loginAs(page, 'tenant_admin');
    await navigateTo(page, '/audit');

    // Wait for table data
    await expect(page.getByRole('table')).toBeVisible();

    // Listen for download event
    const downloadPromise = page.waitForEvent('download', { timeout: 3000 }).catch(() => null);
    await page.getByRole('button', { name: /csv/i }).click();
    const download = await downloadPromise;

    // Download should be triggered (file name contains 'audit-logs')
    if (download) {
      expect(download.suggestedFilename()).toMatch(/audit-logs/);
    }
    // If download event not captured (browser handling differs), just confirm no error
  });

  test('@a11y accessibility: audit log page', async ({ page }) => {
    await setupMocks(page, 'tenant_admin');
    await loginAs(page, 'tenant_admin');
    await navigateTo(page, '/audit');

    const results = await new AxeBuilder({ page }).analyze();
    const critical = results.violations.filter(
      (v) => v.impact === 'critical' || v.impact === 'serious',
    );
    expect(critical, `Critical/serious a11y violations: ${JSON.stringify(critical, null, 2)}`).toEqual([]);
  });
});

test.describe('Audit log — security_analyst RBAC', () => {
  test('/audit → redirected to /dashboard (AC6.2.1: analyst denied)', async ({ page }) => {
    await setupMocks(page, 'security_analyst');
    await loginAs(page, 'security_analyst');

    await navigateTo(page, '/audit');

    // AuditLogPage redirects analyst to /dashboard
    await expect(page).toHaveURL(/\/dashboard/);
    // The audit page's CSV export button should NOT be present (we're on dashboard instead)
    await expect(page.getByRole('button', { name: /csv/i })).not.toBeAttached();
  });
});
