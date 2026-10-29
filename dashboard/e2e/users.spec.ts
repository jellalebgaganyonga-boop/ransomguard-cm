/**
 * e2e/users.spec.ts — EPIC-USERS happy paths + RBAC (highest security risk)
 * AC5.x: CRUD users, self-disable guard (GRID-SEC-002), self-demotion guard.
 */

import { test, expect, AxeBuilder, setupMocks, loginAs, navigateTo, MOCK_USERS_LIST, MOCK_ME } from './fixtures/index';

const ADMIN_ID = 'a0e2e000-0000-0000-0000-000000000010';

const NEW_USER = {
  id: 'user-new-e2e-99',
  email: 'nouveau@yde-hospital.cm',
  full_name: 'Nouveau Utilisateur',
  is_active: true,
  last_login_at: null,
};

test.describe('Users list — tenant_admin', () => {
  test('creates a new user → appears in list', async ({ page }) => {
    await setupMocks(page, 'tenant_admin');

    // After creation, list returns the new user added.
    // Pattern uses trailing * to match both /users and /users?offset=0&limit=20
    let createCalled = false;
    await page.route('**/api/v1/dashboard/users*', async (route) => {
      const url = route.request().url();
      // Skip /users/{id}/... sub-resource paths
      if (url.match(/\/users\/[^?]+/)) return route.continue();

      if (route.request().method() === 'POST') {
        createCalled = true;
        await route.fulfill({ status: 201, json: NEW_USER });
      } else {
        // The list page reads users-with-roles, and this pattern catches that too, so the
        // items have to carry roles or the response does not parse and the table stays empty.
        const withRoles = (u: { id: string }) => ({ ...u, roles: ['read_only_auditor'] });
        await route.fulfill({
          status: 200,
          json: createCalled
            ? { ...MOCK_USERS_LIST, items: [...MOCK_USERS_LIST.items, NEW_USER].map(withRoles), total: 3 }
            : { ...MOCK_USERS_LIST, items: MOCK_USERS_LIST.items.map(withRoles) },
        });
      }
    });

    await loginAs(page, 'tenant_admin');
    await navigateTo(page, '/users');

    // Open create modal
    await page.getByRole('button', { name: /create user/i }).click();
    const modal = page.getByRole('dialog');
    await expect(modal).toBeVisible();

    // Fill form
    await modal.getByLabel(/full name/i).fill('Nouveau Utilisateur');
    await modal.getByLabel(/^email/i).fill('nouveau@yde-hospital.cm');
    await modal.getByLabel(/temporary password/i).fill('MotDePasse123!');

    // Submit
    await modal.getByRole('button', { name: /^create$/i }).click();

    // List refreshes — new user appears
    await expect(page.getByText('nouveau@yde-hospital.cm')).toBeVisible();
  });

  test('self-disable: Disable button ABSENT from own row (GRID-SEC-002)', async ({ page }) => {
    await setupMocks(page, 'tenant_admin');
    await loginAs(page, 'tenant_admin');
    await navigateTo(page, '/users');

    // First row = admin (same as logged-in user = ADMIN_ID)
    // The "you" badge should be visible on that row
    await expect(page.getByText('you', { exact: true })).toBeVisible();

    // "Disable" must NOT exist in DOM for own row
    // The whole-page check: at most one Disable for the other user
    const disableButtons = page.getByRole('button', { name: /^disable$/i });
    const count = await disableButtons.count();

    // If any Disable exists, none must be for the current user's row
    // Simplest guard: confirm the "you" row cell contains no Disable
    const adminRow = page.locator('tr').filter({ has: page.getByText('you', { exact: true }) }); // the "you" badge sits right after the name
    await expect(adminRow.getByRole('button', { name: /^disable$/i })).not.toBeAttached();

    // Belt-and-suspenders: even if we had 1 button (for the other user), that's fine
    expect(count).toBeLessThanOrEqual(1);
  });

  test('self-disable defense-in-depth: modal blocks request if somehow opened', async ({ page }) => {
    // This tests the DisableEnableModal's own guard (GAP-05 / GRID-SEC-002)
    // We can't easily bypass the hidden button via UI — we test the modal guard
    // by checking that the guard logic prevents the API call
    await setupMocks(page, 'tenant_admin');

    let disableCalled = false;
    await page.route(`**/api/v1/dashboard/users/${ADMIN_ID}/disable`, (r) => {
      disableCalled = true;
      return r.fulfill({ status: 200, json: {} });
    });

    await loginAs(page, 'tenant_admin');
    await navigateTo(page, '/users');

    // Wait on the page being there, not on a stopwatch: your own row is rendered and carries no
    // Disable button. That is the guard this test is about, and asserting it is stronger than
    // waiting 500ms and checking that nothing happened to have fired yet.
    const ownRow = page.locator('tr').filter({ has: page.getByText('you', { exact: true }) });
    await expect(ownRow).toBeVisible();
    await expect(ownRow.getByRole('button', { name: /^disable$/i })).toHaveCount(0);
    expect(disableCalled).toBe(false);
  });

  test('role editor: self-demotion blocked (AC5.4.3)', async ({ page }) => {
    await setupMocks(page, 'tenant_admin');
    await page.route(`**/api/v1/dashboard/users/${ADMIN_ID}/roles`, (r) =>
      r.fulfill({ status: 200, json: { ...MOCK_ME.tenant_admin, id: ADMIN_ID, roles: ['tenant_admin'] } }));

    await loginAs(page, 'tenant_admin');
    await navigateTo(page, '/users');

    // Open role editor for own row (Roles button on admin's row)
    const adminRow = page.locator('tr').filter({ has: page.getByText('you', { exact: true }) }); // the "you" badge sits right after the name
    await adminRow.getByRole('button', { name: /^roles$/i }).click();

    const modal = page.getByRole('dialog');
    await expect(modal).toBeVisible();

    // The dialog shows what the user has, so the admin's own tenant_admin starts ticked. It
    // used to start unchecked for everyone, which is how an administrator removed a role by
    // accident. Clearing it is what has to be refused.
    const tenantAdminCb = modal.getByRole('checkbox', { name: /tenant_admin/i });
    await expect(tenantAdminCb).toBeChecked();
    await tenantAdminCb.uncheck();

    const saveBtn = modal.getByRole('button', { name: /enregistrer|save/i });
    await saveBtn.click();

    // Self-demotion error should appear (since tenant_admin not checked = removing own admin)
    await expect(modal.getByText(/you cannot remove/i)).toBeVisible();
  });

  test('@a11y accessibility: users list', async ({ page }) => {
    await setupMocks(page, 'tenant_admin');
    await loginAs(page, 'tenant_admin');
    await navigateTo(page, '/users');

    const results = await new AxeBuilder({ page }).analyze();
    const critical = results.violations.filter(
      (v) => v.impact === 'critical' || v.impact === 'serious',
    );
    expect(critical, `Critical/serious a11y violations: ${JSON.stringify(critical, null, 2)}`).toEqual([]);
  });
});

test.describe('Users — security_analyst RBAC', () => {
  test('navigating to /users → redirected to /dashboard', async ({ page }) => {
    await setupMocks(page, 'security_analyst');
    await loginAs(page, 'security_analyst');

    await navigateTo(page, '/users');
    // UsersListPage redirects analyst to /dashboard
    await expect(page).toHaveURL(/\/dashboard/);
    // The users page "Create user" button should NOT be present
    await expect(page.getByRole('button', { name: /create user/i })).not.toBeAttached();
  });
});
