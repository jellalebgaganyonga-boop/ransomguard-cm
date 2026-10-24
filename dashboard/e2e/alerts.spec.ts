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
    await page.getByRole('button', { name: /^acknowledge$/i }).click();

    // Modal opens
    await expect(page.getByRole('dialog')).toBeVisible();

    // Confirm without note (default justification sent)
    await page.getByRole('dialog').getByRole('button', { name: /confirm|acknowledge/i }).click();

    // After mutation: detail refetch returns Investigating -> the status badge
    // reads "Investigating".
    // exact: true is required. Once the refetched status history renders, a
    // loose /investigating/i also matches the history line "New -> Investigating"
    // and Playwright's strict mode rejects the two-element match. Locally the
    // assertion usually ran before the history painted; on CI it did not.
    await expect(page.getByText('Investigating', { exact: true }).first()).toBeVisible();
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

    await page.getByRole('button', { name: /close incident/i }).click();
    await expect(page.getByRole('dialog')).toBeVisible();

    // Select resolution category (shadcn Select — click trigger then option)
    await page.getByRole('combobox').click();
    await page.getByRole('option', { name: /true positive.*contained/i }).click();

    // Type resolution notes (min 20 chars)
    const notes = 'Investigation complète : processus isolé et neutralisé avec succès.';
    await page.getByRole('textbox').fill(notes);

    // Submit
    await page.getByRole('dialog').getByRole('button', { name: /close incident/i }).click();

    // Use exact match: "Resolved" is the status badge; "Resolution notes" in modal labels also matches /resol/i
    await expect(page.getByText('Resolved', { exact: true })).toBeVisible();
  });

  test('Close submit disabled until notes >= 20 chars (AC3.4.1)', async ({ page }) => {
    await setupMocks(page, 'security_analyst');
    await page.route('**/api/v1/dashboard/alerts/alert-e2e-001', (r) =>
      r.fulfill({ status: 200, json: MOCK_ALERT_INVESTIGATING }));

    await loginAs(page, 'security_analyst');
    await navigateTo(page, '/alerts/alert-e2e-001');
    await page.getByRole('button', { name: /close incident/i }).click();

    const submitBtn = page.getByRole('dialog').getByRole('button', { name: /close incident/i });

    // Before category + notes: submit is disabled
    await expect(submitBtn).toBeDisabled();

    // Fill category but short notes (< 20 chars) — shadcn Select inside Dialog: force click on option
    await page.getByRole('combobox').click();
    await page.getByRole('option', { name: /true positive.*contained/i }).click({ force: true });
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
    await expect(page.getByRole('button', { name: /^acknowledge$/i })).not.toBeAttached();
    await expect(page.getByRole('button', { name: /close incident/i })).not.toBeAttached();

    // Read-only notice visible instead
    await expect(page.getByText(/read-only mode/i)).toBeVisible();
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

// ADR-025: the alert arrives first; "Who did it?" follows, from the agent's complement.
test.describe('Alert detail — attribution (ADR-025)', () => {
  for (const [label, attribution, expected] of [
    ['pending', { state: 'pending' }, 'Attribution in progress…'],
    [
      'resolved',
      { state: 'resolved', process_id: 4242, process_name: 'evil', process_path: String.raw`C:\Temp\evil.exe`, process_created_at: '2026-10-21T09:57:00+00:00' },
      '— PID 4242',
    ],
    ['unavailable', { state: 'unavailable', reason: 'system_pid' }, '— written by the system (deferred write or memory-mapped file)'],
    ['ambiguous', { state: 'ambiguous' }, 'Several processes wrote this file: none is named'],
  ] as const) {
    test(`@a11y ${label}`, async ({ page }) => {
      await setupMocks(page, 'security_analyst');
      await page.route('**/api/v1/dashboard/alerts/alert-e2e-001', (r) =>
        r.fulfill({ status: 200, json: { ...MOCK_ALERT_NEW, detected_at: new Date().toISOString(), attribution } }));
      await loginAs(page, 'security_analyst');
      await navigateTo(page, '/alerts/alert-e2e-001');

      const section = page.getByTestId('alert-attribution');
      await expect(section.getByText('Who did it?')).toBeVisible();
      await expect(section.getByText(expected)).toBeVisible();

      const results = await new AxeBuilder({ page }).include('[data-testid="alert-attribution"]').analyze();
      const critical = results.violations.filter((v) => v.impact === 'critical' || v.impact === 'serious');
      expect(critical, `Critical/serious a11y violations: ${JSON.stringify(critical, null, 2)}`).toEqual([]);
    });
  }

  test('an alert without attribution shows no such section', async ({ page }) => {
    await setupMocks(page, 'security_analyst');
    await loginAs(page, 'security_analyst');
    await navigateTo(page, '/alerts/alert-e2e-001');

    await expect(page.getByText(MOCK_ALERT_NEW.summary)).toBeVisible();
    await expect(page.getByTestId('alert-attribution')).toHaveCount(0);
  });
});

test.describe('Alert detail — response (ADR-026, J1 § 1.3)', () => {
  const RESOLVED = {
    state: 'resolved', process_id: 4242, process_name: 'evil',
    process_path: String.raw`C:\Temp\evil.exe`, process_created_at: '2026-10-21T09:57:00+00:00',
  };
  const SUSPENDED = {
    alert_id: 'alert-e2e-resp', alert_type: 'SentinelProcessSuspended', response: 'process_suspended',
    detected_at: '2026-10-21T09:57:05+00:00', suspension_id: '5a6d1d3e-0000-4000-8000-000000000001',
    process_id: 4242, process_name: 'evil',
  };

  test('a suspended writer is shown, and tenant_admin can ask for the resume', async ({ page }) => {
    await setupMocks(page, 'tenant_admin');
    await page.route('**/api/v1/dashboard/alerts/alert-e2e-001', (r) =>
      r.fulfill({ status: 200, json: { ...MOCK_ALERT_NEW, attribution: RESOLVED, responses: [SUSPENDED], commands: [] } }));
    await loginAs(page, 'tenant_admin');
    await navigateTo(page, '/alerts/alert-e2e-001');

    const section = page.getByTestId('alert-response');
    await expect(section).toHaveAttribute('data-state', 'suspended');
    await expect(section.getByText(/Process suspended: evil \(PID 4242\)/)).toBeVisible();
    await expect(section.getByRole('button', { name: /resume the process/i })).toBeVisible();
    await expect(section.getByRole('button', { name: /suspend the detected process/i })).toHaveCount(0);
  });

  test('"Suspend" sends the command, then shows the agent\'s own answer: not executed, with its reason', async ({ page }) => {
    await setupMocks(page, 'tenant_admin');
    const command = {
      id: 'cmd-e2e-suspend', command_type: 'suspend_process', status: 'Pending',
      issued_at: '2026-10-21T10:00:00+00:00', dispatched_at: null, completed_at: null, success: null, result: null,
    };
    let sent: unknown = null;
    let phase = 0; // 0: nothing sent; 1: sent; 2: answered by the agent
    await page.route('**/api/v1/dashboard/alerts/alert-e2e-001', (r) => {
      const commands = phase === 0 ? [] : phase === 1 ? [command]
        : [{ ...command, status: 'Completed', completed_at: '2026-10-21T10:00:20+00:00', success: false, result: { reason: 'pid_reused' } }];
      return r.fulfill({ status: 200, json: { ...MOCK_ALERT_NEW, attribution: RESOLVED, responses: [], commands } });
    });
    await page.route('**/api/v1/dashboard/commands', (r) => {
      sent = r.request().postDataJSON();
      phase = 1;
      return r.fulfill({ status: 201, json: { id: command.id, agent_id: MOCK_ALERT_NEW.agent_id, command_type: 'suspend_process', status: 'Pending', issued_at: command.issued_at } });
    });
    await loginAs(page, 'tenant_admin');
    await navigateTo(page, '/alerts/alert-e2e-001');

    const section = page.getByTestId('alert-response');
    await section.getByRole('button', { name: /suspend the detected process/i }).click();
    await section.getByRole('button', { name: /confirm the suspension/i }).click();

    // The console sends the alert id only: the PID comes from the alert's attribution, on the GRID.
    expect(sent).toEqual({ agent_id: MOCK_ALERT_NEW.agent_id, command_type: 'suspend_process', parameters: { alert_id: 'alert-e2e-001' } });
    const status = page.getByTestId('alert-command-status');
    await expect(status).toHaveAttribute('data-status', 'sent');
    await expect(section.getByRole('button')).toHaveCount(0); // nothing else while the agent has not answered

    phase = 2; // the page polls every 5 s while a command is open
    await expect(status).toHaveAttribute('data-status', 'not_executed', { timeout: 15_000 });
    await expect(status).toContainText('not executed by the agent');
    await expect(status).toContainText('the process number was reassigned');
  });

  test('security_analyst sees the response but no command button', async ({ page }) => {
    await setupMocks(page, 'security_analyst');
    await page.route('**/api/v1/dashboard/alerts/alert-e2e-001', (r) =>
      r.fulfill({ status: 200, json: { ...MOCK_ALERT_NEW, attribution: RESOLVED, responses: [SUSPENDED], commands: [] } }));
    await loginAs(page, 'security_analyst');
    await navigateTo(page, '/alerts/alert-e2e-001');

    const section = page.getByTestId('alert-response');
    await expect(section.getByText(/Process suspended/)).toBeVisible();
    await expect(section.getByRole('button')).toHaveCount(0);
  });
});
