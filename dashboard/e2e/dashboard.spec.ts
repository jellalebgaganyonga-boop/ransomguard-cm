/**
 * e2e/dashboard.spec.ts — EPIC-DASHBOARD role-based routing
 * AC2.x: Each role sees the correct dashboard variant.
 */

import { test, expect, AxeBuilder, setupMocks, loginAs } from './fixtures/index';

test.describe('Dashboard routing by role', () => {
  test('tenant_admin → Executive dashboard (Conformité réglementaire visible)', async ({ page }) => {
    await setupMocks(page, 'tenant_admin');
    await loginAs(page, 'tenant_admin');

    // Executive dashboard has compliance section (fr.json: dashboard.exec.compliance)
    await expect(page.getByText(/conformité réglementaire/i)).toBeVisible();
  });

  test('security_analyst → Operational dashboard (FLUX D\'ALERTES visible)', async ({ page }) => {
    await setupMocks(page, 'security_analyst');
    await loginAs(page, 'security_analyst');

    // Operational dashboard has live alert feed (fr.json: dashboard.ops.feed.title)
    await expect(page.getByText(/flux d'alertes/i)).toBeVisible();
  });

  test('read_only_auditor → Read-only dashboard (Mode auditeur notice visible)', async ({ page }) => {
    await setupMocks(page, 'read_only_auditor');
    await loginAs(page, 'read_only_auditor');

    // Read-only dashboard has auditor notice (fr.json: dashboard.readonly.notice)
    await expect(page.getByText(/mode auditeur/i)).toBeVisible();
  });

  test('@a11y accessibility: executive dashboard', async ({ page }) => {
    await setupMocks(page, 'tenant_admin');
    await loginAs(page, 'tenant_admin');

    const results = await new AxeBuilder({ page }).analyze();
    const critical = results.violations.filter(
      (v) => v.impact === 'critical' || v.impact === 'serious',
    );
    expect(critical, `Critical/serious a11y violations: ${JSON.stringify(critical, null, 2)}`).toEqual([]);
  });

  test('@a11y accessibility: operational dashboard', async ({ page }) => {
    await setupMocks(page, 'security_analyst');
    await loginAs(page, 'security_analyst');

    const results = await new AxeBuilder({ page }).analyze();
    const critical = results.violations.filter(
      (v) => v.impact === 'critical' || v.impact === 'serious',
    );
    expect(critical, `Critical/serious a11y violations: ${JSON.stringify(critical, null, 2)}`).toEqual([]);
  });

  test('@a11y accessibility: read-only dashboard', async ({ page }) => {
    await setupMocks(page, 'read_only_auditor');
    await loginAs(page, 'read_only_auditor');

    const results = await new AxeBuilder({ page }).analyze();
    const critical = results.violations.filter(
      (v) => v.impact === 'critical' || v.impact === 'serious',
    );
    expect(critical, `Critical/serious a11y violations: ${JSON.stringify(critical, null, 2)}`).toEqual([]);
  });
});
